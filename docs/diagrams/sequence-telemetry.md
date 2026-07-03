# Telemetry sequence — edge to dashboard

```mermaid
sequenceDiagram
    autonumber
    participant Agent as C++ edge_agent
    participant Hub as Azure IoT Hub (F1)
    participant Listener as IoTHubListenerService
    participant Svc as TelemetryService
    participant Alerts as AlertEvaluationService
    participant DB as PostgreSQL
    participant SR as SignalR (TelemetryHub)
    participant Dash as Blazor dashboard

    loop every 30 seconds
        Agent->>Agent: ISensorReader.readAll() → TelemetryBatch
        Agent->>Hub: MQTT publish (batched JSON, ~0.3KB)
        Note over Agent,Hub: On 403002 quota error:<br/>queue locally until midnight UTC
        Hub-->>Listener: Event Hub-compatible endpoint
        Listener->>Svc: ProcessBatchAsync(TelemetryBatchDto)
        Svc->>DB: INSERT SensorReadings (with TenantId)
        Svc->>SR: ReadingReceived (per reading)
        SR-->>Dash: live tile update
        Svc->>Alerts: EvaluateBatchAsync(batch)
        Alerts->>DB: load active alerts for tenant
        alt threshold breached
            Svc->>SR: AlertTriggered (plainEnglishMessage)
            SR-->>Dash: "Zone 2 temperature is 79.4°F — above your alert level of 78°F."
        end
    end

    Dash->>Svc: GET /api/telemetry/latest (initial tiles)
    Dash->>Svc: GET /api/telemetry?zoneId=&from=&to= (history + costs)
```

Local dev is the same sequence with two substitutions:
`Agent → /tmp/edgemonitor/readings/*.json → FileSystemListenerService` replaces the IoT Hub hop,
and the in-process SignalR hub replaces Azure SignalR.
