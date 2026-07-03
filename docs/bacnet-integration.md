# BACnet Integration Notes — v1.5 (Phase 1.5)

Real-building integration via BACnet/IP. Budget **4–8 weeks** — implementations vary
significantly across vendors, and on-site hardware variability is the top production risk (R9).

## Why the risk is contained

`BACnetSensorReader` implements the same `ISensorReader` interface as the simulator.
The `TelemetryPublisher`, the cloud pipeline, the API, and the dashboard change **zero lines**
when real hardware arrives. The blast radius is one class.

## Plan

1. **Library:** [bacnet-stack](https://github.com/bacnet-stack/bacnet-stack) (BSD license, C, widely deployed).
   Link it in `edge/CMakeLists.txt` (the commented block at the bottom).
2. **Discovery:** Who-Is broadcast on the local network → collect I-Am responses → device inventory.
3. **Read:** `ReadProperty` on Analog Input / Analog Value objects → map to `SensorReading`:
   - `object-name` / `description` → `zoneId` (with a per-site mapping file)
   - `present-value` → `value`
   - `units` enum → `unit` string
4. **Polling:** keep the 30s interval; batch exactly as the simulator does.
5. **Testing without a building:** run against a BACnet simulator first — VTS or YABE
   (both free) can serve simulated Analog Input objects on your LAN.
6. **First real install:** bring a BACnet sniffer (Wireshark + BACnet dissector), budget
   2× the estimated integration time, and expect vendor quirks in object naming.

## Checklist (from PHASES.md — Phase 1.5)

- [ ] bacnet-stack linked via CMake
- [ ] Who-Is/I-Am discovery working on the local network
- [ ] ReadProperty maps object model → `SensorReading`
- [ ] Tested against VTS or YABE simulator
- [x] `--bacnet` flag routes through `BACnetSensorReader` (already wired in `main.cpp`)
- [ ] Per-site zone mapping file format decided (JSON: BACnet object id → zoneId)
