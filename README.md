# EdgeMonitor — Building Management System Digital Twin

[![ci](https://github.com/wNohejl/BMS/actions/workflows/ci.yml/badge.svg)](https://github.com/wNohejl/BMS/actions/workflows/ci.yml)

> A Building Management System digital twin built with **C++** (control engine +
> physics-like simulation), **C#/.NET 8** (orchestration, APIs, SignalR), **Blazor
> WASM + MudBlazor** (dashboard), and **PostgreSQL** — deployable entirely on Azure
> free tiers ($0/month portfolio phase). Inject faults from the dashboard — a
> disconnected sensor, a stuck damper, an overloaded HVAC unit — and watch alarms,
> control decisions, and recovery happen in real time.

**Status:** local pipeline complete and verified (45 C++ self-test checks, 27 .NET
tests, fault-injection e2e). The Azure footprint is fully authored as IaC
([infra/main.bicep](infra/main.bicep) — IoT Hub F1, Container Apps, SignalR, Static
Web Apps, $10 cost alert) and deliberately not provisioned yet: the portfolio phase
stays at $0, and `az deployment group create` turns it on when wanted.

## What it does

A C++ **control engine** runs the building: an event bus drives per-zone HVAC state
machines (hysteresis + equipment-protection hold times), an occupancy schedule and
rule-based energy optimizer set the targets, and a closed-loop thermal simulation
stands in for real hardware behind `ISensorReader`/`IDeviceActuator` abstractions.
A .NET API owns orchestration, persistence (multi-tenant PostgreSQL), REST APIs, and
SignalR; the MudBlazor dashboard speaks plain English — "Zone 2 is 74°F — normal",
never "SensorType: Temperature, Value: 74.2".

```
        telemetry + status                        commands (event-based)
C++ engine ──MQTT──▶ IoT Hub ──▶ .NET API   Dashboard ──▶ API ──▶ C++ engine ──▶ Device
   ▲                                 │
   └── device (simulation / BACnet)  ├──▶ PostgreSQL (readings, alerts, zone state, audit)
                                     └──▶ SignalR ──▶ Blazor dashboard (live tiles,
                                                      control, history, costs, alerts)
```

Full diagram: [docs/architecture.md](docs/architecture.md) ·
Sequence: [docs/diagrams/sequence-telemetry.md](docs/diagrams/sequence-telemetry.md)

## Run it locally — $0, no Azure account

```bash
# 1. Postgres
docker run -d --name edgemonitor-postgres -e POSTGRES_USER=edgeadmin \
  -e POSTGRES_PASSWORD=localpassword -e POSTGRES_DB=edgemonitordb -p 5432:5432 postgres:15

# 2. API (http://localhost:5080)
cd api/EdgeMonitor.Api && dotnet run

# 3. Edge agent (no Azure SDK needed)
cd edge && cmake -S . -B build && cmake --build build
./build/edge_agent --local --interval 5

# 4. Dashboard (http://localhost:5001)
cd dashboard/EdgeMonitor.Dashboard && dotnet watch run
```

Details and troubleshooting: [docs/local-dev-setup.md](docs/local-dev-setup.md)

## Build phases (the Free Path)

The project is built in strict milestones — see [PHASES.md](PHASES.md).

| Phase | Deliverable | Cost |
|---|---|---|
| 1 | Local pipeline end-to-end (agent → API → dashboard) | $0 |
| 2 | Cloud pipeline on Azure free tiers (IoT Hub F1, Neon.tech) | $0 |
| 3 | Live dashboard at a public URL (Static Web App) | $0 |
| 4 | Plain-English alerts + energy cost widget | $0 |
| 5 | CI/CD (Azure DevOps + GitHub Actions) + polish | $0 |
| 1.5 | Real BACnet buildings via bacnet-stack | post-portfolio |

## Repo layout

```
edge/        C++ control engine — EventBus, Scheduler, ZoneStateMachine, ZoneController,
             BuildingSimulation (closed loop), Optimizer, CommandListener, quota-guarded publisher
api/         .NET 8 Web API + tests — orchestration, command channel, persistence, TenantId everywhere
dashboard/   Blazor WASM + MudBlazor — live tiles, zone control, history/costs, alerts
infra/       Bicep: IoT Hub F1 + Container App + SignalR + SWA + $10 cost alert, one command
.azure/      Azure DevOps pipeline        .github/  Static Web App deploy workflow
deployment/  IoT Edge manifest            docs/     architecture, costs, BACnet, dev setup
```

## Tests

```bash
dotnet test api/EdgeMonitor.Api.sln     # 27 tests: telemetry, alerts, control, alarms, timeline, scenarios
./edge/build/edge_agent --selftest     # 45 checks: state machine, fault physics, alarms, SPSC ring, model
```

## Code tour (the interesting parts, in reading order)

For reviewers and interviewers — the five files that carry the engineering weight:

1. **[edge/src/SpscRing.h](edge/src/SpscRing.h)** — lock-free single-producer/single-consumer
   ring buffer bridging the 10 Hz physics thread into the 1 Hz control loop. Fixed
   capacity, acquire/release memory ordering, no mutexes on the hot path;
   stress-tested cross-thread with 100k items in the self-test.
2. **[edge/src/ZoneStateMachine.cpp](edge/src/ZoneStateMachine.cpp)** — per-zone HVAC
   state machine with hysteresis bands and equipment-protection hold times (a real
   compressor can't short-cycle; neither can this one).
3. **[edge/src/AlarmMonitor.cpp](edge/src/AlarmMonitor.cpp)** — behavior-based fault
   detection by analytical redundancy: the monitor compares observed behavior against
   the thermal model's expectation, so a *drifting* sensor that stays in-range is still
   caught. Alarm debounce keeps chatter out. It is never told what broke — it infers.
4. **[edge/src/BuildingSimulation.cpp](edge/src/BuildingSimulation.cpp)** — the
   closed-loop thermal model standing in for real hardware behind
   `ISensorReader`/`IDeviceActuator`, which is also exactly where
   [BACnet](docs/bacnet-integration.md) plugs in for real buildings.
5. **[api/EdgeMonitor.Api](api/EdgeMonitor.Api)** — the .NET 8 side: ingestion,
   command channel back down to the C++ engine, PostgreSQL persistence with an audit
   trail, and SignalR fan-out to the Blazor dashboard.

The C++/.NET boundary is the point: telemetry flows up (C++ → MQTT/local channel →
.NET → SignalR → browser), commands flow down (browser → REST → .NET → C++ actuators),
and each side plays to its strength.

## Digital twin

The engine loads a building model at startup — every room, HVAC unit, sensor, and
controller is a simulation object. The **Digital twin** dashboard page injects
faults at the device layer; the engine's alarm monitor detects the *symptoms*
(it's never told what broke), controllers fail-safe where needed, and everything
recovers when the fault clears. Full walkthrough: [docs/digital-twin.md](docs/digital-twin.md)

## Cost posture

Everything through Phase 5 runs on free tiers — IoT Hub F1, Container Apps free grant,
SignalR Free_F1, Neon.tech PostgreSQL, ghcr.io, Static Web Apps. A $10 budget alert is
provisioned by Bicep so any unexpected charge emails immediately.
Breakdown and upgrade triggers: [docs/cost-model.md](docs/cost-model.md)
