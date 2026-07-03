namespace EdgeMonitor.Api.Models;

public class Alert
{
    public int Id { get; set; }
    public string TenantId { get; set; } = ""; // Multi-tenancy
    public string ZoneId { get; set; } = "";
    public string SensorType { get; set; } = "";
    public double ThresholdValue { get; set; }
    public string Condition { get; set; } = ""; // "above" | "below"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
}
