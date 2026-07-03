// Engine.h — wires the event-driven control loop of the digital twin:
//
//   Dashboard → API → command file / IoT Hub C2D → CommandReceived event
//     → ZoneController (control logic) → ZoneStateMachine → IDeviceActuator
//     → device (BuildingSimulation in v1, BACnet in v1.5)
//     → sensor readings → telemetry + status + alarms → API → SignalR → Dashboard
//
// Faults injected from the dashboard land in the simulation's device layer;
// the AlarmMonitor detects their *symptoms* (stale sensors, non-responding
// temperatures) and the controllers react (fail-safe idle) — alarms, control
// decisions, and recovery are all observable in real time.
//
// Concurrency model: the physics simulation steps on a dedicated worker thread
// at 10 Hz (BuildingSimulation locks internally), while the control loop —
// scheduler, command inbox, event dispatch — runs deterministically at 1 Hz on
// the main thread. Control logic stays single-threaded and easy to reason
// about; only the shared device model is synchronized.
#pragma once
#include "AlarmMonitor.h"
#include "BuildingSimulation.h"
#include "CommandListener.h"
#include "EventBus.h"
#include "Optimizer.h"
#include "Scheduler.h"
#include "SpscRing.h"
#include "TelemetryPublisher.h"
#include "ZoneController.h"

#include <atomic>
#include <map>
#include <memory>
#include <string>
#include <thread>
#include <vector>

class Engine {
public:
    Engine(TelemetryPublisher& publisher, int readIntervalSeconds);
    ~Engine();

    void run(); // blocks until the process is stopped

    // Lightweight built-in checks (state machine, simulation loop closure,
    // fault physics, alarm detection, optimizer, command parsing).
    // Returns the number of failures.
    static int selfTest();

private:
    void drainSamples();
    void publishTelemetry(long long nowMs);
    void publishStatus(long long nowMs);
    std::string statusJson(long long nowMs) const;
    ZoneController* findZone(const std::string& zoneId);
    static long long nowMs();

    struct LastReading {
        double tempF = 0.0;
        long long atMs = -1;
    };

    TelemetryPublisher& publisher_;
    int readIntervalSeconds_;
    EventBus bus_;
    Scheduler scheduler_;
    BuildingSimulation simulation_;
    CommandListener commands_;
    AlarmMonitor alarms_;
    std::vector<std::unique_ptr<ZoneController>> zones_;
    std::map<std::string, ZoneInfo> zoneInfo_;
    std::map<std::string, LastReading> lastTemps_;
    std::unique_ptr<Optimizer> optimizer_;

    std::atomic<bool> running_{true};
    std::thread physicsThread_;

    // Physics thread → control loop, no locks: samples flow through this ring.
    SpscRing<SensorReading, 1024> samples_;
    std::vector<SensorReading> pendingReadings_;
};
