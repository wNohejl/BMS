#include "CommandListener.h"

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>

namespace fs = std::filesystem;

namespace minijson {

namespace {
// Position just after the colon of "key": — or npos.
size_t valueStart(const std::string& json, const std::string& key) {
    const std::string needle = "\"" + key + "\"";
    size_t pos = json.find(needle);
    if (pos == std::string::npos) return std::string::npos;
    pos = json.find(':', pos + needle.size());
    if (pos == std::string::npos) return std::string::npos;
    ++pos;
    while (pos < json.size() && (json[pos] == ' ' || json[pos] == '\t')) ++pos;
    return pos;
}
} // namespace

std::string extractString(const std::string& json, const std::string& key) {
    size_t pos = valueStart(json, key);
    if (pos == std::string::npos || pos >= json.size() || json[pos] != '"') return "";
    const size_t end = json.find('"', pos + 1);
    if (end == std::string::npos) return "";
    return json.substr(pos + 1, end - pos - 1);
}

double extractNumber(const std::string& json, const std::string& key, double fallback) {
    size_t pos = valueStart(json, key);
    if (pos == std::string::npos) return fallback;
    char* end = nullptr;
    const double value = std::strtod(json.c_str() + pos, &end);
    if (end == json.c_str() + pos) return fallback;
    return value;
}

} // namespace minijson

CommandListener::CommandListener(EventBus& bus) : bus_(bus) {}

std::string CommandListener::commandDir() const {
    if (const char* dir = std::getenv("EDGEMONITOR_COMMAND_DIR")) {
        return dir;
    }
    return DEFAULT_COMMAND_DIR;
}

void CommandListener::poll(long long nowMs) {
    const fs::path dir = commandDir();
    std::error_code ec;
    fs::create_directories(dir, ec);
    if (ec) return;

    for (const auto& entry : fs::directory_iterator(dir, ec)) {
        if (ec) return;
        if (!entry.is_regular_file() || entry.path().extension() != ".json") continue;
        processFile(entry.path().string(), nowMs);
    }
}

void CommandListener::processFile(const std::string& path, long long nowMs) {
    std::string json;
    {
        std::ifstream in(path, std::ios::binary);
        if (!in) return; // writer may not be done — retry next poll
        std::ostringstream buffer;
        buffer << in.rdbuf();
        json = buffer.str();
    }

    const std::string type = minijson::extractString(json, "type");
    const std::string zoneId = minijson::extractString(json, "zoneId");
    const double value = minijson::extractNumber(json, "value");
    const std::string fault = minijson::extractString(json, "fault");

    if (!type.empty() && !zoneId.empty()) {
        std::cout << "[engine] command received: " << type << " zone=" << zoneId
                  << " value=" << value << (fault.empty() ? "" : " fault=" + fault) << "\n";
        bus_.publish({EventType::CommandReceived, zoneId, type, value, nowMs, fault});
    } else {
        std::cerr << "[engine] ignoring malformed command file: " << path << "\n";
    }

    std::error_code ec;
    fs::remove(path, ec);
}
