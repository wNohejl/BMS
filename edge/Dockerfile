FROM ubuntu:22.04 AS build
RUN apt-get update && apt-get install -y \
    cmake g++ git curl \
    libssl-dev uuid-dev \
    && rm -rf /var/lib/apt/lists/*

RUN git clone https://github.com/Azure/azure-iot-sdk-c.git --recurse-submodules
RUN cmake -S azure-iot-sdk-c -B azure-iot-sdk-c/build \
    -Duse_mqtt=ON -Dskip_samples=ON \
    && cmake --build azure-iot-sdk-c/build --target install

WORKDIR /app
COPY . .
RUN cmake -S . -B build -DUSE_AZURE_IOT=ON && cmake --build build

FROM ubuntu:22.04
RUN apt-get update && apt-get install -y libssl3 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/build/edge_agent /usr/local/bin/edge_agent

# Default: cloud mode (requires IOTHUB_CONNECTION_STRING). Override with --local for dev.
CMD ["edge_agent"]
