# Local Development Setup — $0, no Azure dependency

The complete inner loop: C++ agent → JSON files → C# API → in-process SignalR → Blazor dashboard.

## Prerequisites

- .NET 8 SDK (or newer — projects roll forward)
- CMake 3.16+ and a C++17 compiler (GCC/Clang/MSVC/MinGW)
- Docker Desktop (for local PostgreSQL)

## Step 1 — Start local PostgreSQL

```bash
docker run -d \
  --name edgemonitor-postgres \
  -e POSTGRES_USER=edgeadmin \
  -e POSTGRES_PASSWORD=localpassword \
  -e POSTGRES_DB=edgemonitordb \
  -p 5432:5432 \
  postgres:15
```

## Step 2 — Start the API

```bash
cd api/EdgeMonitor.Api
dotnet run
# → http://localhost:5080  (Swagger at /swagger)
# FileSystemListenerService watches /tmp/edgemonitor/readings/
# In-process SignalR hub at /hubs/telemetry — no Azure SignalR needed
# Dev mode auto-creates the DB schema (Database:AutoCreate = true)
```

To use real EF migrations instead of auto-create:

```bash
dotnet tool install -g dotnet-ef        # once
dotnet ef migrations add InitialCreate
dotnet ef database update
# then set Database:AutoCreate to false in appsettings.Development.json
```

## Step 3 — Build and run the C++ edge agent

```bash
cd edge
cmake -S . -B build          # Azure SDK NOT required (USE_AZURE_IOT=OFF default)
cmake --build build

./build/edge_agent --local   # writes a batch every 30s
./build/edge_agent --local --interval 5   # faster feedback while developing
./build/edge_agent --output-stdout        # print JSON to terminal instead
```

**Windows note:** the default output dir `/tmp/edgemonitor/readings/` resolves to
`C:\tmp\edgemonitor\readings\` for the C++ agent but the .NET API resolves it relative
to the current drive too — both land on the same folder when run from `C:`. If they
don't line up, point both at an explicit folder:

```powershell
$env:EDGEMONITOR_OUTPUT_DIR = "C:\tmp\edgemonitor\readings"   # agent
# and set Listener:LocalDirectory to the same path in appsettings.Development.json
```

## Step 4 — Start the dashboard

```bash
cd dashboard/EdgeMonitor.Dashboard
dotnet watch run
# → http://localhost:5001 — tiles update as batches arrive
```

## Verify the pipeline

1. Agent console: `[edge] wrote batch (5 readings)` and `[engine] zone-1 -> cooling …`
2. API console: `Processed 5 readings from batch_....json`
3. Dashboard Overview: tiles update within a couple of seconds of each batch
4. `curl http://localhost:5080/api/telemetry?limit=5` returns stored readings
5. Add an alert on the Alerts page with a threshold below the current temperature — a plain-English alert appears within one interval

## Verify the control loop (Dashboard → API → engine → device)

1. Open the **Control** page — zone cards show live state chips (Cooling / Heating / Holding steady)
2. Drag a setpoint slider and click **Hold this temperature**
3. API console: `Command sent to engine: setSetpoint zone=zone-1 …`
4. Engine console: `[engine] command received` then `[engine] zone-1 setpoint via manual -> …`
5. The zone chip flips (e.g. to Cooling) as the state machine reacts; **Back to schedule** reverts it
6. Directories involved: commands flow through `/tmp/edgemonitor/commands/`, status snapshots come back as `status_*.json` in the readings directory (override with `EDGEMONITOR_COMMAND_DIR` to match `Listener:CommandDirectory`)

Engine self-test (no services needed):

```bash
./edge/build/edge_agent --selftest   # 19 checks — state machine, loop closure, optimizer, parsing
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| Tiles never appear | Is the API on :5080? Check `wwwroot/appsettings.json` → `ApiBaseUrl`. |
| API can't reach Postgres | `docker ps` — is `edgemonitor-postgres` running on :5432? |
| Agent writes but API doesn't process | Output dir mismatch — see the Windows note above. |
| CORS errors in browser console | Dashboard origin must be listed in `Cors:AllowedOrigins` (5001 is by default). |
