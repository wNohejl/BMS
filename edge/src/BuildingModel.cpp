#include "BuildingModel.h"

#include "../third_party/nlohmann/json.hpp"

#include <cstdlib>
#include <fstream>
#include <iostream>
#include <sstream>
#include <stdexcept>

using nlohmann::json;

BuildingModel defaultBuildingModel() {
    BuildingModel model;
    model.zones.push_back({"zone-1", "Lobby", 1, 74.0, 0.8, 1.6});
    model.zones.push_back({"zone-2", "Conference Room", 1, 76.5, 0.5, 1.0});
    model.zones.push_back({"zone-3", "Office 201", 2, 73.0, 0.6, 0.8});
    model.zones.push_back({"zone-4", "Server Room", 2, 78.0, 2.4, 0.5});
    return model;
}

BuildingModel parseBuildingModel(const std::string& text) {
    json doc;
    try {
        doc = json::parse(text);
    } catch (const json::parse_error& ex) {
        throw std::runtime_error(std::string("building model is not valid JSON: ") + ex.what());
    }

    BuildingModel model;
    model.name = doc.value("name", model.name);

    if (!doc.contains("zones") || !doc["zones"].is_array() || doc["zones"].empty()) {
        throw std::runtime_error("building model needs a non-empty \"zones\" array");
    }

    for (const auto& z : doc["zones"]) {
        ZoneSpec spec;
        spec.id = z.value("id", "");
        spec.name = z.value("name", spec.id);
        spec.floor = z.value("floor", 1);
        spec.initialTempF = z.value("initialTempF", 72.0);
        spec.basePowerKw = z.value("basePowerKw", 0.5);
        spec.leakFactor = z.value("leakFactor", 1.0);
        if (spec.id.empty()) {
            throw std::runtime_error("every zone needs an \"id\"");
        }
        model.zones.push_back(spec);
    }
    return model;
}

BuildingModel loadBuildingModel() {
    std::string path = "building.json";
    if (const char* env = std::getenv("EDGEMONITOR_BUILDING_MODEL")) {
        path = env;
    }

    std::ifstream in(path, std::ios::binary);
    if (!in) {
        std::cout << "[engine] no building model at " << path << " — using built-in model\n";
        return defaultBuildingModel();
    }

    std::ostringstream buffer;
    buffer << in.rdbuf();
    try {
        BuildingModel model = parseBuildingModel(buffer.str());
        std::cout << "[engine] loaded building model \"" << model.name << "\" from " << path
                  << " (" << model.zones.size() << " zones)\n";
        return model;
    } catch (const std::exception& ex) {
        std::cerr << "[engine] " << path << ": " << ex.what() << " — using built-in model\n";
        return defaultBuildingModel();
    }
}
