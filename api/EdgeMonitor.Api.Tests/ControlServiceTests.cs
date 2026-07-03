using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;
using EdgeMonitor.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EdgeMonitor.Api.Tests;

public class ControlServiceTests
{
    private static (ControlService Service, FakeCommandChannel Channel, FakeBroadcaster Broadcaster)
        NewControlService(EdgeMonitorDbContext db)
    {
        var channel = new FakeCommandChannel();
        var broadcaster = new FakeBroadcaster();
        var service = new ControlService(db, channel, broadcaster,
            NullLogger<ControlService>.Instance);
        return (service, channel, broadcaster);
    }

    [Fact]
    public async Task SetSetpoint_PersistsConfig_Audits_AndSendsCommand()
    {
        using var db = TestHelpers.NewDb();
        var (service, channel, _) = NewControlService(db);

        var control = await service.SetSetpointAsync("tenant-demo", "zone-1", 74.5);

        Assert.Equal("manual", control.Mode);
        Assert.Equal(74.5, control.ManualSetpointF);
        Assert.Single(db.ZoneControls);
        var audit = Assert.Single(db.DeviceCommands);
        Assert.Equal("setSetpoint", audit.Type);
        Assert.Equal(74.5, audit.Value);
        Assert.Equal(("setSetpoint", "zone-1", 74.5, null), Assert.Single(channel.Sent));
    }

    [Fact]
    public async Task SetSetpoint_Twice_UpsertsInsteadOfDuplicating()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, _) = NewControlService(db);

        await service.SetSetpointAsync("tenant-demo", "zone-1", 74.0);
        await service.SetSetpointAsync("tenant-demo", "zone-1", 71.0);

        var control = Assert.Single(db.ZoneControls);
        Assert.Equal(71.0, control.ManualSetpointF);
        Assert.Equal(2, db.DeviceCommands.Count()); // but every command is audited
    }

    [Fact]
    public async Task ClearSetpoint_ReturnsZoneToSchedule()
    {
        using var db = TestHelpers.NewDb();
        var (service, channel, _) = NewControlService(db);

        await service.SetSetpointAsync("tenant-demo", "zone-1", 74.0);
        var control = await service.ClearSetpointAsync("tenant-demo", "zone-1");

        Assert.Equal("schedule", control.Mode);
        Assert.Null(control.ManualSetpointF);
        Assert.Equal("clearSetpoint", channel.Sent.Last().Type);
    }

    [Fact]
    public async Task ProcessStatus_UpsertsZoneStatus_AndBroadcasts()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, broadcaster) = NewControlService(db);

        var batch = new ZoneStatusBatchDto
        {
            TenantId = "tenant-demo",
            DeviceId = "edge-device-01",
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Zones =
            {
                new ZoneStatusDto { ZoneId = "zone-1", State = "cooling", SetpointF = 72, TempF = 75.2, Occupied = true },
                new ZoneStatusDto { ZoneId = "zone-2", State = "idle", SetpointF = 72, TempF = 71.8, Occupied = true },
            },
        };

        await service.ProcessStatusAsync(batch);
        // Second snapshot updates in place rather than growing the table.
        batch.Zones[0].State = "idle";
        await service.ProcessStatusAsync(batch);

        Assert.Equal(2, db.ZoneStatuses.Count());
        Assert.Equal("idle", db.ZoneStatuses.Single(s => s.ZoneId == "zone-1").HvacState);
        Assert.Equal(4, broadcaster.ZoneStates.Count);
        Assert.Contains(broadcaster.ZoneStates, z => z.ZoneId == "zone-1" && z.State == "cooling");
    }

    [Fact]
    public async Task GetZones_CombinesReportedStateWithDesiredConfig()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, _) = NewControlService(db);

        await service.SetSetpointAsync("tenant-demo", "zone-1", 74.0);
        await service.ProcessStatusAsync(new ZoneStatusBatchDto
        {
            TenantId = "tenant-demo",
            Zones = { new ZoneStatusDto { ZoneId = "zone-1", State = "cooling", SetpointF = 74, TempF = 76, Occupied = true } },
        });

        var zones = await service.GetZonesAsync("tenant-demo");

        var zone = Assert.Single(zones);
        Assert.Equal("cooling", zone.State);
        Assert.Equal("manual", zone.Mode);
        Assert.Equal(74.0, zone.ManualSetpointF);
    }

    [Fact]
    public async Task GetZones_FiltersByTenant()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, _) = NewControlService(db);

        await service.ProcessStatusAsync(new ZoneStatusBatchDto
        {
            TenantId = "tenant-other",
            Zones = { new ZoneStatusDto { ZoneId = "zone-1", State = "cooling" } },
        });

        Assert.Empty(await service.GetZonesAsync("tenant-demo"));
    }
}
