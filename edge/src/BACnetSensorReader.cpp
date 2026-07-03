#include "BACnetSensorReader.h"

#include <iostream>

BACnetSensorReader::BACnetSensorReader(const std::string& networkInterface)
    : networkInterface_(networkInterface) {}

bool BACnetSensorReader::isAvailable() const {
    // v1.5: return true once bacnet-stack discovery finds at least one device
    // on networkInterface_.
    return false;
}

std::vector<SensorReading> BACnetSensorReader::readAll() {
    std::cerr << "[edge] BACnetSensorReader is a v1.5 stub — no readings produced. "
              << "See docs/bacnet-integration.md.\n";
    return {};
}
