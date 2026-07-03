// SimulatedSensorReader.h — v1: generates sine-wave sensor data.
#pragma once
#include "ISensorReader.h"

class SimulatedSensorReader : public ISensorReader {
public:
    std::vector<SensorReading> readAll() override;
    bool isAvailable() const override { return true; }

private:
    double sineWave(double base, double amplitude, double periodSeconds) const;
    long long nowMs() const;
};
