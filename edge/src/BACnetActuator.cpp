#include "BACnetActuator.h"

#include <iostream>

BACnetActuator::BACnetActuator(const std::string& networkInterface)
    : networkInterface_(networkInterface) {}

void BACnetActuator::apply(const std::string& zoneId, HvacState state) {
    std::cerr << "[edge] BACnetActuator is a v1.5 stub — command " << hvacStateToString(state)
              << " for " << zoneId << " on " << networkInterface_
              << " was not delivered. See docs/bacnet-integration.md.\n";
}
