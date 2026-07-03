// BuildingSimulation.h — the digital twin's physical model. Every room, HVAC
// unit, and sensor exists as an object with health state, loaded from
// building.json (see BuildingModel.h). It implements BOTH sides of the device
// boundary: ISensorReader (temperatures/power/occupancy flow out) and
// IDeviceActuator (HVAC commands flow in and change those temperatures) — so
// the control loop actually closes in simulation.
//
// Faults are injected here, at the device layer, exactly where they happen in
// a real building. The control/alarm layers upstream must *detect* their
// effects; they are never told directly:
//   sensorOffline   — the room's temperature reading simply stops appearing
//   sensorDrift     — the sensor reports plausible-but-wrong values that creep
//                     upward; the controller silently overcools the real room
//   damperStuck     — equipment runs (and draws power) but air barely moves
//   damperChatter   — the damper alternates stuck/free every 15s: an
//                     intermittent fault that tries to make alarms flap
//   hvacOverload    — unit strains: half the effect, 1.5× the power draw
//   refrigerantLeak — capacity decays slowly over time (failing unit)
//
// Thread safety: step() runs on the physics thread while the control loop
// reads/commands from the main thread — all public methods lock.
#pragma once
#include "BuildingModel.h"
#include "IDeviceActuator.h"
#include "ISensorReader.h"

#include <mutex>
#include <vector>

struct ZoneInfo {
    std::string id;
    std::string name;
    int floor;
    double leakFactor; // from the building model — config, not runtime truth
};

class BuildingSimulation : public ISensorReader, public IDeviceActuator {
public:
    BuildingSimulation();                             // loads building.json / defaults
    explicit BuildingSimulation(const BuildingModel& model);

    // ISensorReader — what the building reports (drift applies here)
    std::vector<SensorReading> readAll() override;
    bool isAvailable() const override { return true; }

    // IDeviceActuator — what the engine commands
    void apply(const std::string& zoneId, HvacState state) override;

    // Advance the thermal model to nowMs (called from the physics thread).
    void step(long long nowMs);

    // Fault injection (Dashboard → API → engine command → here).
    bool setFault(const std::string& zoneId, const std::string& fault, bool active);
    std::vector<std::string> activeFaults(const std::string& zoneId) const;
    static bool isKnownFault(const std::string& fault);

    std::vector<ZoneInfo> zoneInfos() const;
    double zoneTempF(const std::string& zoneId) const;       // ground truth
    double zoneSensorTempF(const std::string& zoneId) const; // what the sensor claims
    double outdoorTempF(long long nowMs) const;

private:
    struct Zone {
        std::string id;
        std::string name;
        int floor;
        double tempF;
        double basePowerKw;
        double leakFactor;   // envelope quality: glass lobby leaks more than an office
        HvacState hvac = HvacState::Idle;
        // health state
        bool sensorOffline = false;
        bool sensorDrift = false;
        bool damperStuck = false;
        bool damperChatter = false;
        bool hvacOverload = false;
        bool refrigerantLeak = false;
        double sensorDriftF = 0.0;   // accumulated drift while sensorDrift active
        double capacityFactor = 1.0; // decays while refrigerantLeak active
    };

    Zone* find(const std::string& zoneId);
    const Zone* find(const std::string& zoneId) const;
    double occupancyAt(long long nowMs) const;
    static int hourOfDay(long long nowMs);
    static long long currentMs();

    mutable std::mutex mutex_;
    std::vector<Zone> zones_;
    long long lastStepMs_ = 0;
};
