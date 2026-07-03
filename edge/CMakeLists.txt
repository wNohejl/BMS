cmake_minimum_required(VERSION 3.16)
project(EdgeMonitorAgent CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# OFF by default so Milestone 1 (local pipeline, $0) builds with no Azure SDK.
# Turn ON for Milestone 2 cloud publishing:
#   cmake -S . -B build -DUSE_AZURE_IOT=ON
option(USE_AZURE_IOT "Link the Azure IoT C SDK for cloud publishing" OFF)

find_package(Threads REQUIRED)

add_executable(edge_agent
    src/main.cpp
    src/AlarmMonitor.cpp
    src/BACnetActuator.cpp
    src/BACnetSensorReader.cpp
    src/BuildingModel.cpp
    src/BuildingSimulation.cpp
    src/CommandListener.cpp
    src/Engine.cpp
    src/EventBus.cpp
    src/Optimizer.cpp
    src/Scheduler.cpp
    src/SimulatedSensorReader.cpp
    src/TelemetryBatch.cpp
    src/TelemetryPublisher.cpp
    src/ZoneController.cpp
    src/ZoneStateMachine.cpp
)

target_link_libraries(edge_agent PRIVATE Threads::Threads)

if(USE_AZURE_IOT)
    find_package(azure_iot_sdks REQUIRED)
    target_compile_definitions(edge_agent PRIVATE USE_AZURE_IOT)
    target_link_libraries(edge_agent
        iothub_client
        iothub_client_mqtt_transport
    )
endif()

# v1.5: add bacnet-stack linkage here
# find_library(BACNET_LIB bacnet-stack HINTS /usr/local/lib)
# target_link_libraries(edge_agent ${BACNET_LIB})
