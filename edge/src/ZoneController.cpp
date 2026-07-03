#include "ZoneController.h"

ZoneController::ZoneController(std::string zoneId, EventBus& bus, IDeviceActuator& actuator,
                               long long minStateMs)
    : zoneId_(std::move(zoneId)), bus_(bus), actuator_(actuator),
      stateMachine_(zoneId_, minStateMs) {}

int ZoneController::hourOfDay(long long nowMs) {
    return static_cast<int>((nowMs / (60LL * 60 * 1000)) % 24);
}

bool ZoneController::occupiedNow(long long nowMs) const {
    return schedule_.isOccupied(hourOfDay(nowMs));
}

double ZoneController::effectiveSetpointF(long long nowMs) const {
    if (manualSetpointF_ >= 0) {
        // A manual command is an explicit user decision — the optimizer
        // must not fight it.
        return manualSetpointF_;
    }
    return schedule_.setpointFor(hourOfDay(nowMs)) + optimizerOffsetF_;
}

void ZoneController::onTemperature(double tempF, long long nowMs) {
    const double setpoint = effectiveSetpointF(nowMs);
    if (stateMachine_.update(tempF, setpoint, nowMs)) {
        actuator_.apply(zoneId_, stateMachine_.state());
        bus_.publish({EventType::StateChanged, zoneId_,
                      hvacStateToString(stateMachine_.state()), tempF, nowMs, ""});
    }
}

void ZoneController::failSafe(long long nowMs) {
    if (stateMachine_.forceIdle(nowMs)) {
        actuator_.apply(zoneId_, HvacState::Idle);
        bus_.publish({EventType::StateChanged, zoneId_, hvacStateToString(HvacState::Idle),
                      0.0, nowMs, "fail-safe: sensor offline"});
    }
}

void ZoneController::setManualSetpoint(double setpointF, long long nowMs) {
    manualSetpointF_ = setpointF;
    bus_.publish({EventType::SetpointChanged, zoneId_, "manual", setpointF, nowMs, ""});
}

void ZoneController::clearManualSetpoint(long long nowMs) {
    manualSetpointF_ = -1.0;
    bus_.publish({EventType::SetpointChanged, zoneId_, "schedule",
                  effectiveSetpointF(nowMs), nowMs, ""});
}

void ZoneController::setOptimizerOffset(double offsetF) {
    optimizerOffsetF_ = offsetF;
}
