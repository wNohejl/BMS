using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;
using EdgeMonitor.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EdgeMonitor.Api.Tests;

/// <summary>Records broadcasts instead of pushing to SignalR, so tests can assert on them.</summary>
public class FakeBroadcaster : ITelemetryBroadcaster
{
    public List<ReadingEventDto> Readings { get; } = new();
    public List<AlertEventDto> Alerts { get; } = new();
    public List<ZoneStateEventDto> ZoneStates { get; } = new();

    public Task ReadingReceivedAsync(ReadingEventDto reading, CancellationToken ct = default)
    {
        Readings.Add(reading);
        return Task.CompletedTask;
    }

    public Task AlertTriggeredAsync(AlertEventDto alert, CancellationToken ct = default)
    {
        Alerts.Add(alert);
        return Task.CompletedTask;
    }

    public Task ZoneStateChangedAsync(ZoneStateEventDto zone, CancellationToken ct = default)
    {
        ZoneStates.Add(zone);
        return Task.CompletedTask;
    }

    public List<List<ActiveAlarmDto>> AlarmBroadcasts { get; } = new();

    public Task ActiveAlarmsChangedAsync(List<ActiveAlarmDto> alarms, CancellationToken ct = default)
    {
        AlarmBroadcasts.Add(alarms);
        return Task.CompletedTask;
    }
}

/// <summary>Records commands instead of writing files / sending C2D messages.</summary>
public class FakeCommandChannel : ICommandChannel
{
    public List<(string Type, string ZoneId, double Value, string? Fault)> Sent { get; } = new();

    public Task SendAsync(string type, string zoneId, double value, string? fault = null,
        CancellationToken ct = default)
    {
        Sent.Add((type, zoneId, value, fault));
        return Task.CompletedTask;
    }
}

public static class TestHelpers
{
    public static EdgeMonitorDbContext NewDb() =>
        new(new DbContextOptionsBuilder<EdgeMonitorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public static (TelemetryService Service, FakeBroadcaster Broadcaster) NewTelemetryService(
        EdgeMonitorDbContext db)
    {
        var broadcaster = new FakeBroadcaster();
        var service = new TelemetryService(db, new AlertEvaluationService(db), broadcaster,
            NullLogger<TelemetryService>.Instance);
        return (service, broadcaster);
    }

    public static TelemetryBatchDto Batch(string tenantId = "tenant-demo",
        params SensorReadingDto[] readings) => new()
    {
        TenantId = tenantId,
        DeviceId = "edge-device-01",
        Readings = readings.ToList(),
    };

    public static SensorReadingDto Reading(string sensorType = "temperature", double value = 72.0,
        string zoneId = "zone-1", string unit = "F") => new()
    {
        SensorType = sensorType,
        Value = value,
        Unit = unit,
        ZoneId = zoneId,
        DeviceId = "edge-device-01",
        TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };
}
