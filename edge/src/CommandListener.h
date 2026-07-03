// CommandListener.h — the inbound half of the command channel
// (Dashboard → API → C++ engine). The .NET orchestrator writes JSON command
// files ({"type":"setSetpoint","zoneId":"zone-1","value":74}) into a command
// directory; we poll, emit CommandReceived events, and delete. In cloud mode
// the same commands arrive as IoT Hub cloud-to-device messages (v-next) — the
// event shape on the bus is identical either way.
#pragma once
#include "EventBus.h"

#include <string>

namespace minijson {
// Minimal key extraction for our own controlled command format — deliberately
// not a JSON parser. Swap for a real JSON library when command payloads grow.
std::string extractString(const std::string& json, const std::string& key);
double extractNumber(const std::string& json, const std::string& key, double fallback = 0.0);
} // namespace minijson

class CommandListener {
public:
    explicit CommandListener(EventBus& bus);

    void poll(long long nowMs);

private:
    std::string commandDir() const;
    void processFile(const std::string& path, long long nowMs);

    EventBus& bus_;
    static constexpr const char* DEFAULT_COMMAND_DIR = "/tmp/edgemonitor/commands/";
};
