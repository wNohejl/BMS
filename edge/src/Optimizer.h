// Optimizer.h — rule-based energy optimization (v1):
//   • peak electricity hours (2–6pm): relax cooling setpoints by +2°F
//   • the hour before peak: pre-cool by -1°F while power is cheaper
// Offsets only apply in schedule mode — manual setpoints are never overridden.
// Emits SetpointChanged events so every adjustment is observable downstream.
// v2: replace the rules with a price feed + moving-average baseline model.
#pragma once
#include "EventBus.h"
#include "ZoneController.h"

#include <vector>

class Optimizer {
public:
    Optimizer(EventBus& bus, std::vector<ZoneController*> zones);

    // Called periodically by the scheduler.
    void evaluate(long long nowMs);

    double currentOffsetF() const { return currentOffsetF_; }

    static constexpr int kPeakStartHour = 14;
    static constexpr int kPeakEndHour = 18;
    static constexpr double kPeakOffsetF = 2.0;
    static constexpr double kPreCoolOffsetF = -1.0;

private:
    static int hourOfDay(long long nowMs);

    EventBus& bus_;
    std::vector<ZoneController*> zones_;
    double currentOffsetF_ = 0.0;
};
