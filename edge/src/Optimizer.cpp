#include "Optimizer.h"

Optimizer::Optimizer(EventBus& bus, std::vector<ZoneController*> zones)
    : bus_(bus), zones_(std::move(zones)) {}

int Optimizer::hourOfDay(long long nowMs) {
    return static_cast<int>((nowMs / (60LL * 60 * 1000)) % 24);
}

void Optimizer::evaluate(long long nowMs) {
    const int hour = hourOfDay(nowMs);

    double offset = 0.0;
    if (hour >= kPeakStartHour && hour < kPeakEndHour) {
        offset = kPeakOffsetF;
    } else if (hour == kPeakStartHour - 1) {
        offset = kPreCoolOffsetF;
    }

    if (offset == currentOffsetF_) {
        return;
    }
    currentOffsetF_ = offset;

    for (auto* zone : zones_) {
        zone->setOptimizerOffset(offset);
        bus_.publish({EventType::SetpointChanged, zone->zoneId(), "optimizer",
                      zone->effectiveSetpointF(nowMs), nowMs, ""});
    }
}
