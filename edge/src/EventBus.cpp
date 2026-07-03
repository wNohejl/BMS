#include "EventBus.h"

void EventBus::subscribe(EventType type, Handler handler) {
    handlers_[type].push_back(std::move(handler));
}

void EventBus::publish(const Event& event) {
    queue_.push(event);
}

void EventBus::dispatchAll() {
    // Handlers may publish follow-up events; cap the cascade so a handler that
    // re-publishes its own trigger can't spin the loop forever.
    int budget = 1000;
    while (!queue_.empty() && budget-- > 0) {
        Event event = queue_.front();
        queue_.pop();
        auto it = handlers_.find(event.type);
        if (it == handlers_.end()) continue;
        for (auto& handler : it->second) {
            handler(event);
        }
    }
}
