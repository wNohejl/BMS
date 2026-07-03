namespace EdgeMonitor.Api.Models;

/// <summary>Payload shape emitted by the C++ edge agent (one MQTT message / JSON file per batch).</summary>
public class TelemetryBatchDto
{
    public string TenantId { get; set; } = "tenant-demo";
    public string DeviceId { get; set; } = "";
    public List<SensorReadingDto> Readings { get; set; } = new();
}

public class SensorReadingDto
{
    public string SensorType { get; set; } = "";
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public long TimestampMs { get; set; }
}

/// <summary>SignalR "ReadingReceived" event and REST response shape.</summary>
public class ReadingEventDto
{
    public string SensorType { get; set; } = "";
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public string ZoneId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

/// <summary>SignalR "AlertTriggered" event. The dashboard shows PlainEnglishMessage
/// directly to building owners — no technical jargon.</summary>
public class AlertEventDto
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

public class CreateAlertRequest
{
    public string TenantId { get; set; } = "tenant-demo";
    public string ZoneId { get; set; } = "";
    public string SensorType { get; set; } = "";
    public string Condition { get; set; } = ""; // "above" | "below"
    public double ThresholdValue { get; set; }
}
