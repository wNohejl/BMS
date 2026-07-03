// ZoneController.h — the control logic for one zone. Three inputs decide the
// effective setpoint (occupancy schedule, manual command from the .NET
// orchestrator, optimizer offset); the state machine turns temperature error
// into equipment state; the actuator pushes it across the device boundary.
#pragma once
#include "EventBus.h"
#include "IDeviceActuator.h"
#include "ZoneStateMachine.h"

#include <string>

struct SetpointSchedule {
    int occupiedStartHour = 8;
    int occupiedEndHour = 18;
    double occupiedSetpointF = 72.0;
    double unoccupiedSetpointF = 78.0; // setback saves energy when nobody's in

    bool isOccupied(int hourOfDay) const {
        return hourOfDay >= occupiedStartHour && hourOfDay < occupiedEndHour;
    }
    double setpointFor(int hourOfDay) const {
        return isOccupied(hourOfDay) ? occupiedSetpointF : unoccupiedSetpointF;
    }
};

class ZoneController {
public:
    ZoneController(std::string zoneId, EventBus& bus, IDeviceActuator& actuator,
                   long long minStateMs);

    // Called on every sensor read — the control evaluation cadence.
    void onTemperature(double tempF, long long nowMs);

    void setManualSetpoint(double setpointF, long long nowMs); // Dashboard → API → command
    void clearManualSetpoint(long long nowMs);                 // back to schedule
    void setOptimizerOffset(double offsetF);

    // Recovery behavior: when the zone's sensor goes silent, running equipment
    // blind is the risk — force Idle and let the alarm bring a human in.
    void failSafe(long long nowMs);

    double effectiveSetpointF(long long nowMs) const;
    bool occupiedNow(long long nowMs) const;
    HvacState state() const { return stateMachine_.state(); }
    const std::string& zoneId() const { return zoneId_; }

private:
    static int hourOfDay(long long nowMs);

    std::string zoneId_;
    EventBus& bus_;
    IDeviceActuator& actuator_;
    ZoneStateMachine stateMachine_;
    SetpointSchedule schedule_;
    double manualSetpointF_ = -1.0; // < 0 = follow the schedule
    double optimizerOffsetF_ = 0.0;
};
