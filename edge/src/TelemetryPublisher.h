// TelemetryPublisher.h — outbound channel of the engine. Publishes telemetry
// batches (with an IoT Hub F1 quota guard for 403002) and engine status
// snapshots. --local mode writes JSON files to disk for the zero-cost dev loop.
#pragma once
#include "TelemetryBatch.h"

#include <queue>
#include <string>

class TelemetryPublisher {
public:
    TelemetryPublisher(const std::string& connectionString, bool localMode = false,
                       bool stdoutMode = false);
    ~TelemetryPublisher();

    void publishBatch(const TelemetryBatch& batch);

    // Engine control-state snapshot ({"zones":[{zoneId,state,setpointF,...}]}).
    // Written as status_*.json locally; sent as a plain message in cloud mode.
    void publishStatus(const std::string& json);

private:
    void publishToCloud(const TelemetryBatch& batch);
    bool sendToCloud(const std::string& json);
    void writeJsonFile(const std::string& json, const std::string& stemPrefix);
    void handleQuotaExceeded(const TelemetryBatch& batch); // 403002 IoTHubQuotaExceeded
    void drainLocalQueue();  // replays queued batches after the quota resets
    std::string outputDir() const;
    static long long nowMs();
    static long long nextMidnightUtcMs();

    bool localMode_;
    bool stdoutMode_;
    bool quotaExhausted_ = false;
    long long quotaResetMs_ = 0;   // F1 quota resets at midnight UTC
    void* iotHandle_ = nullptr;    // IOTHUB_DEVICE_CLIENT_LL_HANDLE
    std::queue<TelemetryBatch> localQueue_;

    static constexpr const char* LOCAL_OUTPUT_DIR = "/tmp/edgemonitor/readings/";
};
