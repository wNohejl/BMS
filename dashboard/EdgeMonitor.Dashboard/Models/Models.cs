namespace EdgeMonitor.Dashboard.Models;

/// <summary>Matches the API's ReadingEventDto — used for both REST history and SignalR events.</summary>
public class ReadingEvent
{
    public string SensorType { get; set; } = "";
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class AlertEvent
{
    public int AlertId { get; set; }
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string SensorType { get; set; } = "";
    public string Condition { get; set; } = "";
    public double ThresholdValue { get; set; }
    public double CurrentValue { get; set; }
    public string PlainEnglishMessage { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

/// <summary>Matches the API's ZoneStateEventDto — reported engine state + desired config.</summary>
public class ZoneState
{
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Floor { get; set; }
    public string State { get; set; } = "";
    public double SetpointF { get; set; }
    public double TempF { get; set; }        // model ground truth
    public double SensorTempF { get; set; }  // what the sensor claims — diverges under drift
    public bool Occupied { get; set; }
    public List<string> Faults { get; set; } = new();
    public string Mode { get; set; } = "schedule";
    public double? ManualSetpointF { get; set; }
    public DateTime Timestamp { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? PlainEnglish.Zone(ZoneId) : Name;
}

/// <summary>Matches the API's TimelineEventDto — one entry of the incident timeline.</summary>
public class TimelineEvent
{
    public DateTime Timestamp { get; set; }
    public string ZoneId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Matches the API's ScenarioInfo — a scripted incident scenario.</summary>
public class ScenarioInfo
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Matches the API's ActiveAlarmDto — a live alarm from the twin.</summary>
public class ActiveAlarm
{
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime RaisedUtc { get; set; }
}

public class AlertRule
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string SensorType { get; set; } = "";
    public double ThresholdValue { get; set; }
    public string Condition { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class CreateAlertRequest
{
    public string TenantId { get; set; } = "tenant-demo";
    public string ZoneId { get; set; } = "zone-1";
    public string SensorType { get; set; } = "temperature";
    public string Condition { get; set; } = "above";
    public double ThresholdValue { get; set; } = 78;
}

/// <summary>Plain-English display helpers — every label a building owner sees goes through here.</summary>
public static class PlainEnglish
{
    public static string Zone(string zoneId) => zoneId switch
    {
        "building" => "Whole building",
        _ when zoneId.StartsWith("zone-") => "Zone " + zoneId["zone-".Length..],
        _ => zoneId,
    };

    public static string Metric(string sensorType) => sensorType switch
    {
        "temperature" => "temperature",
        "power" => "power use",
        "occupancy" => "people inside",
        _ => sensorType,
    };

    public static string Value(ReadingEvent r) => r.SensorType switch
    {
        "temperature" => $"{r.Value:0}°F",
        "power" => $"{r.Value:0.0} kW",
        "occupancy" => $"{r.Value:0} people",
        _ => $"{r.Value:0.#} {r.Unit}",
    };
}
