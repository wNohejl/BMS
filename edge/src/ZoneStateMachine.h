// ZoneStateMachine.h — per-zone HVAC state machine with a hysteresis deadband
// and a minimum-state hold time (equipment protection: compressors must not
// short-cycle). Pure logic, no I/O — fully covered by `edge_agent --selftest`.
#pragma once
#include <string>

enum class HvacState { Idle, Cooling, Heating };

const char* hvacStateToString(HvacState state);

class ZoneStateMachine {
public:
    explicit ZoneStateMachine(std::string zoneId, long long minStateMs = 3 * 60 * 1000);

    // Evaluate temperature against the setpoint; returns true if the state changed.
    bool update(double tempF, double setpointF, long long nowMs);

    // Fail-safe: force Idle immediately, bypassing the hold time. Used when the
    // zone's sensor goes silent — running blind is worse than short-cycling.
    bool forceIdle(long long nowMs);

    HvacState state() const { return state_; }
    const std::string& zoneId() const { return zoneId_; }

    static constexpr double kDeadbandF = 1.0; // ±1°F hysteresis around the setpoint

private:
    bool transitionTo(HvacState next, long long nowMs);

    std::string zoneId_;
    long long minStateMs_;
    HvacState state_ = HvacState::Idle;
    long long lastTransitionMs_ = 0;
};
