// BACnetActuator.h — stub for v1.5; the outbound half of real-hardware
// integration. Mirrors BACnetSensorReader: implemented against bacnet-stack
// (github.com/bacnet-stack/bacnet-stack) with WriteProperty to the zone's
// AO/BO objects (damper position, unit enable). Because it implements
// IDeviceActuator, swapping it in for BuildingSimulation changes zero lines
// of engine, orchestration, or dashboard code.
#pragma once
#include "IDeviceActuator.h"

#include <string>

class BACnetActuator : public IDeviceActuator {
public:
    explicit BACnetActuator(const std::string& networkInterface);
    void apply(const std::string& zoneId, HvacState state) override;
    // v1.5: Who-Is discovery → per-site mapping file (zoneId → BACnet object ids)
    //       → WriteProperty present-value with priority array handling.

private:
    std::string networkInterface_;
};
