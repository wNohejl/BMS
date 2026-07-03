using EdgeMonitor.Api.Models;
using EdgeMonitor.Api.Services;
using Xunit;

namespace EdgeMonitor.Api.Tests;

public class AlertEvaluationTests
{
    private static Alert NewAlert(string condition = "above", double threshold = 78.0,
        string tenantId = "tenant-demo", string zoneId = "zone-2",
        string sensorType = "temperature", bool isActive = true) => new()
    {
        TenantId = tenantId,
        ZoneId = zoneId,
        SensorType = sensorType,
        Condition = condition,
        ThresholdValue = threshold,
        IsActive = isActive,
        CreatedUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task AboveCondition_Triggers_WhenValueExceedsThreshold()
    {
        using var db = TestHelpers.NewDb();
        db.Alerts.Add(NewAlert("above", 78.0));
        await db.SaveChangesAsync();

        var service = new AlertEvaluationService(db);
        var events = await service.EvaluateBatchAsync(TestHelpers.Batch("tenant-demo",
            TestHelpers.Reading("temperature", 79.4, "zone-2")));

        var evt = Assert.Single(events);
        Assert.Equal("above", evt.Condition);
        Assert.Equal(79.4, evt.CurrentValue);
    }

    [Fact]
    public async Task BelowCondition_Triggers_WhenValueUnderThreshold()
    {
        using var db = TestHelpers.NewDb();
        db.Alerts.Add(NewAlert("below", 65.0));
        await db.SaveChangesAsync();

        var service = new AlertEvaluationService(db);
        var events = await service.EvaluateBatchAsync(TestHelpers.Batch("tenant-demo",
            TestHelpers.Reading("temperature", 62.0, "zone-2")));

        Assert.Single(events);
    }

    [Fact]
    public async Task DoesNotTrigger_WhenValueWithinThreshold()
    {
        using var db = TestHelpers.NewDb();
        db.Alerts.Add(NewAlert("above", 78.0));
        await db.SaveChangesAsync();

        var service = new AlertEvaluationService(db);
        var events = await service.EvaluateBatchAsync(TestHelpers.Batch("tenant-demo",
            TestHelpers.Reading("temperature", 74.0, "zone-2")));

        Assert.Empty(events);
    }

    [Fact]
    public async Task InactiveAlerts_AreIgnored()
    {
        using var db = TestHelpers.NewDb();
        db.Alerts.Add(NewAlert("above", 78.0, isActive: false));
        await db.SaveChangesAsync();

        var service = new AlertEvaluationService(db);
        var events = await service.EvaluateBatchAsync(TestHelpers.Batch("tenant-demo",
            TestHelpers.Reading("temperature", 90.0, "zone-2")));

        Assert.Empty(events);
    }

    [Fact]
    public async Task OtherTenantsAlerts_AreIgnored()
    {
        using var db = TestHelpers.NewDb();
        db.Alerts.Add(NewAlert("above", 78.0, tenantId: "tenant-other"));
        await db.SaveChangesAsync();

        var service = new AlertEvaluationService(db);
        var events = await service.EvaluateBatchAsync(TestHelpers.Batch("tenant-demo",
            TestHelpers.Reading("temperature", 90.0, "zone-2")));

        Assert.Empty(events);
    }

    [Fact]
    public void PlainEnglishMessage_IsReadableByABuildingOwner()
    {
        var message = AlertEvaluationService.BuildPlainEnglishMessage(
            NewAlert("above", 78.0, zoneId: "zone-2", sensorType: "temperature"), 79.4);

        Assert.Equal("Zone 2 temperature is 79.4°F — above your alert level of 78°F.", message);
        Assert.DoesNotContain("ThresholdCondition", message);
        Assert.DoesNotContain("ZoneId", message);
    }

    [Theory]
    [InlineData("zone-1", "Zone 1")]
    [InlineData("zone-2", "Zone 2")]
    [InlineData("building", "The building")]
    public void HumanizeZone_ProducesPlainEnglish(string zoneId, string expected)
    {
        Assert.Equal(expected, AlertEvaluationService.HumanizeZone(zoneId));
    }
}
