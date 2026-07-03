# EdgeMonitor — The Free Path (Build Phases)

> Phased build plan for EdgeMonitor, derived from the Build Order & Milestones in
> `vault/Business Management Systems/Designs/Free Design.md`.
> Work strictly in order. Do not start the next phase until the current one is
> committed, working, and pushed to GitHub. Cost stays **$0/month** through Phase 5.

---

## Phase 0 — Pre-work: Azure IoT C SDK familiarization (1–2 weeks)

Front-load the SDK learning so it doesn't block Phase 2 mid-build.

- [ ] Azure IoT C SDK compiled and linked via CMake (`-DUSE_AZURE_IOT=ON` in `edge/CMakeLists.txt`)
- [ ] Simple "Hello IoT Hub" program sends a message; verify it in the Azure Portal
- [ ] Understand `IOTHUB_DEVICE_CLIENT_LL_HANDLE` lifecycle and the `403002 IoTHubQuotaExceeded` error

**Cost: $0** (IoT Hub F1 free tier for the hello-world test)

---

## Phase 1 — Local pipeline end-to-end (Week 1–2) — Milestone 1

**Goal:** Sensor data flows from C++ agent to dashboard without touching Azure.

Code in this repo:

| Item | Location |
|---|---|
| `ISensorReader` interface | `edge/src/ISensorReader.h` |
| `SimulatedSensorReader` (sine-wave temp/power/occupancy) | `edge/src/SimulatedSensorReader.cpp` |
| `TelemetryBatch` JSON serialization | `edge/src/TelemetryBatch.cpp` |
| `TelemetryPublisher --local` → `/tmp/edgemonitor/readings/` | `edge/src/TelemetryPublisher.cpp` |
| `FileSystemListenerService` | `api/EdgeMonitor.Api/Services/FileSystemListenerService.cs` |
| `SensorReading` + `Alert` models with `TenantId` | `api/EdgeMonitor.Api/Models/` |
| Blazor live sensor tiles | `dashboard/EdgeMonitor.Dashboard/Pages/Home.razor` |

Checklist:

- [x] `ISensorReader` interface defined
- [x] `SimulatedSensorReader` generates realistic sine-wave temp/power/occupancy data
- [x] `TelemetryBatch` serializes multiple readings to JSON
- [x] `TelemetryPublisher` with `--local` flag writes batches to `/tmp/edgemonitor/readings/`
- [x] `FileSystemListenerService` picks up JSON files and feeds the pipeline
- [x] `SensorReading` and `Alert` EF Core models include `TenantId`
- [ ] EF Core migration applied to local Postgres (`dotnet ef migrations add InitialCreate && dotnet ef database update`)
- [x] Blazor home page shows live sensor tiles updating from local SignalR
- [ ] **Demo:** C++ agent running + dashboard open in browser, tiles update every 30 seconds

**How to run:** see `docs/local-dev-setup.md`. **Cost: $0**

---

## Phase 2 — Cloud pipeline on free tier (Week 2–3) — Milestone 2

**Goal:** Same pipeline as Phase 1, but data flows through Azure IoT Hub F1 (free) and Neon.tech PostgreSQL (free).

- [ ] Neon.tech project + database created; connection string saved as Container App secret
- [ ] `az deployment group create -g rg-edgemonitor -f infra/main.bicep -p @infra/parameters.json` provisions IoT Hub F1, Container App, SignalR Free_F1, Static Web App in one command
- [ ] Azure Cost Alert set at $10/month (included in `infra/main.bicep`)
- [ ] Rebuild edge agent with `-DUSE_AZURE_IOT=ON`; `TelemetryPublisher` sends batched MQTT payloads to IoT Hub F1 (30s interval ≈ 2,880 msgs/day, under the 8,000/day F1 cap)
- [ ] `IoTHubListenerService` reads from the Event Hub-compatible endpoint (already implemented — set `Listener:EventHubCompatibleEndpoint`)
- [ ] `403002` quota exception handled with local queue fallback (implemented in `TelemetryPublisher`)
- [ ] EF Core migrations run against Neon.tech PostgreSQL
- [ ] `GET /api/telemetry` returns stored readings as JSON
- [x] Unit tests for `TelemetryService` and `AlertEvaluationService` pass (`dotnet test api/EdgeMonitor.Api.sln`)
- [ ] **Demo:** readings appear in Azure Portal → IoT Hub within 60 seconds; Azure cost still $0

**Cost: $0** (F1 hub, Container Apps free grant, SignalR Free_F1, Neon.tech, ghcr.io)

---

## Phase 3 — Real-time dashboard on Azure (Week 3–4) — Milestone 3

**Goal:** Live dashboard accessible at a public URL.

- [ ] Azure SignalR upgraded Free_F1 → Standard_S1 the day before multi-viewer testing (downgrade after)
- [ ] Blazor dashboard deployed to Azure Static Web App with public URL (`.github/workflows/deploy-dashboard.yml`)
- [ ] Home page shows live updating tiles from Azure SignalR
- [x] All sensor labels use plain English ("Zone 1 is 72°F — normal")
- [ ] **Demo:** share the public URL; someone else opens it on their phone; tiles update live

**Cost: $0** normally; ~$1.30/day only while Standard_S1 is active for demos

---

## Phase 4 — Alerts and energy cost widget (Week 4–5) — Milestone 4

- [x] `AlertEvaluationService` evaluates thresholds and emits `AlertTriggered` with `plainEnglishMessage`
- [x] `POST /api/alerts` creates threshold rules
- [x] `AlertBadge` shows plain-English alert notifications
- [x] `EnergyCostWidget` shows estimated cost delta vs prior period
- [x] Alerts page lets user configure thresholds in plain English
- [ ] **Demo:** set a threshold lower than current temperature; confirm the alert fires with a readable message

**Cost: $0**

---

## Phase 4.5 — C++ control engine (implemented)

The edge agent is a full event-driven control engine, not just a telemetry reader.
Control flow: **Dashboard → API → C++ engine → Device**, all event-based.

| Responsibility | C++ (engine) | .NET (orchestration) |
|---|---|---|
| Scheduling | `Scheduler` — sensor cadence, optimizer passes, status heartbeats | — |
| State machine | `ZoneStateMachine` — Idle/Cooling/Heating, hysteresis + min-state hold | — |
| Control logic | `ZoneController` — schedule + manual + optimizer → effective setpoint | — |
| Simulation | `BuildingSimulation` — closed-loop thermal model (ISensorReader + IDeviceActuator) | — |
| Optimization | `Optimizer` — peak-hour setback, pre-cooling | — |
| Events | `EventBus` — CommandReceived / StateChanged / SetpointChanged | SignalR `ZoneStateChanged` |
| Commands | `CommandListener` — polls command inbox | `ICommandChannel` (file / IoT Hub C2D) |
| Persistence | — | `ZoneControl`, `ZoneStatus`, `DeviceCommand` (audit) in Postgres |
| UI | — | MudBlazor `Control` page (setpoint hold / back to schedule) |
| APIs | — | `GET/POST/DELETE /api/control/zones…` |

- [x] `edge_agent --selftest` — 31 built-in checks (state machine, loop closure, fault physics, alarms, optimizer, parsing)
- [x] End-to-end verified: command file → engine → manual setpoint → status snapshot
- [ ] v1.5: `BACnetActuator` (WriteProperty) + IoT Hub C2D delivery in `IoTHubCommandChannel`

**Cost: $0**

---

## Phase 4.6 — Digital twin (implemented)

The engine loads a building model at startup — every room, HVAC unit, sensor, and
controller is an object in the simulation (4 zones across 2 floors). From the
dashboard's **Digital twin** page you inject faults and watch alarms, control
decisions, and recovery happen live. See `docs/digital-twin.md`.

- [x] Building model loads from `edge/building.json` (nlohmann/json parser; env override; built-in fallback)
- [x] Fault injection at the device layer: `sensorOffline`, `sensorDrift`, `damperStuck`,
      `damperChatter`, `hvacOverload`, `refrigerantLeak`
- [x] Behavior-based `AlarmMonitor` — detects symptoms, never told which fault was injected;
      rate-based judgment works at any read interval
- [x] Analytical redundancy: `sensor-implausible` alarm from a per-zone model-residual
      estimate — catches the drifting sensor that evades every threshold rule
- [x] Alarm debounce: minimum-active + re-raise hold-off — intermittent faults show one steady alarm
- [x] Recovery: fail-safe idle on sensor loss; alarms auto-clear when conditions resolve
- [x] Threaded engine: physics at 10 Hz on a worker thread, lock-free SPSC sample ring to
      the 1 Hz control loop (stress-tested in the self-test)
- [x] .NET alarm lifecycle: `TwinAlarms` incident history + `AlarmsChanged` SignalR broadcast
- [x] Incident timeline (`GET /api/twin/timeline`) — commands + alarm raise/clear, merged
- [x] Scripted scenarios (`/api/twin/scenarios`) + replay-from-audit (`/api/twin/replay`)
- [x] MudBlazor Twin page: floor-plan SVG, fault switches, sensor-vs-model divergence
      warning, scenario buttons, live alarm feed, incident timeline
- [ ] v1.5: implement `BACnetActuator`/`BACnetSensorReader` stubs against bacnet-stack;
      wire IoT Hub C2D in `IoTHubCommandChannel`

**Cost: $0**

---

## Phase 5 — CI/CD and portfolio polish (Week 5–6) — Milestone 5

- [ ] Azure DevOps pipeline (`.azure/azure-pipelines.yml`) pushes C++ + API images to ghcr.io and deploys the Container App
- [ ] Blazor dashboard deploys to Azure Static Web Apps via GitHub Actions
- [ ] `README.md` includes architecture diagram, live demo URL, one-paragraph description
- [x] Sequence diagram in `docs/diagrams/sequence-telemetry.md`
- [x] `docs/cost-model.md` documents per-building cost breakdown and upgrade triggers
- [ ] Record 2–3 minute screen capture demo for LinkedIn and the GitHub README
- [ ] Confirm the Azure bill is $0 after the full pipeline is running
- [ ] **Demo:** push a commit; pipeline runs; dashboard updates live at the public URL; cost = $0

**Cost: $0**

---

## Phase 6 — Resume (after Phase 5) — Milestone 6

- [ ] Add EdgeMonitor to the Projects section of the resume
- [ ] Update resume summary to reference Azure IoT Edge experience
- [ ] Submit application to the Schneider Electric Senior role

---

## Phase 1.5 — BACnet real building integration (post-portfolio, pre-first-customer)

Budget 4–8 weeks — real BACnet implementations vary significantly across vendors.

- [ ] `BACnetSensorReader` implemented using [bacnet-stack](https://github.com/bacnet-stack/bacnet-stack) (stub lives at `edge/src/BACnetSensorReader.cpp`)
- [ ] BACnet/IP device discovery working on the local network
- [ ] `ReadProperty` maps the BACnet object model to `SensorReading`
- [ ] Tested against a real HVAC controller or a BACnet simulator (VTS or YABE)
- [x] `--bacnet` flag routes through `BACnetSensorReader` in `main.cpp`

See `docs/bacnet-integration.md`.

---

## Upgrade triggers (pay only when there's a reason)

| Trigger | Upgrade | Cost added |
|---|---|---|
| Live demo with multiple viewers | SignalR Free_F1 → Standard_S1 | +$40/month (downgrade after) |
| First paying customer | IoT Hub F1 → S1 | +$25/month |
| First paying customer | Neon.tech → Azure PostgreSQL B1ms | +$15/month |
| First paying customer | ghcr.io → Azure Container Registry Basic | +$5/month |
| 10+ paying buildings | Container App min-replicas 0 → 1 | +$10–13/month |
