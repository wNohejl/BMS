# EdgeMonitor — Architecture

> A lightweight Building Management System edge monitoring platform:
> C++ (IoT Edge) → Azure IoT Hub → C#/.NET API + SignalR → Blazor WASM dashboard, with PostgreSQL storage.

## Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                          EDGE LAYER                              │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │            C++ IoT Edge Module (Docker container)          │  │
│  │   ISensorReader (interface)                                │  │
│  │   ├── SimulatedSensorReader   ← v1 (portfolio)            │  │
│  │   └── BACnetSensorReader      ← v1.5 (real buildings)     │  │
│  │   Batches readings → TelemetryPublisher (quota guard +    │  │
│  │   local queue fallback)                                    │  │
│  └──────────────────────────┬─────────────────────────────────┘  │
└─────────────────────────────│────────────────────────────────────┘
                              │ MQTT (batched payloads — F1 free tier)
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                         AZURE CLOUD                              │
│  ┌──────────────┐  event   ┌───────────────────────────────┐    │
│  │ Azure IoT    │──trigger─▶  C# .NET Container App        │    │
│  │ Hub (F1)     │          │  min-replicas=0 (free)        │    │
│  └──────────────┘          │  • REST API  • SignalR hub    │    │
│                            │  • IoTHubListenerService      │    │
│                            │  • FileSystemListenerService  │    │
│                            └──────────┬──────────┬─────────┘    │
│                              ┌────────▼──┐  ┌────▼───────────┐  │
│                              │ Postgres  │  │ Azure SignalR  │  │
│                              │ (TenantId │  │ (Free_F1)      │  │
│                              │  day one) │  └────────┬───────┘  │
│                              └───────────┘           │          │
└──────────────────────────────────────────────────────│──────────┘
                                                       │ WebSocket
                                                       ▼
                                      ┌────────────────────────────┐
                                      │  Blazor WASM Dashboard     │
                                      │  (Azure Static Web App)    │
                                      └────────────────────────────┘
```

## Data flow (production)

1. The C++ edge module reads sensors via `ISensorReader`, batches readings, and publishes one MQTT payload per 30s interval to Azure IoT Hub (quota guard queues locally on `403002`).
2. `IoTHubListenerService` reads the Event Hub-compatible endpoint and deserializes each batch.
3. `TelemetryService` persists readings to PostgreSQL (every entity carries `TenantId`), asks `AlertEvaluationService` to check thresholds, and broadcasts `ReadingReceived` / `AlertTriggered` via SignalR.
4. The Blazor dashboard subscribes to SignalR for live tiles and queries the REST API for history and costs.

## Data flow (local dev — $0)

1. `edge_agent --local` writes JSON batches to `/tmp/edgemonitor/readings/` (tmp-file-then-rename so readers never see partial files).
2. `FileSystemListenerService` polls that directory and feeds the **same** `TelemetryService` pipeline.
3. PostgreSQL runs in local Docker; SignalR is the in-process hub. Nothing touches Azure.

## Control flow (Dashboard → API → C++ → Device, event-based)

Telemetry flows up; control flows down. The C++ side is a full engine, not a reader:

```
Dashboard (MudBlazor Control page)
   │ POST /api/control/zones/{zone}/setpoint
   ▼
.NET API — ControlService: persist ZoneControl, audit DeviceCommand,
   │        ICommandChannel.SendAsync
   ▼ command file (local) / IoT Hub C2D (prod, v-next)
C++ Engine — CommandListener → EventBus (CommandReceived)
   │            → ZoneController (control logic: schedule + manual + optimizer)
   │            → ZoneStateMachine (hysteresis, min-state hold)
   ▼
IDeviceActuator → Device (BuildingSimulation v1 / BACnet v1.5)
   │  temperatures respond → ISensorReader → telemetry + status snapshots
   ▼
.NET API → PostgreSQL (ZoneStatus) + SignalR "ZoneStateChanged" → Dashboard chip updates
```

Engine responsibilities (all in `edge/src/`): **scheduling** (`Scheduler`),
**state machines** (`ZoneStateMachine`), **control logic** (`ZoneController`),
**simulation** (`BuildingSimulation` — implements both `ISensorReader` and
`IDeviceActuator`, so the loop closes), **optimization** (`Optimizer` — peak-hour
setback + pre-cooling), all coordinated over an **event bus** (`EventBus`).
`edge_agent --selftest` runs 19 built-in checks over this logic.

.NET responsibilities: orchestration (`ControlService`), persistence
(`ZoneControl`/`ZoneStatus`/`DeviceCommand` audit), APIs (`ControlController`),
real-time UI fan-out (SignalR), and the command channel (`ICommandChannel`).

## Key design decisions

| Decision | Why |
|---|---|
| `ISensorReader` abstraction | Contains the BACnet integration risk (R2). Swapping simulated → real hardware changes zero cloud code. |
| Batched telemetry | One payload per interval instead of one per reading keeps IoT Hub F1 under its 8,000 msgs/day cap. |
| Two listener services, one pipeline | Local dev and production differ only at the ingestion edge — everything downstream is identical and testable. |
| `ITelemetryBroadcaster` interface | `TelemetryService` is unit-testable without a running SignalR hub. |
| `TenantId` on every entity | Multi-tenancy from day one; every query and endpoint filters by tenant. |
| Plain-English everywhere | Dashboard labels must pass the "restaurant manager" test — it's a product decision, not a style choice. |

## Repository map

```
edge/        C++ agent (CMake; -DUSE_AZURE_IOT=ON links the Azure IoT C SDK)
api/         .NET 8 Web API + xUnit tests (EdgeMonitor.Api.sln)
dashboard/   Blazor WASM (EdgeMonitor.Dashboard.sln)
infra/       Bicep — one-command provision of all free-tier resources
.azure/      Azure DevOps pipeline
.github/     GitHub Actions — Static Web App deploy
deployment/  IoT Edge deployment manifest
docs/        This folder
```

See [diagrams/sequence-telemetry.md](diagrams/sequence-telemetry.md) for the full message sequence.
