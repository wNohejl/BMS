// BACnetSensorReader.h — stub for v1; implemented in v1.5.
// v1.5 plan: link against bacnet-stack (github.com/bacnet-stack/bacnet-stack),
// BACnet/IP discovery → ReadProperty → map object model to SensorReading.
#pragma once
#include "ISensorReader.h"

class BACnetSensorReader : public ISensorReader {
public:
    explicit BACnetSensorReader(const std::string& networkInterface);
    std::vector<SensorReading> readAll() override;
    bool isAvailable() const override;

private:
    std::string networkInterface_;
};
