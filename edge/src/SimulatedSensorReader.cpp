#include "SimulatedSensorReader.h"

#include <chrono>
#include <cmath>
#include <cstdlib>

namespace {
constexpr double kPi = 3.14159265358979323846;
constexpr const char* kDeviceId = "edge-device-01";
}

long long SimulatedSensorReader::nowMs() const {
    using namespace std::chrono;
    return duration_cast<milliseconds>(system_clock::now().time_since_epoch()).count();
}

double SimulatedSensorReader::sineWave(double base, double amplitude, double periodSeconds) const {
    const double t = static_cast<double>(nowMs()) / 1000.0;
    // Small random jitter so consecutive readings don't look machine-perfect.
    const double noise = ((std::rand() % 100) - 50) / 100.0 * amplitude * 0.1;
    return base + amplitude * std::sin(2.0 * kPi * t / periodSeconds) + noise;
}

std::vector<SensorReading> SimulatedSensorReader::readAll() {
    const long long ts = nowMs();

    // Periods are compressed (minutes instead of a full day) so demo tiles
    // visibly move while someone is watching the dashboard.
    std::vector<SensorReading> readings;
    readings.push_back({SensorType::Temperature, sineWave(72.0, 4.0, 3600.0), "F", "zone-1", kDeviceId, ts});
    readings.push_back({SensorType::Temperature, sineWave(74.0, 5.0, 2700.0), "F", "zone-2", kDeviceId, ts});
    readings.push_back({SensorType::PowerDraw, sineWave(12.0, 3.0, 1800.0), "kW", "zone-1", kDeviceId, ts});
    readings.push_back({SensorType::PowerDraw, sineWave(9.0, 2.5, 2400.0), "kW", "zone-2", kDeviceId, ts});

    double occupancy = std::round(sineWave(18.0, 14.0, 3600.0));
    if (occupancy < 0) occupancy = 0;
    readings.push_back({SensorType::Occupancy, occupancy, "people", "building", kDeviceId, ts});

    return readings;
}
