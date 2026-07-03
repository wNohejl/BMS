#include "TelemetryPublisher.h"

#include <chrono>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <thread>

#ifdef USE_AZURE_IOT
#include "iothub.h"
#include "iothub_device_client_ll.h"
#include "iothub_message.h"
#include "iothubtransportmqtt.h"
#endif

namespace fs = std::filesystem;

long long TelemetryPublisher::nowMs() {
    using namespace std::chrono;
    return duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
}

long long TelemetryPublisher::nextMidnightUtcMs() {
    constexpr long long dayMs = 24LL * 60 * 60 * 1000;
    return (nowMs() / dayMs + 1) * dayMs;
}

TelemetryPublisher::TelemetryPublisher(const std::string& connectionString, bool localMode,
                                       bool stdoutMode)
    : localMode_(localMode), stdoutMode_(stdoutMode) {
    if (localMode_ || stdoutMode_) {
        return; // zero-cost dev loop — no Azure connection
    }

#ifdef USE_AZURE_IOT
    IoTHub_Init();
    iotHandle_ = IoTHubDeviceClient_LL_CreateFromConnectionString(connectionString.c_str(),
                                                                  MQTT_Protocol);
    if (iotHandle_ == nullptr) {
        throw std::runtime_error("Failed to create IoT Hub client from connection string");
    }
#else
    (void)connectionString;
    throw std::runtime_error(
        "Cloud mode requires the Azure IoT C SDK. Rebuild with -DUSE_AZURE_IOT=ON "
        "(see edge/CMakeLists.txt), or run with --local for the $0 dev loop.");
#endif
}

TelemetryPublisher::~TelemetryPublisher() {
#ifdef USE_AZURE_IOT
    if (iotHandle_ != nullptr) {
        IoTHubDeviceClient_LL_Destroy(static_cast<IOTHUB_DEVICE_CLIENT_LL_HANDLE>(iotHandle_));
        IoTHub_Deinit();
    }
#endif
}

void TelemetryPublisher::publishBatch(const TelemetryBatch& batch) {
    if (batch.estimatedSizeBytes() > TelemetryBatch::MAX_BATCH_BYTES) {
        std::cerr << "[edge] batch exceeds " << TelemetryBatch::MAX_BATCH_BYTES
                  << " bytes — dropping to stay under the IoT Hub 256KB limit\n";
        return;
    }

    if (stdoutMode_) {
        std::cout << batch.toJson() << std::endl;
        return;
    }
    if (localMode_) {
        writeJsonFile(batch.toJson(), "batch_");
        std::cout << "[edge] wrote batch (" << batch.readings.size() << " readings)\n";
        return;
    }

    // Cloud mode with quota guard: while the F1 daily quota is exhausted,
    // queue locally; the counter resets at midnight UTC.
    if (quotaExhausted_ && nowMs() < quotaResetMs_) {
        localQueue_.push(batch);
        std::cerr << "[edge] quota exhausted — queued batch ("
                  << localQueue_.size() << " pending until midnight UTC)\n";
        return;
    }
    quotaExhausted_ = false;
    drainLocalQueue();
    publishToCloud(batch);
}

void TelemetryPublisher::publishStatus(const std::string& json) {
    if (stdoutMode_) {
        std::cout << json << std::endl;
        return;
    }
    if (localMode_) {
        writeJsonFile(json, "status_");
        return;
    }
    // Status is a best-effort snapshot — a fresh one follows within the read
    // interval, so no quota queue; just try to send.
    if (!sendToCloud(json)) {
        std::cerr << "[edge] status send failed — next snapshot follows shortly\n";
    }
}

std::string TelemetryPublisher::outputDir() const {
    if (const char* dir = std::getenv("EDGEMONITOR_OUTPUT_DIR")) {
        return dir;
    }
    return LOCAL_OUTPUT_DIR;
}

void TelemetryPublisher::writeJsonFile(const std::string& json, const std::string& stemPrefix) {
    const fs::path dir = outputDir();
    fs::create_directories(dir);

    const std::string stem = stemPrefix + std::to_string(nowMs());
    const fs::path tmpPath = dir / (stem + ".tmp");
    const fs::path finalPath = dir / (stem + ".json");

    {
        std::ofstream out(tmpPath, std::ios::binary);
        out << json;
    }
    // Write-then-rename so the API's file listener never reads a half-written file.
    fs::rename(tmpPath, finalPath);
}

void TelemetryPublisher::handleQuotaExceeded(const TelemetryBatch& batch) {
    quotaExhausted_ = true;
    quotaResetMs_ = nextMidnightUtcMs();
    localQueue_.push(batch);
    std::cerr << "[edge] 403002 IoTHubQuotaExceeded — F1 daily quota hit. "
              << "Queuing locally until midnight UTC.\n";
}

void TelemetryPublisher::drainLocalQueue() {
    while (!localQueue_.empty() && !quotaExhausted_) {
        TelemetryBatch queued = localQueue_.front();
        localQueue_.pop();
        publishToCloud(queued);
    }
}

void TelemetryPublisher::publishToCloud(const TelemetryBatch& batch) {
    if (sendToCloud(batch.toJson())) {
        std::cout << "[edge] published batch (" << batch.readings.size()
                  << " readings) to IoT Hub\n";
    } else {
        handleQuotaExceeded(batch);
    }
}

#ifdef USE_AZURE_IOT

namespace {
struct SendContext {
    bool completed = false;
    IOTHUB_CLIENT_CONFIRMATION_RESULT result = IOTHUB_CLIENT_CONFIRMATION_ERROR;
};

void sendConfirmationCallback(IOTHUB_CLIENT_CONFIRMATION_RESULT result, void* userContext) {
    auto* ctx = static_cast<SendContext*>(userContext);
    ctx->result = result;
    ctx->completed = true;
}
} // namespace

bool TelemetryPublisher::sendToCloud(const std::string& json) {
    auto handle = static_cast<IOTHUB_DEVICE_CLIENT_LL_HANDLE>(iotHandle_);

    IOTHUB_MESSAGE_HANDLE message = IoTHubMessage_CreateFromString(json.c_str());
    IoTHubMessage_SetContentTypeSystemProperty(message, "application/json");
    IoTHubMessage_SetContentEncodingSystemProperty(message, "utf-8");

    SendContext ctx;
    if (IoTHubDeviceClient_LL_SendEventAsync(handle, message, sendConfirmationCallback, &ctx) !=
        IOTHUB_CLIENT_OK) {
        IoTHubMessage_Destroy(message);
        return false;
    }
    IoTHubMessage_Destroy(message);

    // Pump the LL client until the send is confirmed (or give up after ~15s —
    // a hang here is the classic F1 403002 quota symptom).
    for (int i = 0; i < 1500 && !ctx.completed; ++i) {
        IoTHubDeviceClient_LL_DoWork(handle);
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }

    return ctx.completed && ctx.result == IOTHUB_CLIENT_CONFIRMATION_OK;
}

#else

bool TelemetryPublisher::sendToCloud(const std::string&) {
    // Unreachable: the constructor throws in cloud mode when built without the SDK.
    return false;
}

#endif
