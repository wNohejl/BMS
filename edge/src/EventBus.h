// EventBus.h — the spine of the engine. Everything that happens (a sensor read,
// a command from the .NET orchestrator, a state-machine transition, a fault, an
// alarm) is an Event; components subscribe rather than call each other directly.
#pragma once
#include <functional>
#include <map>
#include <queue>
#include <string>
#include <vector>

enum class EventType {
    SensorRead,       // simulation/hardware produced a new temperature (value = °F)
    CommandReceived,  // command from .NET orchestration (name = command type)
    StateChanged,     // a zone state machine transitioned (name = new state)
    SetpointChanged,  // effective setpoint moved (command or optimizer; value = °F)
    FaultChanged,     // a fault was injected or cleared (name = fault, value = 1/0)
    AlarmRaised,      // alarm monitor detected a problem (name = type, detail = message)
    AlarmCleared,     // alarm condition no longer observed
};

struct Event {
    EventType type;
    std::string zoneId;
    std::string name;
    double value = 0.0;
    long long timestampMs = 0;
    std::string detail; // free text: alarm message, fault name, transition reason
};

class EventBus {
public:
    using Handler = std::function<void(const Event&)>;

    void subscribe(EventType type, Handler handler);
    void publish(const Event& event);
    void dispatchAll();

private:
    std::map<EventType, std::vector<Handler>> handlers_;
    std::queue<Event> queue_;
};
