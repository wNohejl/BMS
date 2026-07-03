// AlarmMonitor.h — behavior-based alarm detection. It is never told which
// faults were injected; it watches what the building actually does and raises
// plain-English alarms from observed symptoms, the way a real BAS does:
//   sensor-offline        — a zone's temperature readings went stale
//   out-of-range          — a zone drifted > 4°F from its target
//   ineffective-equipment — equipment has been running but the temperature
//                           isn't responding (stuck damper / failing unit)
//   sensor-implausible    — analytical redundancy: the monitor integrates its
//                           own model estimate of each zone (from the building
//                           model + commanded state + outdoor temp) and flags
//                           sensors that diverge from expected behavior —
//                           this is what catches a drifting sensor that
//                           otherwise "looks healthy"
// Alarms auto-clear when the condition is no longer observed (recovery).
#pragma once
#include "EventBus.h"
#include "ZoneStateMachine.h"

#include <map>
#include <string>
#include <vector>

struct ZoneObservation {
    std::string zoneId;
    std::string zoneName;
    HvacState state = HvacState::Idle;
    double lastTempF = 0.0;
    long long lastTempMs = -1; // -1 = never seen a reading
    double setpointF = 0.0;
    double outdoorTempF = 0.0; // from the outdoor air sensor
    double leakFactor = 1.0;   // from the building model (config)
};

struct Alarm {
    std::string zoneId;
    std::string type;
    std::string severity; // "critical" | "warning"
    std::string message;  // plain English — shown directly to building owners
    long long sinceMs = 0;
};

class AlarmMonitor {
public:
    AlarmMonitor(EventBus& bus, long long readIntervalMs);

    void evaluate(const std::vector<ZoneObservation>& zones, long long nowMs);
    const std::vector<Alarm>& active() const { return active_; }

    static constexpr double kOutOfRangeF = 4.0;   // raise beyond this deviation
    static constexpr double kBackInRangeF = 3.0;  // clear inside this (hysteresis)
    // Expected progress while running, per second — half the healthy equipment
    // rate, so the judgment scales with how long we've been watching instead of
    // false-alarming at short observation windows (fast demo intervals).
    static constexpr double kMinImprovementFPerSec = 0.01;
    static constexpr int kStaleIntervals = 3;        // reads missed before "offline"
    static constexpr int kIneffectiveIntervals = 4;  // run time before judging progress

    // Analytical redundancy: nominal model coefficients (must match the design
    // values in BuildingSimulation — they're the shared building model, not a
    // peek at runtime ground truth) plus the residual thresholds.
    static constexpr double kNomLeakPerSec = 0.001;
    static constexpr double kNomCoolPerSec = 0.050;
    static constexpr double kNomHeatPerSec = 0.040;
    static constexpr double kAnchorPerSec = 0.002;   // slow pull toward the sensor so
                                                     // modest model error can't accumulate
    static constexpr double kImplausibleF = 3.0;     // raise beyond this residual
    static constexpr double kPlausibleAgainF = 2.0;  // clear inside this

    // Debounce — intermittent faults (chattering damper) must not make alarms
    // flap. An alarm stays active for a minimum time, and after clearing it
    // can't re-raise during a hold-off window.
    static constexpr int kMinActiveIntervals = 2;
    static constexpr int kHoldOffIntervals = 6;

private:
    struct RunTrend {
        HvacState state = HvacState::Idle;
        long long sinceMs = 0;
        double tempAtStart = 0.0;
    };

    struct ModelEstimate {
        bool initialized = false;
        double tempF = 0.0;
        long long lastMs = 0;
    };

    struct Debounce {
        long long raisedMs = -1;
        long long clearedMs = -1;
    };

    bool isActive(const std::string& zoneId, const std::string& type) const;
    void raise(const std::string& zoneId, const std::string& type, const std::string& severity,
               const std::string& message, long long nowMs);
    void clear(const std::string& zoneId, const std::string& type, long long nowMs);

    EventBus& bus_;
    long long intervalMs_;
    std::vector<Alarm> active_;
    std::map<std::string, RunTrend> trends_;
    std::map<std::string, ModelEstimate> estimates_;
    std::map<std::string, Debounce> debounce_;
};
