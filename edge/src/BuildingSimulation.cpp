#include "BuildingSimulation.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdlib>

namespace {
constexpr double kPi = 3.14159265358979323846;
constexpr const char* kDeviceId = "edge-device-01";

// Balance rule: healthy equipment must beat worst-case leak (leakiest zone on
// the hottest afternoon ≈ 0.001 × 1.6 × 20°F = 0.032°F/s) with margin, while a
// stuck damper (15% effect) must not — that separation is what lets the alarm
// monitor tell a healthy zone from a broken one at any read interval.
// Coefficients are deliberately accelerated (~minutes instead of hours) so a
// demo viewer can watch the control loop settle.
constexpr double kLeakPerSec = 0.001;    // baseline drift toward outdoor temp
constexpr double kCoolFPerSec = 0.050;   // cooling pulls ~3°F per minute
constexpr double kHeatFPerSec = 0.040;   // heating adds ~2.4°F per minute
constexpr double kMaxStepSec = 10.0;     // clamp big gaps (paused process, clock jump)

constexpr double kCoolingKw = 3.5;
constexpr double kHeatingKw = 3.0;

// Fault physics
constexpr double kStuckDamperEffect = 0.15;   // air barely moves, equipment still runs
constexpr long long kChatterPeriodMs = 15000; // chattering damper: stuck/free every 15s
constexpr double kOverloadEffect = 0.5;       // strained unit: half the output...
constexpr double kOverloadPower = 1.5;        // ...for 1.5x the power
constexpr double kDriftFPerSec = 0.01;        // sensor creeps ~0.6°F per minute
constexpr double kMaxDriftF = 8.0;
constexpr double kLeakDecayPerSec = 0.0008;   // refrigerant: capacity gone in ~18 min
constexpr double kMinCapacity = 0.15;
} // namespace

BuildingSimulation::BuildingSimulation() : BuildingSimulation(loadBuildingModel()) {}

BuildingSimulation::BuildingSimulation(const BuildingModel& model) {
    for (const auto& spec : model.zones) {
        Zone zone;
        zone.id = spec.id;
        zone.name = spec.name;
        zone.floor = spec.floor;
        zone.tempF = spec.initialTempF;
        zone.basePowerKw = spec.basePowerKw;
        zone.leakFactor = spec.leakFactor;
        zones_.push_back(zone);
    }
}

long long BuildingSimulation::currentMs() {
    using namespace std::chrono;
    return duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
}

int BuildingSimulation::hourOfDay(long long nowMs) {
    return static_cast<int>((nowMs / (60LL * 60 * 1000)) % 24);
}

double BuildingSimulation::outdoorTempF(long long nowMs) const {
    // Daily cycle peaking mid-afternoon: 64°F at dawn, ~92°F at 3pm.
    const double hours = static_cast<double>(nowMs) / (60.0 * 60.0 * 1000.0);
    return 78.0 + 14.0 * std::sin(2.0 * kPi * (hours - 9.0) / 24.0);
}

double BuildingSimulation::occupancyAt(long long nowMs) const {
    const int hour = hourOfDay(nowMs);
    if (hour < 7 || hour >= 19) {
        return static_cast<double>(std::rand() % 3); // night: cleaners, security
    }
    const double peak = 24.0 * std::sin(kPi * (hour - 7) / 12.0);
    return std::max(0.0, std::round(peak + (std::rand() % 5) - 2));
}

BuildingSimulation::Zone* BuildingSimulation::find(const std::string& zoneId) {
    for (auto& zone : zones_) {
        if (zone.id == zoneId) return &zone;
    }
    return nullptr;
}

const BuildingSimulation::Zone* BuildingSimulation::find(const std::string& zoneId) const {
    for (const auto& zone : zones_) {
        if (zone.id == zoneId) return &zone;
    }
    return nullptr;
}

void BuildingSimulation::apply(const std::string& zoneId, HvacState state) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (Zone* zone = find(zoneId)) {
        zone->hvac = state;
    }
}

bool BuildingSimulation::isKnownFault(const std::string& fault) {
    return fault == "sensorOffline" || fault == "sensorDrift" || fault == "damperStuck" ||
           fault == "damperChatter" || fault == "hvacOverload" || fault == "refrigerantLeak";
}

bool BuildingSimulation::setFault(const std::string& zoneId, const std::string& fault,
                                  bool active) {
    std::lock_guard<std::mutex> lock(mutex_);
    Zone* zone = find(zoneId);
    if (zone == nullptr || !isKnownFault(fault)) return false;

    if (fault == "sensorOffline") {
        zone->sensorOffline = active;
    } else if (fault == "sensorDrift") {
        zone->sensorDrift = active;
        if (!active) zone->sensorDriftF = 0.0; // clearing = recalibration
    } else if (fault == "damperStuck") {
        zone->damperStuck = active;
    } else if (fault == "damperChatter") {
        zone->damperChatter = active;
    } else if (fault == "hvacOverload") {
        zone->hvacOverload = active;
    } else if (fault == "refrigerantLeak") {
        zone->refrigerantLeak = active;
        if (!active) zone->capacityFactor = 1.0; // clearing = recharge
    }
    return true;
}

std::vector<std::string> BuildingSimulation::activeFaults(const std::string& zoneId) const {
    std::lock_guard<std::mutex> lock(mutex_);
    std::vector<std::string> faults;
    if (const Zone* zone = find(zoneId)) {
        if (zone->sensorOffline) faults.push_back("sensorOffline");
        if (zone->sensorDrift) faults.push_back("sensorDrift");
        if (zone->damperStuck) faults.push_back("damperStuck");
        if (zone->damperChatter) faults.push_back("damperChatter");
        if (zone->hvacOverload) faults.push_back("hvacOverload");
        if (zone->refrigerantLeak) faults.push_back("refrigerantLeak");
    }
    return faults;
}

std::vector<ZoneInfo> BuildingSimulation::zoneInfos() const {
    std::lock_guard<std::mutex> lock(mutex_);
    std::vector<ZoneInfo> infos;
    infos.reserve(zones_.size());
    for (const auto& zone : zones_) {
        infos.push_back({zone.id, zone.name, zone.floor, zone.leakFactor});
    }
    return infos;
}

void BuildingSimulation::step(long long nowMs) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (lastStepMs_ == 0) {
        lastStepMs_ = nowMs;
        return;
    }
    double dtSec = static_cast<double>(nowMs - lastStepMs_) / 1000.0;
    lastStepMs_ = nowMs;
    if (dtSec <= 0) return;
    if (dtSec > kMaxStepSec) dtSec = kMaxStepSec;

    const double outdoor = outdoorTempF(nowMs);
    for (auto& zone : zones_) {
        // Progressive faults evolve with time.
        if (zone.sensorDrift) {
            zone.sensorDriftF = std::min(kMaxDriftF, zone.sensorDriftF + kDriftFPerSec * dtSec);
        }
        if (zone.refrigerantLeak) {
            zone.capacityFactor =
                std::max(kMinCapacity, zone.capacityFactor - kLeakDecayPerSec * dtSec);
        }

        double delta = kLeakPerSec * zone.leakFactor * (outdoor - zone.tempF) * dtSec;

        // Fault physics: a stuck damper strangles airflow; an overloaded unit
        // delivers half its rated effect; a refrigerant leak decays capacity.
        double effectiveness = zone.capacityFactor;
        if (zone.damperStuck) effectiveness *= kStuckDamperEffect;
        // Chatter: intermittently stuck — half the time the air moves, half it doesn't.
        if (zone.damperChatter && (nowMs / kChatterPeriodMs) % 2 == 0) {
            effectiveness *= kStuckDamperEffect;
        }
        if (zone.hvacOverload) effectiveness *= kOverloadEffect;

        if (zone.hvac == HvacState::Cooling) delta -= kCoolFPerSec * effectiveness * dtSec;
        if (zone.hvac == HvacState::Heating) delta += kHeatFPerSec * effectiveness * dtSec;

        // sensor noise / internal gains
        delta += ((std::rand() % 100) - 50) / 100.0 * 0.02;
        zone.tempF += delta;
    }
}

double BuildingSimulation::zoneTempF(const std::string& zoneId) const {
    std::lock_guard<std::mutex> lock(mutex_);
    const Zone* zone = find(zoneId);
    return zone != nullptr ? zone->tempF : 0.0;
}

double BuildingSimulation::zoneSensorTempF(const std::string& zoneId) const {
    std::lock_guard<std::mutex> lock(mutex_);
    const Zone* zone = find(zoneId);
    return zone != nullptr ? zone->tempF + zone->sensorDriftF : 0.0;
}

std::vector<SensorReading> BuildingSimulation::readAll() {
    std::lock_guard<std::mutex> lock(mutex_);
    const long long ts = currentMs();
    std::vector<SensorReading> readings;
    readings.reserve(zones_.size() * 2 + 1);

    for (const auto& zone : zones_) {
        // A disconnected sensor doesn't report an error — it just goes silent.
        // A drifting sensor is worse: it reports plausible-but-wrong values.
        if (!zone.sensorOffline) {
            readings.push_back({SensorType::Temperature, zone.tempF + zone.sensorDriftF, "F",
                                zone.id, kDeviceId, ts});
        }

        double powerKw = zone.basePowerKw;
        if (zone.hvac != HvacState::Idle) {
            double draw = zone.hvac == HvacState::Cooling ? kCoolingKw : kHeatingKw;
            if (zone.hvacOverload) draw *= kOverloadPower;
            powerKw += draw; // note: a stuck damper doesn't reduce power — that's the tell
        }
        powerKw += ((std::rand() % 100) - 50) / 100.0 * 0.2;
        readings.push_back({SensorType::PowerDraw, powerKw, "kW", zone.id, kDeviceId, ts});
    }

    readings.push_back({SensorType::Occupancy, occupancyAt(ts), "people", "building", kDeviceId, ts});
    return readings;
}
