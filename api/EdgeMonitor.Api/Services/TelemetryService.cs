using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;

namespace EdgeMonitor.Api.Services;

/// <summary>
/// Shared processing pipeline: persist readings (with TenantId), evaluate alert
/// thresholds, broadcast real-time events. Both listener services feed this,
/// so local dev and production behave identically past the ingestion edge.
/// </summary>
public class TelemetryService
{
    private readonly EdgeMonitorDbContext _db;
    private readonly AlertEvaluationService _alerts;
    private readonly ITelemetryBroadcaster _broadcaster;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(EdgeMonitorDbContext db, AlertEvaluationService alerts,
        ITelemetryBroadcaster broadcaster, ILogger<TelemetryService> logger)
    {
        _db = db;
        _alerts = alerts;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<int> ProcessBatchAsync(TelemetryBatchDto batch, CancellationToken ct = default)
    {
        if (batch.Readings.Count == 0)
        {
            return 0;
        }

        var entities = batch.Readings.Select(r => new SensorReading
        {
            TenantId = batch.TenantId,
            DeviceId = string.IsNullOrEmpty(r.DeviceId) ? batch.DeviceId : r.DeviceId,
            SensorType = r.SensorType,
            Value = r.Value,
            Unit = r.Unit,
            ZoneId = r.ZoneId,
            TimestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(r.TimestampMs).UtcDateTime,
        }).ToList();

        _db.SensorReadings.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        foreach (var reading in entities)
        {
            await _broadcaster.ReadingReceivedAsync(new ReadingEventDto
            {
                SensorType = reading.SensorType,
                Value = reading.Value,
                Unit = reading.Unit,
                ZoneId = reading.ZoneId,
                TenantId = reading.TenantId,
                DeviceId = reading.DeviceId,
                Timestamp = reading.TimestampUtc,
            }, ct);
        }

        var triggered = await _alerts.EvaluateBatchAsync(batch, ct);
        foreach (var alertEvent in triggered)
        {
            _logger.LogInformation("Alert triggered: {Message}", alertEvent.PlainEnglishMessage);
            await _broadcaster.AlertTriggeredAsync(alertEvent, ct);
        }

        return entities.Count;
    }
}
