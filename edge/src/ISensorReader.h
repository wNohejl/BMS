// ISensorReader.h — the key abstraction of the project.
// SimulatedSensorReader implements it for v1 (portfolio);
// BACnetSensorReader implements it for v1.5 (real buildings).
// TelemetryPublisher never knows which one it's talking to.
#pragma once
#include <string>
#include <vector>

enum class SensorType { Temperature, PowerDraw, Occupancy };

struct SensorReading {
    SensorType type;
    double value;
    std::string unit;
    std::string zoneId;
    std::string deviceId;
    long long timestampMs;
};

class ISensorReader {
public:
    virtual ~ISensorReader() = default;
    virtual std::vector<SensorReading> readAll() = 0;
    virtual bool isAvailable() const = 0;
};
