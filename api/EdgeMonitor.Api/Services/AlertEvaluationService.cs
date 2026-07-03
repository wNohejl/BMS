using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EdgeMonitor.Api.Services;

/// <summary>
/// Evaluates incoming readings against the tenant's active alert rules and
/// produces AlertTriggered events with a plainEnglishMessage a building owner
/// can read without explanation.
/// </summary>
public class AlertEvaluationService
{
    private readonly EdgeMonitorDbContext _db;

    public AlertEvaluationService(EdgeMonitorDbContext db)
    {
        _db = db;
    }

    public async Task<List<AlertEventDto>> EvaluateBatchAsync(TelemetryBatchDto batch,
        CancellationToken ct = default)
    {
        var activeAlerts = await _db.Alerts
            .Where(a => a.TenantId == batch.TenantId && a.IsActive)
            .ToListAsync(ct);

        var events = new List<AlertEventDto>();
        if (activeAlerts.Count == 0)
        {
            return events;
        }

        foreach (var reading in batch.Readings)
        {
            foreach (var alert in activeAlerts.Where(a =>
                         a.ZoneId == reading.ZoneId && a.SensorType == reading.SensorType))
            {
                if (!IsBreached(alert, reading.Value))
                {
                    continue;
                }

                events.Add(new AlertEventDto
                {
                    AlertId = alert.Id,
                    TenantId = alert.TenantId,
                    ZoneId = alert.ZoneId,
                    SensorType = alert.SensorType,
                    Condition = alert.Condition,
                    ThresholdValue = alert.ThresholdValue,
                    CurrentValue = reading.Value,
                    PlainEnglishMessage = BuildPlainEnglishMessage(alert, reading.Value),
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reading.TimestampMs).UtcDateTime,
                });
            }
        }

        return events;
    }

    public static bool IsBreached(Alert alert, double value) =>
        alert.Condition == "above" ? value > alert.ThresholdValue : value < alert.ThresholdValue;

    // "Zone 2 temperature is 79.4°F — above your alert level of 78°F."
    // Never "ThresholdCondition: above, CurrentValue: 79.4".
    public static string BuildPlainEnglishMessage(Alert alert, double currentValue)
    {
        var zone = HumanizeZone(alert.ZoneId);
        var (metric, unit) = alert.SensorType switch
        {
            "temperature" => ("temperature", "°F"),
            "power" => ("power use", " kW"),
            "occupancy" => ("occupancy", " people"),
            _ => (alert.SensorType, ""),
        };
        var direction = alert.Condition == "above" ? "above" : "below";
        return $"{zone} {metric} is {currentValue:0.#}{unit} — {direction} your alert level of {alert.ThresholdValue:0.#}{unit}.";
    }

    public static string HumanizeZone(string zoneId)
    {
        if (string.Equals(zoneId, "building", StringComparison.OrdinalIgnoreCase))
        {
            return "The building";
        }

        var parts = zoneId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0].Equals("zone", StringComparison.OrdinalIgnoreCase))
        {
            return $"Zone {parts[1]}";
        }

        return zoneId;
    }
}
