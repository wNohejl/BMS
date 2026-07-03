// main.cpp — entry point.
//
// Default: the event-driven control Engine (scheduling, state machines, control
// logic, closed-loop simulation, optimization) with the command channel open to
// the .NET orchestrator.
//
// Modes:
//   --local          file-based I/O under /tmp/edgemonitor/ ($0 dev loop)
//   --output-stdout  print JSON to the terminal
//   --bacnet         legacy read-only loop via BACnetSensorReader (v1.5 stub)
//   --interval N     sensor read cadence in seconds (min 20 in cloud mode)
//   --selftest       run built-in engine checks and exit
#include "BACnetSensorReader.h"
#include "Engine.h"
#include "TelemetryPublisher.h"

#include <chrono>
#include <cstdlib>
#include <iostream>
#include <string>
#include <thread>

int main(int argc, char* argv[]) {
    bool localMode = false;
    bool stdoutMode = false;
    bool useBACnet = false;
    bool selfTest = false;
    int intervalSeconds = 30; // ~2,880 msgs/day — safely under the F1 cap of 8,000

    for (int i = 1; i < argc; i++) {
        const std::string arg = argv[i];
        if (arg == "--local") localMode = true;
        else if (arg == "--output-stdout") stdoutMode = true;
        else if (arg == "--bacnet") useBACnet = true;
        else if (arg == "--selftest") selfTest = true;
        else if (arg == "--interval" && i + 1 < argc) intervalSeconds = std::atoi(argv[++i]);
    }

    if (selfTest) {
        return Engine::selfTest() == 0 ? 0 : 1;
    }

    if (!localMode && !stdoutMode && intervalSeconds < 20) {
        std::cerr << "[edge] interval below 20s would exceed the IoT Hub F1 daily quota — "
                     "clamping to 20s\n";
        intervalSeconds = 20;
    }
    if (intervalSeconds < 1) intervalSeconds = 1;

    std::string connStr;
    if (!localMode && !stdoutMode) {
        const char* env = std::getenv("IOTHUB_CONNECTION_STRING");
        if (env == nullptr || *env == '\0') {
            std::cerr << "[edge] IOTHUB_CONNECTION_STRING is not set. Either export it "
                         "(cloud mode) or run with --local for the $0 dev loop.\n";
            return 1;
        }
        connStr = env;
    }

    try {
        TelemetryPublisher publisher(connStr, localMode, stdoutMode);

        if (useBACnet) {
            // v1.5 path: read-only publishing from real hardware. Control-loop
            // integration follows once BACnetActuator exists.
            BACnetSensorReader reader("eth0");
            std::cout << "[edge] BACnet mode (stub) — interval " << intervalSeconds << "s\n";
            while (true) {
                if (reader.isAvailable()) {
                    TelemetryBatch batch;
                    batch.readings = reader.readAll();
                    if (const char* tenant = std::getenv("EDGEMONITOR_TENANT_ID")) {
                        batch.tenantId = tenant;
                    }
                    if (!batch.readings.empty()) publisher.publishBatch(batch);
                } else {
                    std::cerr << "[edge] BACnet reader unavailable — retrying next interval\n";
                }
                std::this_thread::sleep_for(std::chrono::seconds(intervalSeconds));
            }
        }

        Engine engine(publisher, intervalSeconds);
        engine.run();
    } catch (const std::exception& ex) {
        std::cerr << "[edge] fatal: " << ex.what() << "\n";
        return 1;
    }
}
