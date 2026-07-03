// TelemetryBatch.h — batches multiple readings into one MQTT payload.
// One payload per 30s interval keeps message count within the IoT Hub F1
// free-tier limit (8,000 msgs/day) instead of burning quota per reading.
#pragma once
#include "ISensorReader.h"

#include <string>
#include <vector>

const char* sensorTypeToString(SensorType type);

struct TelemetryBatch {
    std::vector<SensorReading> readings;
    std::string tenantId = "tenant-demo";
    std::string deviceId = "edge-device-01";

    std::string toJson() const;
    size_t estimatedSizeBytes() const;

    static constexpr size_t MAX_BATCH_BYTES = 200 * 1024; // stay under IoT Hub's 256KB message limit
};
