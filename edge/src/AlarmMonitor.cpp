#include "AlarmMonitor.h"

#include <algorithm>
#include <cmath>
#include <sstream>

AlarmMonitor::AlarmMonitor(EventBus& bus, long long readIntervalMs)
    : bus_(bus), intervalMs_(readIntervalMs) {}

bool AlarmMonitor::isActive(const std::string& zoneId, const std::string& type) const {
    for (const auto& alarm : active_) {
        if (alarm.zoneId == zoneId && alarm.type == type) return true;
    }
    return false;
}

void AlarmMonitor::raise(const std::string& zoneId, const std::string& type,
                         const std::string& severity, const std::string& message,
                         long long nowMs) {
    if (isActive(zoneId, type)) return;

    // Flap suppression: an alarm that just cleared can't immediately re-raise —
    // an intermittent fault (chattering damper) shows up as one steady alarm.
    Debounce& debounce = debounce_[zoneId + "|" + type];
    if (debounce.clearedMs >= 0 && nowMs - debounce.clearedMs < kHoldOffIntervals * intervalMs_) {
        return;
    }

    debounce.raisedMs = nowMs;
    active_.push_back({zoneId, type, severity, message, nowMs});
    bus_.publish({EventType::AlarmRaised, zoneId, type, 0.0, nowMs, message});
}

void AlarmMonitor::clear(const std::string& zoneId, const std::string& type, long long nowMs) {
    if (!isActive(zoneId, type)) return;

    // Minimum active time: a borderline condition can't blink an alarm on/off.
    Debounce& debounce = debounce_[zoneId + "|" + type];
    if (debounce.raisedMs >= 0 && nowMs - debounce.raisedMs < kMinActiveIntervals * intervalMs_) {
        return;
    }

    debounce.clearedMs = nowMs;
    active_.erase(std::remove_if(active_.begin(), active_.end(),
                                 [&](const Alarm& a) {
                                     return a.zoneId == zoneId && a.type == type;
                                 }),
                  active_.end());
    bus_.publish({EventType::AlarmCleared, zoneId, type, 0.0, nowMs, ""});
}

void AlarmMonitor::evaluate(const std::vector<ZoneObservation>& zones, long long nowMs) {
    for (const auto& obs : zones) {
        const bool everSeen = obs.lastTempMs >= 0;
        const bool sensorFresh =
            everSeen && (nowMs - obs.lastTempMs) <= kStaleIntervals * intervalMs_;

        // 1. Sensor offline — readings went stale.
        if (everSeen && !sensorFresh) {
            raise(obs.zoneId, "sensor-offline", "critical",
                  obs.zoneName + " temperature sensor has stopped reporting — "
                  "equipment held in fail-safe idle.",
                  nowMs);
        } else if (sensorFresh) {
            clear(obs.zoneId, "sensor-offline", nowMs);
        }

        // 2. Out of range — with hysteresis so the alarm doesn't flap.
        if (sensorFresh) {
            const double deviation = obs.lastTempF - obs.setpointF;
            if (std::abs(deviation) > kOutOfRangeF) {
                std::ostringstream msg;
                msg << obs.zoneName << " is " << std::abs(static_cast<int>(deviation))
                    << "°F " << (deviation > 0 ? "above" : "below") << " its target.";
                raise(obs.zoneId, "out-of-range", "warning", msg.str(), nowMs);
            } else if (std::abs(deviation) < kBackInRangeF) {
                clear(obs.zoneId, "out-of-range", nowMs);
            }
        }

        // 3. Ineffective equipment — running, but the temperature isn't responding.
        RunTrend& trend = trends_[obs.zoneId];
        if (trend.state != obs.state) {
            trend = {obs.state, nowMs, obs.lastTempF};
        }
        const bool running = obs.state == HvacState::Cooling || obs.state == HvacState::Heating;
        if (sensorFresh && running &&
            nowMs - trend.sinceMs > kIneffectiveIntervals * intervalMs_) {
            const double improvement = obs.state == HvacState::Cooling
                                           ? trend.tempAtStart - obs.lastTempF
                                           : obs.lastTempF - trend.tempAtStart;
            const double elapsedSec = static_cast<double>(nowMs - trend.sinceMs) / 1000.0;
            const double expected = kMinImprovementFPerSec * elapsedSec;
            if (improvement < expected) {
                const std::string verb =
                    obs.state == HvacState::Cooling ? "cooling" : "heating";
                raise(obs.zoneId, "ineffective-equipment", "warning",
                      obs.zoneName + " " + verb + " has been running but the temperature "
                      "isn't responding — possible stuck damper or failing unit.",
                      nowMs);
            } else {
                clear(obs.zoneId, "ineffective-equipment", nowMs);
            }
        } else if (!running) {
            clear(obs.zoneId, "ineffective-equipment", nowMs);
        }

        // 4. Sensor plausibility — analytical redundancy. Integrate our own
        //    model estimate of the zone (building model + commanded state +
        //    outdoor temp) and compare it against the sensor. A drifting sensor
        //    "looks healthy" to every rule above; it can't fool the model.
        if (sensorFresh) {
            ModelEstimate& est = estimates_[obs.zoneId];
            if (!est.initialized) {
                est = {true, obs.lastTempF, nowMs};
            } else {
                double dtSec = static_cast<double>(nowMs - est.lastMs) / 1000.0;
                dtSec = std::min(std::max(dtSec, 0.0), 60.0);

                double delta =
                    kNomLeakPerSec * obs.leakFactor * (obs.outdoorTempF - est.tempF) * dtSec;
                if (obs.state == HvacState::Cooling) delta -= kNomCoolPerSec * dtSec;
                if (obs.state == HvacState::Heating) delta += kNomHeatPerSec * dtSec;
                est.tempF += delta;
                // Slow anchor toward the sensor: honest model error washes out,
                // a persistent bias (drift) still forces a large steady residual.
                est.tempF += kAnchorPerSec * dtSec * (obs.lastTempF - est.tempF);
                est.lastMs = nowMs;
            }

            const double residual = obs.lastTempF - est.tempF;
            // Broken equipment also diverges from the nominal model — but that
            // already has its own alarm; don't double-report it as a sensor issue.
            const bool equipmentSuspect = isActive(obs.zoneId, "ineffective-equipment");
            if (!equipmentSuspect && std::abs(residual) > kImplausibleF) {
                std::ostringstream msg;
                msg << obs.zoneName << " temperature sensor disagrees with expected building "
                    << "behavior by " << std::abs(static_cast<int>(residual))
                    << "°F — possible sensor drift; reading may not be trustworthy.";
                raise(obs.zoneId, "sensor-implausible", "critical", msg.str(), nowMs);
            } else if (std::abs(residual) < kPlausibleAgainF) {
                clear(obs.zoneId, "sensor-implausible", nowMs);
            }
        }
    }
}
