#include "TelemetryBatch.h"

#include <iomanip>
#include <sstream>

const char* sensorTypeToString(SensorType type) {
    switch (type) {
        case SensorType::Temperature: return "temperature";
        case SensorType::PowerDraw:   return "power";
        case SensorType::Occupancy:   return "occupancy";
    }
    return "unknown";
}

std::string TelemetryBatch::toJson() const {
    std::ostringstream out;
    out << "{\"tenantId\":\"" << tenantId << "\","
        << "\"deviceId\":\"" << deviceId << "\","
        << "\"readings\":[";

    for (size_t i = 0; i < readings.size(); ++i) {
        const SensorReading& r = readings[i];
        if (i > 0) out << ",";
        out << "{\"sensorType\":\"" << sensorTypeToString(r.type) << "\","
            << "\"value\":" << std::fixed << std::setprecision(2) << r.value << ","
            << "\"unit\":\"" << r.unit << "\","
            << "\"zoneId\":\"" << r.zoneId << "\","
            << "\"deviceId\":\"" << r.deviceId << "\","
            << "\"timestampMs\":" << r.timestampMs << "}";
    }

    out << "]}";
    return out.str();
}

size_t TelemetryBatch::estimatedSizeBytes() const {
    return toJson().size();
}
