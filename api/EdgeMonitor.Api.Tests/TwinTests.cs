using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;
using EdgeMonitor.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EdgeMonitor.Api.Tests;

public class TwinTests
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

    private static ZoneStatusBatchDto StatusBatch(params AlarmDto[] alarms) => new()
    {
        TenantId = "tenant-demo",
        DeviceId = "edge-device-01",
        TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Zones =
        {
            new ZoneStatusDto
            {
                ZoneId = "zone-1", Name = "Lobby", Floor = 1, State = "cooling",
                SetpointF = 72, TempF = 75.0, Occupied = true,
                Faults = { "damperStuck" },
            },
        },
        Alarms = alarms.ToList(),
    };

    [Fact]
    public async Task InjectFault_SendsCommandAndAudits()
    {
        using var db = TestHelpers.NewDb();
        var (service, channel, _) = NewControlService(db);

        await service.InjectFaultAsync("tenant-demo", "zone-1", "damperStuck");

        Assert.Equal(("injectFault", "zone-1", 0, "damperStuck"), Assert.Single(channel.Sent));
        var audit = Assert.Single(db.DeviceCommands);
        Assert.Equal("injectFault:damperStuck", audit.Type);
    }

    [Fact]
    public async Task ProcessStatus_PersistsModelMetadataAndFaults()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, _) = NewControlService(db);

        await service.ProcessStatusAsync(StatusBatch());

        var status = Assert.Single(db.ZoneStatuses);
        Assert.Equal("Lobby", status.Name);
        Assert.Equal(1, status.Floor);
        Assert.Equal("damperStuck", status.FaultsCsv);
    }

    [Fact]
    public async Task AlarmLifecycle_RaisePersistBroadcastClear()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, broadcaster) = NewControlService(db);

        var alarm = new AlarmDto
        {
            ZoneId = "zone-1",
            Type = "ineffective-equipment",
            Severity = "warning",
            Message = "Lobby cooling has been running but the temperature isn't responding.",
            SinceMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        // Raise: alarm persisted active + broadcast fired.
        await service.ProcessStatusAsync(StatusBatch(alarm));
        var row = Assert.Single(db.TwinAlarms);
        Assert.True(row.IsActive);
        var broadcast = Assert.Single(broadcaster.AlarmBroadcasts);
        Assert.Single(broadcast);

        // Steady state: same alarm reported again — no new rows, no re-broadcast.
        await service.ProcessStatusAsync(StatusBatch(alarm));
        Assert.Single(db.TwinAlarms);
        Assert.Single(broadcaster.AlarmBroadcasts);

        // Recovery: alarm no longer reported — row kept as history, broadcast empty list.
        await service.ProcessStatusAsync(StatusBatch());
        row = Assert.Single(db.TwinAlarms);
        Assert.False(row.IsActive);
        Assert.NotNull(row.ClearedUtc);
        Assert.Equal(2, broadcaster.AlarmBroadcasts.Count);
        Assert.Empty(broadcaster.AlarmBroadcasts.Last());
    }

    [Fact]
    public async Task GetActiveAlarms_FiltersByTenantAndActive()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, _) = NewControlService(db);

        db.TwinAlarms.Add(new TwinAlarm
        {
            TenantId = "tenant-demo", ZoneId = "zone-1", Type = "out-of-range",
            Severity = "warning", Message = "Lobby is 5°F above its target.",
            RaisedUtc = DateTime.UtcNow, IsActive = true,
        });
        db.TwinAlarms.Add(new TwinAlarm
        {
            TenantId = "tenant-demo", ZoneId = "zone-1", Type = "sensor-offline",
            Severity = "critical", Message = "cleared long ago",
            RaisedUtc = DateTime.UtcNow.AddHours(-2), IsActive = false,
        });
        db.TwinAlarms.Add(new TwinAlarm
        {
            TenantId = "tenant-other", ZoneId = "zone-9", Type = "out-of-range",
            Severity = "warning", Message = "other tenant",
            RaisedUtc = DateTime.UtcNow, IsActive = true,
        });
        await db.SaveChangesAsync();

        var alarms = await service.GetActiveAlarmsAsync("tenant-demo");

        var active = Assert.Single(alarms);
        Assert.Equal("out-of-range", active.Type);
    }

    [Fact]
    public async Task Timeline_MergesCommandsAndAlarmLifecycle()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, _) = NewControlService(db);

        await service.InjectFaultAsync("tenant-demo", "zone-1", "damperStuck");
        var alarm = new AlarmDto
        {
            ZoneId = "zone-1", Type = "ineffective-equipment", Severity = "warning",
            Message = "Lobby cooling isn't responding.",
            SinceMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        await service.ProcessStatusAsync(StatusBatch(alarm)); // raise
        await service.ProcessStatusAsync(StatusBatch());      // clear

        var timeline = await service.GetTimelineAsync("tenant-demo");

        Assert.Equal(3, timeline.Count);
        Assert.Contains(timeline, e => e.Kind == "command" && e.Description.Contains("damperStuck"));
        Assert.Contains(timeline, e => e.Kind == "alarm-raised");
        Assert.Contains(timeline, e => e.Kind == "alarm-cleared");
        // newest first
        Assert.True(timeline.First().Timestamp >= timeline.Last().Timestamp);
    }

    [Fact]
    public void ScenarioCatalog_IsSaneAndOrdered()
    {
        var scenarios = ScenarioService.List();

        Assert.True(scenarios.Count >= 3);
        Assert.Contains(scenarios, s => s.Name == "afternoon-meltdown");
        Assert.All(scenarios, s => Assert.True(ScenarioService.Exists(s.Name)));
    }

    [Fact]
    public void BuildReplayPlan_DecodesAuditRowsInOrder()
    {
        var t0 = DateTime.UtcNow;
        var commands = new[]
        {
            new DeviceCommand { ZoneId = "zone-2", Type = "injectFault:damperStuck", SentUtc = t0.AddSeconds(5) },
            new DeviceCommand { ZoneId = "zone-1", Type = "setSetpoint", Value = 74.5, SentUtc = t0 },
            new DeviceCommand { ZoneId = "zone-2", Type = "clearFault:damperStuck", SentUtc = t0.AddSeconds(30) },
            new DeviceCommand { ZoneId = "zone-1", Type = "clearSetpoint", SentUtc = t0.AddSeconds(40) },
        };

        var plan = ScenarioService.BuildReplayPlan(commands);

        Assert.Equal(4, plan.Count);
        // chronological, 2s apart regardless of original spacing
        Assert.Equal(("setSetpoint", "zone-1", 74.5, (string?)null, 0),
            (plan[0].Type, plan[0].ZoneId, plan[0].Value, plan[0].Fault, plan[0].AtSeconds));
        Assert.Equal(("injectFault", "zone-2", "damperStuck", 2),
            (plan[1].Type, plan[1].ZoneId, plan[1].Fault, plan[1].AtSeconds));
        Assert.Equal("clearFault", plan[2].Type);
        Assert.Equal("clearSetpoint", plan[3].Type);
    }

    [Fact]
    public async Task GetZones_ExposesFaultsList()
    {
        using var db = TestHelpers.NewDb();
        var (service, _, _) = NewControlService(db);

        await service.ProcessStatusAsync(StatusBatch());
        var zones = await service.GetZonesAsync("tenant-demo");

        var zone = Assert.Single(zones);
        Assert.Equal("Lobby", zone.Name);
        Assert.Equal(new[] { "damperStuck" }, zone.Faults);
    }
}
