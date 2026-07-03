#include "ZoneStateMachine.h"

const char* hvacStateToString(HvacState state) {
    switch (state) {
        case HvacState::Idle:    return "idle";
        case HvacState::Cooling: return "cooling";
        case HvacState::Heating: return "heating";
    }
    return "unknown";
}

ZoneStateMachine::ZoneStateMachine(std::string zoneId, long long minStateMs)
    : zoneId_(std::move(zoneId)), minStateMs_(minStateMs) {}

bool ZoneStateMachine::transitionTo(HvacState next, long long nowMs) {
    state_ = next;
    lastTransitionMs_ = nowMs;
    return true;
}

bool ZoneStateMachine::forceIdle(long long nowMs) {
    if (state_ == HvacState::Idle) return false;
    return transitionTo(HvacState::Idle, nowMs);
}

bool ZoneStateMachine::update(double tempF, double setpointF, long long nowMs) {
    // Equipment protection: hold every state for at least minStateMs_ so the
    // compressor never short-cycles (first transition is always allowed).
    if (lastTransitionMs_ != 0 && nowMs - lastTransitionMs_ < minStateMs_) {
        return false;
    }

    switch (state_) {
        case HvacState::Idle:
            if (tempF > setpointF + kDeadbandF) return transitionTo(HvacState::Cooling, nowMs);
            if (tempF < setpointF - kDeadbandF) return transitionTo(HvacState::Heating, nowMs);
            return false;
        case HvacState::Cooling:
            if (tempF <= setpointF) return transitionTo(HvacState::Idle, nowMs);
            return false;
        case HvacState::Heating:
            if (tempF >= setpointF) return transitionTo(HvacState::Idle, nowMs);
            return false;
    }
    return false;
}
