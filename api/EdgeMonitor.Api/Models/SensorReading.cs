namespace EdgeMonitor.Api.Models;

public class SensorReading
{
    public int Id { get; set; }

    // Multi-tenancy — required from day one. Adding it after the first customer
    // means a migration on live data; adding it now costs one column.
    public string TenantId { get; set; } = "";

    public string DeviceId { get; set; } = "";
    public string SensorType { get; set; } = ""; // "temperature" | "power" | "occupancy"
    public double Value { get; set; }
    public string Unit { get; set; } = "";
    public DateTime TimestampUtc { get; set; }
    public string ZoneId { get; set; } = "";
}
