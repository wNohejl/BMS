// IDeviceActuator.h — the device-layer boundary of the control flow
// (Dashboard → API → C++ engine → Device). BuildingSimulation implements it
// for v1; a BACnetActuator (WriteProperty to AO/BO objects) lands here in v1.5
// without touching the engine, mirroring the ISensorReader abstraction.
#pragma once
#include "ZoneStateMachine.h"

#include <string>

class IDeviceActuator {
public:
    virtual ~IDeviceActuator() = default;
    virtual void apply(const std::string& zoneId, HvacState state) = 0;
};
