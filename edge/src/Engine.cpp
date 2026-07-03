#include "Engine.h"

#include <chrono>
#include <cstdlib>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <thread>

namespace {
std::string tenantId() {
    if (const char* tenant = std::getenv("EDGEMONITOR_TENANT_ID")) return tenant;
    return "tenant-demo";
}
} // namespace

long long Engine::nowMs() {
    using namespace std::chrono;
    return duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
}

Engine::Engine(TelemetryPublisher& publisher, int readIntervalSeconds)
    : publisher_(publisher), readIntervalSeconds_(readIntervalSeconds), commands_(bus_),
      alarms_(bus_, readIntervalSeconds * 1000LL) {
    // Demo-tuned equipment hold time; use 3–5 minutes against real hardware.
    constexpr long long kMinStateMs = 45 * 1000;

    // The building model drives everything: one controller per zone object.
    std::vector<ZoneController*> zonePtrs;
    for (const auto& info : simulation_.zoneInfos()) {
        zoneInfo_[info.id] = info;
        zones_.push_back(std::make_unique<ZoneController>(info.id, bus_, simulation_,
                                                          kMinStateMs));
        zonePtrs.push_back(zones_.back().get());
    }
    optimizer_ = std::make_unique<Optimizer>(bus_, zonePtrs);

    // Commands from the .NET orchestrator (Dashboard → API → here).
    bus_.subscribe(EventType::CommandReceived, [this](const Event& e) {
        if (e.name == "injectFault" || e.name == "clearFault") {
            const bool active = e.name == "injectFault";
            if (simulation_.setFault(e.zoneId, e.detail, active)) {
                bus_.publish({EventType::FaultChanged, e.zoneId, e.detail,
                              active ? 1.0 : 0.0, e.timestampMs, ""});
            } else {
                std::cerr << "[engine] invalid fault command: " << e.detail
                          << " for " << e.zoneId << "\n";
            }
            return;
        }

        ZoneController* zone = findZone(e.zoneId);
        if (zone == nullptr) {
            std::cerr << "[engine] command for unknown zone: " << e.zoneId << "\n";
            return;
        }
        if (e.name == "setSetpoint") {
            zone->setManualSetpoint(e.value, e.timestampMs);
        } else if (e.name == "clearSetpoint") {
            zone->clearManualSetpoint(e.timestampMs);
        } else {
            std::cerr << "[engine] unknown command type: " << e.name << "\n";
        }
    });

    // Event-driven status push: the moment equipment state, a setpoint, a fault,
    // or an alarm changes, the orchestrator hears about it (plus the periodic
    // heartbeat below).
    bus_.subscribe(EventType::StateChanged, [this](const Event& e) {
        std::cout << "[engine] " << e.zoneId << " -> " << e.name << " (temp " << e.value
                  << "F)" << (e.detail.empty() ? "" : " [" + e.detail + "]") << "\n";
        publishStatus(e.timestampMs);
    });
    bus_.subscribe(EventType::SetpointChanged, [this](const Event& e) {
        std::cout << "[engine] " << e.zoneId << " setpoint via " << e.name
                  << " -> " << e.value << "F\n";
        publishStatus(e.timestampMs);
    });
    bus_.subscribe(EventType::FaultChanged, [this](const Event& e) {
        std::cout << "[engine] fault " << (e.value > 0 ? "injected" : "cleared") << ": "
                  << e.name << " on " << e.zoneId << "\n";
        publishStatus(e.timestampMs);
    });
    bus_.subscribe(EventType::AlarmRaised, [this](const Event& e) {
        std::cout << "[engine] ALARM (" << e.name << "): " << e.detail << "\n";
        publishStatus(e.timestampMs);
    });
    bus_.subscribe(EventType::AlarmCleared, [this](const Event& e) {
        std::cout << "[engine] alarm cleared (" << e.name << ") on " << e.zoneId << "\n";
        publishStatus(e.timestampMs);
    });

    scheduler_.every(readIntervalSeconds_ * 1000LL,
                     [this](long long now) { publishTelemetry(now); }, 2000);
    scheduler_.every(60 * 1000, [this](long long now) { optimizer_->evaluate(now); }, 5000);
    scheduler_.every(readIntervalSeconds_ * 1000LL,
                     [this](long long now) { publishStatus(now); }, 3000);
}

ZoneController* Engine::findZone(const std::string& zoneId) {
    for (auto& zone : zones_) {
        if (zone->zoneId() == zoneId) return zone.get();
    }
    return nullptr;
}

Engine::~Engine() {
    running_ = false;
    if (physicsThread_.joinable()) {
        physicsThread_.join();
    }
}

void Engine::run() {
    std::cout << "[engine] digital twin started (read interval " << readIntervalSeconds_
              << "s, " << zones_.size() << " zones)\n";
    for (const auto& [id, info] : zoneInfo_) {
        std::cout << "[engine]   " << id << " = " << info.name << " (floor "
                  << info.floor << ")\n";
    }

    // Physics on its own thread at 10 Hz: the thermal model evolves smoothly
    // while the control loop ticks at 1 Hz. Sensor samples flow to the control
    // loop through the lock-free SPSC ring (sampled at 2 Hz); the simulation
    // itself is mutex-guarded only for command/status access.
    physicsThread_ = std::thread([this] {
        int tick = 0;
        while (running_) {
            simulation_.step(nowMs());
            if (++tick % 5 == 0) { // sample the sensors every 500ms
                for (const auto& reading : simulation_.readAll()) {
                    samples_.push(reading); // full ring drops the sample — next one is 500ms away
                }
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
        }
    });

    while (running_) {
        const long long now = nowMs();
        drainSamples();
        scheduler_.runDue(now);
        commands_.poll(now);
        bus_.dispatchAll();
        std::this_thread::sleep_for(std::chrono::seconds(1));
    }
}

void Engine::drainSamples() {
    SensorReading reading;
    while (samples_.pop(reading)) {
        if (reading.type == SensorType::Temperature) {
            lastTemps_[reading.zoneId] = {reading.value, reading.timestampMs};
        }
        pendingReadings_.push_back(reading);
    }
}

void Engine::publishTelemetry(long long now) {
    drainSamples();

    if (!pendingReadings_.empty()) {
        // Keep only the freshest sample per zone+sensor for this batch — the
        // ring delivers at 2 Hz but one payload per interval is what the IoT
        // Hub F1 quota budget allows.
        std::map<std::string, SensorReading> latest;
        for (const auto& reading : pendingReadings_) {
            latest[reading.zoneId + "|" + sensorTypeToString(reading.type)] = reading;
        }
        pendingReadings_.clear();

        TelemetryBatch batch;
        batch.tenantId = tenantId();
        for (const auto& [key, reading] : latest) {
            batch.readings.push_back(reading);
        }
        publisher_.publishBatch(batch);

        // Control evaluation runs at sensor cadence, like a real BAS controller.
        for (const auto& reading : batch.readings) {
            if (reading.type != SensorType::Temperature) continue;
            if (ZoneController* zone = findZone(reading.zoneId)) {
                zone->onTemperature(reading.value, now);
            }
        }
    }

    // Recovery behavior: a zone whose sensor went silent must not run blind.
    const long long staleAfterMs = 3LL * readIntervalSeconds_ * 1000;
    for (auto& zone : zones_) {
        auto it = lastTemps_.find(zone->zoneId());
        if (it != lastTemps_.end() && now - it->second.atMs > staleAfterMs) {
            zone->failSafe(now);
        }
    }

    // Alarm detection from observed behavior — the monitor is never told
    // which faults were injected.
    std::vector<ZoneObservation> observations;
    for (auto& zone : zones_) {
        const auto it = lastTemps_.find(zone->zoneId());
        ZoneObservation obs;
        obs.zoneId = zone->zoneId();
        obs.zoneName = zoneInfo_[zone->zoneId()].name;
        obs.state = zone->state();
        obs.lastTempF = it != lastTemps_.end() ? it->second.tempF : 0.0;
        obs.lastTempMs = it != lastTemps_.end() ? it->second.atMs : -1;
        obs.setpointF = zone->effectiveSetpointF(now);
        obs.outdoorTempF = simulation_.outdoorTempF(now); // the outdoor air sensor
        obs.leakFactor = zoneInfo_[zone->zoneId()].leakFactor;
        observations.push_back(obs);
    }
    alarms_.evaluate(observations, now);
}

std::string Engine::statusJson(long long now) const {
    std::ostringstream out;
    out << "{\"tenantId\":\"" << tenantId() << "\","
        << "\"deviceId\":\"edge-device-01\","
        << "\"timestampMs\":" << now << ","
        << "\"zones\":[";
    for (size_t i = 0; i < zones_.size(); ++i) {
        const auto& zone = zones_[i];
        const auto infoIt = zoneInfo_.find(zone->zoneId());
        if (i > 0) out << ",";
        out << "{\"zoneId\":\"" << zone->zoneId() << "\","
            << "\"name\":\"" << (infoIt != zoneInfo_.end() ? infoIt->second.name : "") << "\","
            << "\"floor\":" << (infoIt != zoneInfo_.end() ? infoIt->second.floor : 0) << ","
            << "\"state\":\"" << hvacStateToString(zone->state()) << "\","
            << std::fixed << std::setprecision(1)
            << "\"setpointF\":" << zone->effectiveSetpointF(now) << ","
            << "\"tempF\":" << simulation_.zoneTempF(zone->zoneId()) << ","
            << "\"sensorTempF\":" << simulation_.zoneSensorTempF(zone->zoneId()) << ","
            << "\"occupied\":" << (zone->occupiedNow(now) ? "true" : "false") << ","
            << "\"faults\":[";
        const auto faults = simulation_.activeFaults(zone->zoneId());
        for (size_t f = 0; f < faults.size(); ++f) {
            if (f > 0) out << ",";
            out << "\"" << faults[f] << "\"";
        }
        out << "]}";
    }
    out << "],\"alarms\":[";
    const auto& active = alarms_.active();
    for (size_t i = 0; i < active.size(); ++i) {
        if (i > 0) out << ",";
        out << "{\"zoneId\":\"" << active[i].zoneId << "\","
            << "\"type\":\"" << active[i].type << "\","
            << "\"severity\":\"" << active[i].severity << "\","
            << "\"message\":\"" << active[i].message << "\","
            << "\"sinceMs\":" << active[i].sinceMs << "}";
    }
    out << "]}";
    return out.str();
}

void Engine::publishStatus(long long now) {
    publisher_.publishStatus(statusJson(now));
}

// ---------------------------------------------------------------------------
// Self-test — run with `edge_agent --selftest`
// ---------------------------------------------------------------------------

int Engine::selfTest() {
    int failures = 0;
    auto check = [&failures](bool condition, const char* name) {
        std::cout << "[selftest] " << (condition ? "PASS" : "FAIL") << "  " << name << "\n";
        if (!condition) ++failures;
    };

    // EventBus delivers published events to subscribers.
    {
        EventBus bus;
        int hits = 0;
        bus.subscribe(EventType::CommandReceived, [&hits](const Event&) { ++hits; });
        bus.publish({EventType::CommandReceived, "zone-1", "setSetpoint", 74.0, 0, ""});
        bus.dispatchAll();
        check(hits == 1, "EventBus delivers events");
    }

    // Lock-free SPSC ring: FIFO order, nothing lost across threads.
    {
        SpscRing<int, 64> ring;
        ring.push(1);
        ring.push(2);
        ring.push(3);
        int a = 0, b = 0, c = 0, d = 0;
        ring.pop(a);
        ring.pop(b);
        ring.pop(c);
        check(a == 1 && b == 2 && c == 3 && !ring.pop(d), "SPSC ring preserves FIFO order");

        SpscRing<int, 256> stress;
        constexpr int kItems = 100000;
        long long sum = 0;
        int received = 0;
        std::thread consumer([&stress, &sum, &received] {
            int value;
            while (received < kItems) {
                if (stress.pop(value)) {
                    sum += value;
                    ++received;
                }
            }
        });
        for (int i = 1; i <= kItems; ++i) {
            while (!stress.push(i)) {
            }
        }
        consumer.join();
        check(received == kItems && sum == static_cast<long long>(kItems) * (kItems + 1) / 2,
              "SPSC ring survives cross-thread stress (100k items)");
    }

    // State machine: hysteresis transitions.
    {
        ZoneStateMachine sm("zone-1", 0);
        check(sm.update(80.0, 72.0, 1000) && sm.state() == HvacState::Cooling,
              "Idle -> Cooling above deadband");
        check(sm.update(72.0, 72.0, 2000) && sm.state() == HvacState::Idle,
              "Cooling -> Idle at setpoint");
        check(sm.update(68.0, 72.0, 3000) && sm.state() == HvacState::Heating,
              "Idle -> Heating below deadband");
    }

    // State machine: minimum-state hold protects equipment; forceIdle bypasses it.
    {
        ZoneStateMachine sm("zone-1", 60 * 1000);
        sm.update(80.0, 72.0, 1000); // -> Cooling
        check(!sm.update(70.0, 72.0, 2000), "min-state hold blocks early transition");
        check(sm.update(70.0, 72.0, 70 * 1000), "transition allowed after hold expires");

        ZoneStateMachine held("zone-1", 60 * 1000);
        held.update(80.0, 72.0, 1000); // -> Cooling
        check(held.forceIdle(2000) && held.state() == HvacState::Idle,
              "fail-safe forceIdle bypasses the hold");
    }

    // Simulation closes the loop: applying Cooling lowers the temperature.
    {
        BuildingSimulation sim(defaultBuildingModel());
        sim.apply("zone-1", HvacState::Cooling);
        const double before = sim.zoneTempF("zone-1");
        long long t = 3LL * 60 * 60 * 1000; // 03:00 — cool outdoors, no leak fighting us
        sim.step(t);
        for (int i = 1; i <= 30; ++i) {
            sim.step(t + i * 5000LL);
        }
        check(sim.zoneTempF("zone-1") < before - 1.0, "cooling lowers zone temperature");
    }

    // Fault physics: a stuck damper strangles cooling.
    {
        BuildingSimulation healthy(defaultBuildingModel()), faulty(defaultBuildingModel());
        healthy.apply("zone-1", HvacState::Cooling);
        faulty.apply("zone-1", HvacState::Cooling);
        check(faulty.setFault("zone-1", "damperStuck", true), "setFault accepts known faults");
        check(!faulty.setFault("zone-1", "gremlins", true), "setFault rejects unknown faults");

        long long t = 3LL * 60 * 60 * 1000;
        healthy.step(t);
        faulty.step(t);
        for (int i = 1; i <= 30; ++i) {
            healthy.step(t + i * 5000LL);
            faulty.step(t + i * 5000LL);
        }
        check(healthy.zoneTempF("zone-1") < faulty.zoneTempF("zone-1") - 1.0,
              "stuck damper strangles cooling");
    }

    // Fault physics: an offline sensor goes silent (power still reports).
    {
        BuildingSimulation sim(defaultBuildingModel());
        sim.setFault("zone-1", "sensorOffline", true);
        bool hasTemp = false, hasPower = false;
        for (const auto& r : sim.readAll()) {
            if (r.zoneId != "zone-1") continue;
            if (r.type == SensorType::Temperature) hasTemp = true;
            if (r.type == SensorType::PowerDraw) hasPower = true;
        }
        check(!hasTemp && hasPower, "offline sensor goes silent, equipment still reports");
    }

    // Building model loads from JSON.
    {
        const BuildingModel model = parseBuildingModel(
            R"({"name":"Test","zones":[{"id":"z1","name":"Room","floor":3,"initialTempF":70,"leakFactor":1.2}]})");
        check(model.zones.size() == 1 && model.zones[0].floor == 3 &&
                  model.zones[0].name == "Room",
              "building model parses from JSON");

        bool rejected = false;
        try {
            parseBuildingModel(R"({"zones":[]})");
        } catch (const std::exception&) {
            rejected = true;
        }
        check(rejected, "empty zones array is rejected");
    }

    // Fault physics: a drifting sensor reports plausible-but-wrong values.
    {
        BuildingSimulation sim(defaultBuildingModel());
        sim.setFault("zone-1", "sensorDrift", true);
        long long t = 3LL * 60 * 60 * 1000;
        sim.step(t);
        for (int i = 1; i <= 30; ++i) {
            sim.step(t + i * 5000LL);
        }
        check(sim.zoneSensorTempF("zone-1") > sim.zoneTempF("zone-1") + 0.5,
              "drifting sensor diverges from ground truth");
        sim.setFault("zone-1", "sensorDrift", false);
        check(sim.zoneSensorTempF("zone-1") == sim.zoneTempF("zone-1"),
              "clearing drift recalibrates the sensor");
    }

    // Fault physics: a refrigerant leak degrades cooling capacity over time.
    {
        BuildingSimulation healthy(defaultBuildingModel()), leaking(defaultBuildingModel());
        healthy.apply("zone-1", HvacState::Cooling);
        leaking.apply("zone-1", HvacState::Cooling);
        leaking.setFault("zone-1", "refrigerantLeak", true);
        long long t = 3LL * 60 * 60 * 1000;
        healthy.step(t);
        leaking.step(t);
        for (int i = 1; i <= 200; ++i) {
            healthy.step(t + i * 5000LL);
            leaking.step(t + i * 5000LL);
        }
        check(healthy.zoneTempF("zone-1") < leaking.zoneTempF("zone-1") - 0.5,
              "refrigerant leak degrades cooling over time");
    }

    // Alarm monitor: detects symptoms, never told about faults.
    {
        EventBus bus;
        AlarmMonitor monitor(bus, 1000);
        auto hasAlarm = [&monitor](const std::string& zoneId, const std::string& type) {
            for (const auto& alarm : monitor.active()) {
                if (alarm.zoneId == zoneId && alarm.type == type) return true;
            }
            return false;
        };

        // Stale sensor -> sensor-offline; fresh again -> cleared.
        std::vector<ZoneObservation> obs{{"zone-1", "Lobby", HvacState::Idle, 72.0, 0, 72.0}};
        monitor.evaluate(obs, 10000);
        check(hasAlarm("zone-1", "sensor-offline"), "stale sensor raises sensor-offline");
        obs[0].lastTempMs = 12500; // fresh again — clear waits out the min-active debounce
        monitor.evaluate(obs, 13000);
        check(!hasAlarm("zone-1", "sensor-offline"), "fresh readings clear sensor-offline");

        // Cooling running with zero progress -> ineffective-equipment (+ out-of-range).
        std::vector<ZoneObservation> hot{{"zone-2", "Conference Room", HvacState::Cooling,
                                          80.0, 1000, 72.0}};
        monitor.evaluate(hot, 1000); // establishes the run trend
        hot[0].lastTempMs = 6000;
        monitor.evaluate(hot, 6000); // 5s of cooling, no improvement
        check(hasAlarm("zone-2", "ineffective-equipment"),
              "non-responding temperature raises ineffective-equipment");
        check(hasAlarm("zone-2", "out-of-range"), "8F deviation raises out-of-range");

        // Healthy progress must NOT alarm: 0.5°F in 5s beats the 0.01°F/s expectation.
        std::vector<ZoneObservation> healthy{{"zone-3", "Office 201", HvacState::Cooling,
                                              80.0, 1000, 72.0}};
        monitor.evaluate(healthy, 1000);
        healthy[0].lastTempF = 79.5;
        healthy[0].lastTempMs = 6000;
        monitor.evaluate(healthy, 6000);
        check(!hasAlarm("zone-3", "ineffective-equipment"),
              "healthy progress does not raise ineffective-equipment");
    }

    // Analytical redundancy: a drifting sensor can't fool the model estimate.
    {
        EventBus bus;
        AlarmMonitor monitor(bus, 1000);
        auto hasAlarm = [&monitor](const std::string& zoneId, const std::string& type) {
            for (const auto& alarm : monitor.active()) {
                if (alarm.zoneId == zoneId && alarm.type == type) return true;
            }
            return false;
        };

        // Zone idle with outdoor == indoor, so the model expects a flat line;
        // the sensor creeps up 0.01°F/s (the sensorDrift signature).
        for (int t = 0; t <= 600; ++t) {
            std::vector<ZoneObservation> obs{{"zone-1", "Lobby", HvacState::Idle,
                                              72.0 + 0.01 * t, t * 1000LL, 72.0, 72.0, 1.0}};
            monitor.evaluate(obs, t * 1000LL);
        }
        check(hasAlarm("zone-1", "sensor-implausible"),
              "drifting sensor raises sensor-implausible via model residual");

        EventBus bus2;
        AlarmMonitor steady(bus2, 1000);
        bool falseAlarm = false;
        for (int t = 0; t <= 600; ++t) {
            std::vector<ZoneObservation> obs{{"zone-1", "Lobby", HvacState::Idle, 72.0,
                                              t * 1000LL, 72.0, 72.0, 1.0}};
            steady.evaluate(obs, t * 1000LL);
            for (const auto& alarm : steady.active()) {
                if (alarm.type == "sensor-implausible") falseAlarm = true;
            }
        }
        check(!falseAlarm, "a truthful sensor stays trusted by the model");
    }

    // Fault physics: a chattering damper degrades cooling intermittently.
    {
        BuildingSimulation healthy(defaultBuildingModel()), chatter(defaultBuildingModel());
        healthy.apply("zone-1", HvacState::Cooling);
        chatter.apply("zone-1", HvacState::Cooling);
        chatter.setFault("zone-1", "damperChatter", true);
        long long t = 3LL * 60 * 60 * 1000;
        healthy.step(t);
        chatter.step(t);
        for (int i = 1; i <= 30; ++i) {
            healthy.step(t + i * 5000LL);
            chatter.step(t + i * 5000LL);
        }
        check(healthy.zoneTempF("zone-1") < chatter.zoneTempF("zone-1") - 1.0,
              "chattering damper degrades cooling intermittently");
    }

    // Alarm debounce: intermittent conditions can't flap an alarm on/off.
    {
        EventBus bus;
        AlarmMonitor monitor(bus, 1000);
        auto hasAlarm = [&monitor](const std::string& type) {
            for (const auto& alarm : monitor.active()) {
                if (alarm.type == type) return true;
            }
            return false;
        };
        auto observe = [](double tempF, long long ts) {
            return std::vector<ZoneObservation>{
                {"zone-1", "Lobby", HvacState::Idle, tempF, ts, 72.0, 72.0, 1.0}};
        };

        monitor.evaluate(observe(80.0, 1000), 1000); // 8°F over → raise
        monitor.evaluate(observe(72.0, 1500), 1500); // back in range immediately
        check(hasAlarm("out-of-range"), "alarm holds its minimum active duration");
        monitor.evaluate(observe(72.0, 5000), 5000); // min-active elapsed
        check(!hasAlarm("out-of-range"), "alarm clears after the minimum duration");
        monitor.evaluate(observe(80.0, 6000), 6000); // condition back within hold-off
        check(!hasAlarm("out-of-range"), "hold-off suppresses a flapping re-raise");
        monitor.evaluate(observe(80.0, 12000), 12000); // hold-off elapsed
        check(hasAlarm("out-of-range"), "alarm re-raises once the hold-off expires");
    }

    // Occupancy schedule setpoints.
    {
        SetpointSchedule schedule;
        check(schedule.setpointFor(10) == 72.0, "occupied hours use comfort setpoint");
        check(schedule.setpointFor(22) == 78.0, "unoccupied hours use setback");
    }

    // Controller: manual override, optimizer offset rules, actuator wiring.
    {
        EventBus bus;
        BuildingSimulation sim(defaultBuildingModel());
        ZoneController zone("zone-1", bus, sim, 0);
        const long long noon = 12LL * 60 * 60 * 1000; // occupied hour

        check(zone.effectiveSetpointF(noon) == 72.0, "controller follows schedule");
        zone.setManualSetpoint(74.5, noon);
        check(zone.effectiveSetpointF(noon) == 74.5, "manual setpoint overrides schedule");
        zone.setOptimizerOffset(2.0);
        check(zone.effectiveSetpointF(noon) == 74.5, "optimizer never overrides manual");
        zone.clearManualSetpoint(noon);
        check(zone.effectiveSetpointF(noon) == 74.0, "offset applies in schedule mode");

        zone.setOptimizerOffset(0.0);
        zone.onTemperature(80.0, noon);
        check(zone.state() == HvacState::Cooling, "hot zone commands cooling");

        int stateEvents = 0;
        bus.subscribe(EventType::StateChanged, [&stateEvents](const Event&) { ++stateEvents; });
        bus.dispatchAll();
        check(stateEvents == 1, "state change publishes an event");

        zone.failSafe(noon + 1000);
        check(zone.state() == HvacState::Idle, "fail-safe returns zone to Idle");
    }

    // Optimizer peak-hour rules.
    {
        EventBus bus;
        BuildingSimulation sim(defaultBuildingModel());
        ZoneController zone("zone-1", bus, sim, 0);
        Optimizer optimizer(bus, {&zone});

        const long long peak = 15LL * 60 * 60 * 1000;    // 15:00 — peak pricing
        const long long offPeak = 10LL * 60 * 60 * 1000; // 10:00
        optimizer.evaluate(peak);
        check(zone.effectiveSetpointF(peak) == 74.0, "peak hours relax the setpoint +2F");
        optimizer.evaluate(offPeak);
        check(zone.effectiveSetpointF(offPeak) == 72.0, "offset clears outside peak");
    }

    // Command JSON parsing (incl. the fault field).
    {
        const std::string json =
            R"({"type":"injectFault","zoneId":"zone-1","value":0,"fault":"damperStuck"})";
        check(minijson::extractString(json, "type") == "injectFault", "minijson extracts strings");
        check(minijson::extractString(json, "fault") == "damperStuck", "minijson extracts fault");
        check(minijson::extractNumber(json, "value") == 0.0, "minijson extracts numbers");
    }

    std::cout << "[selftest] " << (failures == 0 ? "ALL PASS" : "FAILURES") << " ("
              << failures << " failed)\n";
    return failures;
}
