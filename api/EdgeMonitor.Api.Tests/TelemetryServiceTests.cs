using EdgeMonitor.Api.Models;
using Xunit;

namespace EdgeMonitor.Api.Tests;

public class TelemetryServiceTests
{
    [Fact]
    public async Task ProcessBatchAsync_PersistsReadingsWithTenantId()
    {
        using var db = TestHelpers.NewDb();
        var (service, _) = TestHelpers.NewTelemetryService(db);

        var count = await service.ProcessBatchAsync(TestHelpers.Batch("tenant-42",
            TestHelpers.Reading("temperature", 72.5, "zone-1"),
            TestHelpers.Reading("power", 12.3, "zone-1", "kW")));

        Assert.Equal(2, count);
        Assert.Equal(2, db.SensorReadings.Count());
        Assert.All(db.SensorReadings, r => Assert.Equal("tenant-42", r.TenantId));
    }

    [Fact]
    public async Task ProcessBatchAsync_BroadcastsOneEventPerReading()
    {
        using var db = TestHelpers.NewDb();
        var (service, broadcaster) = TestHelpers.NewTelemetryService(db);

        await service.ProcessBatchAsync(TestHelpers.Batch("tenant-demo",
            TestHelpers.Reading("temperature", 71.0, "zone-1"),
            TestHelpers.Reading("temperature", 74.0, "zone-2"),
            TestHelpers.Reading("occupancy", 18, "building", "people")));

        Assert.Equal(3, broadcaster.Readings.Count);
        Assert.Contains(broadcaster.Readings, r => r.ZoneId == "zone-2" && r.Value == 74.0);
    }

    [Fact]
    public async Task ProcessBatchAsync_TriggersAlert_WhenThresholdBreached()
    {
        using var db = TestHelpers.NewDb();
        db.Alerts.Add(new Alert
        {
            TenantId = "tenant-demo",
            ZoneId = "zone-2",
            SensorType = "temperature",
            Condition = "above",
            ThresholdValue = 78.0,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var (service, broadcaster) = TestHelpers.NewTelemetryService(db);
        await service.ProcessBatchAsync(TestHelpers.Batch("tenant-demo",
            TestHelpers.Reading("temperature", 79.4, "zone-2")));

        var alertEvent = Assert.Single(broadcaster.Alerts);
        Assert.Equal(79.4, alertEvent.CurrentValue);
        Assert.False(string.IsNullOrWhiteSpace(alertEvent.PlainEnglishMessage));
    }

    [Fact]
    public async Task ProcessBatchAsync_EmptyBatch_IsANoOp()
    {
        using var db = TestHelpers.NewDb();
        var (service, broadcaster) = TestHelpers.NewTelemetryService(db);

        var count = await service.ProcessBatchAsync(TestHelpers.Batch());

        Assert.Equal(0, count);
        Assert.Empty(db.SensorReadings);
        Assert.Empty(broadcaster.Readings);
    }
}
