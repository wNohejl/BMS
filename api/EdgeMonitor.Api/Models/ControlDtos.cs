namespace EdgeMonitor.Api.Models;

/// <summary>Status snapshot emitted by the C++ engine (status_*.json / IoT Hub message).</summary>
public class ZoneStatusBatchDto
{
    public string TenantId { get; set; } = "tenant-demo";
    public string DeviceId { get; set; } = "";
    public long TimestampMs { get; set; }
    public List<ZoneStatusDto> Zones { get; set; } = new();
    public List<AlarmDto> Alarms { get; set; } = new();
}

public class ZoneStatusDto
{
    public string ZoneId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Floor { get; set; }
    public string State { get; set; } = "";
    public double SetpointF { get; set; }
    public double TempF { get; set; }        // model ground truth
    public double SensorTempF { get; set; }  // what the sensor claims (drift shows here)
    public bool Occupied { get; set; }
    public List<string> Faults { get; set; } = new();
}

/// <summary>An active alarm as reported by the engine's AlarmMonitor.</summary>
public class AlarmDto
{
    public string ZoneId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public long SinceMs { get; set; }
}

/// <summary>SignalR "ZoneStateChanged" event and GET /api/control/zones response shape.</summary>
public class ZoneStateEventDto
{
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Floor { get; set; }
    public string State { get; set; } = "";
    public double SetpointF { get; set; }
    public double TempF { get; set; }
    public double SensorTempF { get; set; }
    public bool Occupied { get; set; }
    public List<string> Faults { get; set; } = new();
    public string Mode { get; set; } = "schedule";
    public double? ManualSetpointF { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>SignalR "AlarmsChanged" event and GET /api/twin/alarms response shape.</summary>
public class ActiveAlarmDto
{
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime RaisedUtc { get; set; }
}

public class SetSetpointRequest
{
    public string TenantId { get; set; } = "tenant-demo";
    public double Value { get; set; }
}

public class FaultRequest
{
    public string TenantId { get; set; } = "tenant-demo";
    public string Fault { get; set; } = "";
}

/// <summary>One entry of the incident timeline: commands, alarms raised/cleared.</summary>
public class TimelineEventDto
{
    public DateTime Timestamp { get; set; }
    public string ZoneId { get; set; } = "";
    public string Kind { get; set; } = "";       // "command" | "alarm-raised" | "alarm-cleared"
    public string Description { get; set; } = "";
}
