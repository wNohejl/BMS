# EdgeMonitor — Digital Twin

When the engine starts it loads a building model from **`edge/building.json`**
(parsed with nlohmann/json; falls back to a built-in model): every room, HVAC
unit, sensor, and controller exists as an object in the simulation. The C++
engine runs the control algorithms and physics-like simulation; the .NET
application manages persistence, APIs, and visualization. From the dashboard's
**Digital twin** page you intentionally introduce faults — disconnect a sensor,
drift it, stick a damper, overload an HVAC unit, leak refrigerant — and watch
alarms, control decisions, and recovery happen in real time.

## The building model (`edge/building.json` → `BuildingModel.cpp`)

| Zone | Name | Floor | Character |
|---|---|---|---|
| zone-1 | Lobby | 1 | glass front — leakiest envelope (1.6×) |
| zone-2 | Conference Room | 1 | standard envelope |
| zone-3 | Office 201 | 2 | tight envelope (0.8×) |
| zone-4 | Server Room | 2 | high base load (2.4 kW), best envelope |

Edit `building.json` (or point `EDGEMONITOR_BUILDING_MODEL` at another file) to
change the building — zones, floors, thermal character — without recompiling.
The topology flows to .NET and the dashboard through status snapshots.

## Concurrency model

The physics simulation steps on a dedicated worker thread at 10 Hz and samples
the sensors at 2 Hz; samples flow to the control loop through a **lock-free
SPSC ring buffer** (`SpscRing.h` — acquire/release atomics, wait-free on both
sides, stress-tested with 100k items cross-thread in the self-test). The
control loop — scheduler, command inbox, event dispatch — runs deterministically
at 1 Hz on the main thread and builds each telemetry batch from the freshest
drained samples. Commands and status queries into the simulation are
mutex-guarded; the hot sample path takes no locks.

## Fault injection

Faults enter at the **device layer** (`BuildingSimulation`), exactly where they
happen in a real building. Nothing upstream is told — the symptoms have to be
detected:

| Fault | Physical effect | What you observe |
|---|---|---|
| `sensorOffline` | temperature readings simply stop appearing | `sensor-offline` (critical) after 3 missed reads → controller **fail-safe idles** the zone |
| `sensorDrift` | sensor reports plausible-but-wrong values creeping up ~0.6°F/min | `sensor-implausible` (critical) — analytical redundancy: the monitor's own model estimate diverges from the sensor (~8 min to threshold). The twin page also shows "the sensor reads X but the room is actually Y". |
| `damperStuck` | equipment runs and draws power, but air barely moves (15% effect) | `ineffective-equipment` — running with no temperature progress |
| `damperChatter` | damper alternates stuck/free every 15s (intermittent fault) | one **steady** `ineffective-equipment` alarm — debounce (min-active + hold-off) stops it flapping |
| `hvacOverload` | strained unit: half the output, 1.5× the power | `ineffective-equipment` and/or `out-of-range` |
| `refrigerantLeak` | capacity decays slowly (~18 min to floor) | gradual degradation → eventually `ineffective-equipment` — a failing unit, not a broken one |

Path: Dashboard switch → `POST /api/twin/zones/{zone}/faults` → `DeviceCommand`
audit row → command channel → engine `CommandListener` → `FaultChanged` event →
simulation. Clearing reverses it (drift clear = recalibration, leak clear =
recharge) and alarms auto-clear (recovery).

## Behavior-based alarms (`edge/src/AlarmMonitor.cpp`)

The monitor watches observations only (last reading age, temperature vs
setpoint, progress while equipment runs):

- **sensor-offline** — readings stale for 3 read intervals
- **out-of-range** — > 4°F from target (clears inside 3°F; hysteresis so it doesn't flap)
- **ineffective-equipment** — equipment running for 4+ intervals with progress
  below 0.01°F/s (rate-based, so the judgment is fair at any read interval)
- **sensor-implausible** — analytical redundancy: the monitor integrates its own
  model estimate per zone (building-model leak factor + commanded state + the
  outdoor air sensor, with a slow anchor so honest model error washes out) and
  flags any sensor whose residual exceeds 3°F. This is what catches a drifting
  sensor that looks healthy to every threshold rule. Suppressed while
  `ineffective-equipment` is active so broken equipment isn't double-reported
  as a sensor problem.

All alarms are **debounced**: minimum active duration (2 intervals) and a
re-raise hold-off (6 intervals), so intermittent faults show up as one steady
alarm instead of a flapping one.

The simulation is balanced so healthy equipment always beats worst-case envelope
leak with margin, while a stuck damper can't — that separation is what makes the
detection trustworthy.

## Twin page (dashboard)

- **Floor-plan SVG** per floor: rooms colored by equipment state (blue cooling,
  orange heating), red outline on any zone with a fault or active alarm.
- **Fault switches** per zone for all five fault types.
- **Sensor-vs-model warning** when the reported and actual temperatures diverge.
- **Incident timeline** — the twin's flight recorder: every command sent, every
  alarm raised and cleared, merged newest-first (`GET /api/twin/timeline`, built
  from the `DeviceCommands` audit and `TwinAlarms` history tables).

## Scenarios and replay

Three scripted incidents ship in `ScenarioService` and run from buttons on the
twin page (audited, so they appear on the timeline): **Afternoon meltdown**
(damper + dead sensor + refrigerant leak, then recovery), **Silent drift**, and
**Flapping damper**. **Replay** re-drives the engine with the audited commands
of the last N minutes at compressed 2s spacing — replays go straight over the
command channel and are *not* re-audited, so a replay can never include itself.
`POST /api/twin/scenarios/{name}/run`, `POST /api/twin/replay?minutes=10`.

## Alarm lifecycle in .NET

`ControlService.ProcessStatusAsync` reconciles each engine snapshot against the
`TwinAlarms` table: new alarms are inserted active, missing ones are closed with
`ClearedUtc` — rows are kept as incident history. Changes broadcast
`AlarmsChanged` over SignalR; the twin page updates live.

## Demo script (3 minutes)

1. Start Postgres, API, engine (`edge_agent --local --interval 5`), dashboard.
2. Open **Digital twin** — the model loads from building.json: 4 zones, 2 floors.
3. **Stick the damper** on the Conference Room → within ~30s the
   `ineffective-equipment` alarm appears; its floor-plan room outlines red.
4. **Disconnect the sensor** on Office 201 → critical `sensor-offline` alarm and
   the zone fail-safes to *Holding steady* rather than run blind.
5. **Drift the sensor** on the Lobby → no alarm, but a warning appears on the
   card: "the sensor reads 76°F but the room is actually 73°F" — the silent killer.
6. Clear everything; alarms clear, zones recover, and the **incident timeline**
   at the bottom shows the whole story: commands, alarms, recoveries.

## Verified by

- `edge_agent --selftest` — 45 checks: state machine, loop closure, fault physics
  (stuck/chattering damper, offline + drifting sensors, refrigerant decay),
  behavior-based alarms incl. the model-residual drift catch and debounce,
  fail-safe, SPSC ring FIFO + 100k-item cross-thread stress, building-model parsing.
- `dotnet test api/EdgeMonitor.Api.sln` — 27 tests: telemetry, alerts, control
  orchestration, alarm lifecycle, incident timeline, scenario catalog, replay planning.

## Next iterations

- Implement `BACnetActuator`/`BACnetSensorReader` against bacnet-stack (v1.5) —
  both stubs exist behind the `IDeviceActuator`/`ISensorReader` seams.
- IoT Hub cloud-to-device delivery in `IoTHubCommandChannel` (local file channel
  is fully functional today).
- Persist and replay *named* recorded scenarios (currently: canned scripts +
  replay-from-audit).
- Estimator upgrades: per-zone learned leak factors, occupancy heat gains.
