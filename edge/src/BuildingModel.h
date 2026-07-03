// BuildingModel.h — loads the building topology (rooms, thermal character)
// from building.json so the same definition can drive the engine, the .NET
// orchestrator, and the dashboard. Falls back to a built-in model when no
// file is present, so the engine always starts.
#pragma once
#include <string>
#include <vector>

struct ZoneSpec {
    std::string id;
    std::string name;
    int floor = 1;
    double initialTempF = 72.0;
    double basePowerKw = 0.5;
    double leakFactor = 1.0;
};

struct BuildingModel {
    std::string name = "Demo Office Building";
    std::vector<ZoneSpec> zones;
};

// Parse a building model from JSON text. Throws std::runtime_error with a
// readable message on malformed input.
BuildingModel parseBuildingModel(const std::string& json);

// Resolution order: EDGEMONITOR_BUILDING_MODEL env var → ./building.json →
// built-in defaults. Never fails; logs which source was used.
BuildingModel loadBuildingModel();

BuildingModel defaultBuildingModel();
