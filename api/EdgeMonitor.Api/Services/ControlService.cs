using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EdgeMonitor.Api.Services;

/// <summary>
/// Orchestration for the control flow and digital twin: persists desired config
/// (ZoneControl), audits every command (DeviceCommand), pushes commands and
/// fault injections to the engine over ICommandChannel, and turns engine status
/// snapshots into persisted state (ZoneStatus + TwinAlarm history) plus live
/// ZoneStateChanged / AlarmsChanged broadcasts.
/// </summary>
public class ControlService
{
    private readonly EdgeMonitorDbContext _db;
    private readonly ICommandChannel _commands;
    private readonly ITelemetryBroadcaster _broadcaster;
    private readonly ILogger<ControlService> _logger;

    public ControlService(EdgeMonitorDbContext db, ICommandChannel commands,
        ITelemetryBroadcaster broadcaster, ILogger<ControlService> logger)
    {
        _db = db;
        _commands = commands;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<ZoneControl> SetSetpointAsync(string tenantId, string zoneId, double value,
        CancellationToken ct = default)
    {
        var control = await UpsertControlAsync(tenantId, zoneId, ct);
        control.Mode = "manual";
        control.ManualSetpointF = value;
        control.UpdatedUtc = DateTime.UtcNow;

        AddAudit(tenantId, zoneId, "setSetpoint", value);
        await _db.SaveChangesAsync(ct);
        await _commands.SendAsync("setSetpoint", zoneId, value, null, ct);
        return control;
    }

    public async Task<ZoneControl> ClearSetpointAsync(string tenantId, string zoneId,
        CancellationToken ct = default)
    {
        var control = await UpsertControlAsync(tenantId, zoneId, ct);
        control.Mode = "schedule";
        control.ManualSetpointF = null;
        control.UpdatedUtc = DateTime.UtcNow;

        AddAudit(tenantId, zoneId, "clearSetpoint", 0);
        await _db.SaveChangesAsync(ct);
        await _commands.SendAsync("clearSetpoint", zoneId, 0, null, ct);
        return control;
    }

    /// <summary>Digital-twin fault injection: Dashboard → API → engine's device layer.</summary>
    public async Task InjectFaultAsync(string tenantId, string zoneId, string fault,
        CancellationToken ct = default)
    {
        AddAudit(tenantId, zoneId, $"injectFault:{fault}", 1);
        await _db.SaveChangesAsync(ct);
        await _commands.SendAsync("injectFault", zoneId, 0, fault, ct);
    }

    public async Task ClearFaultAsync(string tenantId, string zoneId, string fault,
        CancellationToken ct = default)
    {
        AddAudit(tenantId, zoneId, $"clearFault:{fault}", 0);
        await _db.SaveChangesAsync(ct);
        await _commands.SendAsync("clearFault", zoneId, 0, fault, ct);
    }

    /// <summary>Engine → orchestrator: persist reported state + alarm lifecycle, broadcast live.</summary>
    public async Task ProcessStatusAsync(ZoneStatusBatchDto batch, CancellationToken ct = default)
    {
        var reportedUtc = batch.TimestampMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(batch.TimestampMs).UtcDateTime
            : DateTime.UtcNow;

        foreach (var zone in batch.Zones)
        {
            var status = await _db.ZoneStatuses.FirstOrDefaultAsync(
                s => s.TenantId == batch.TenantId && s.ZoneId == zone.ZoneId, ct);
            if (status is null)
            {
                status = new ZoneStatus { TenantId = batch.TenantId, ZoneId = zone.ZoneId };
                _db.ZoneStatuses.Add(status);
            }

            status.Name = zone.Name;
            status.Floor = zone.Floor;
            status.HvacState = zone.State;
            status.SetpointF = zone.SetpointF;
            status.TempF = zone.TempF;
            status.SensorTempF = zone.SensorTempF;
            status.Occupied = zone.Occupied;
            status.FaultsCsv = string.Join(',', zone.Faults);
            status.ReportedUtc = reportedUtc;
        }

        var alarmsChanged = await ReconcileAlarmsAsync(batch, reportedUtc, ct);
        await _db.SaveChangesAsync(ct);

        foreach (var zone in batch.Zones)
        {
            await _broadcaster.ZoneStateChangedAsync(new ZoneStateEventDto
            {
                TenantId = batch.TenantId,
                ZoneId = zone.ZoneId,
                Name = zone.Name,
                Floor = zone.Floor,
                State = zone.State,
                SetpointF = zone.SetpointF,
                TempF = zone.TempF,
                SensorTempF = zone.SensorTempF,
                Occupied = zone.Occupied,
                Faults = zone.Faults,
                Timestamp = reportedUtc,
            }, ct);
        }

        if (alarmsChanged)
        {
            await _broadcaster.ActiveAlarmsChangedAsync(
                await GetActiveAlarmsAsync(batch.TenantId, ct), ct);
        }
    }

    /// <summary>Raise new alarms, clear ones no longer reported. Rows are kept
    /// after clearing (IsActive=false) as incident history.</summary>
    private async Task<bool> ReconcileAlarmsAsync(ZoneStatusBatchDto batch, DateTime reportedUtc,
        CancellationToken ct)
    {
        var activeRows = await _db.TwinAlarms
            .Where(a => a.TenantId == batch.TenantId && a.IsActive)
            .ToListAsync(ct);
        var changed = false;

        foreach (var alarm in batch.Alarms)
        {
            if (activeRows.Any(r => r.ZoneId == alarm.ZoneId && r.Type == alarm.Type))
            {
                continue;
            }
            _db.TwinAlarms.Add(new TwinAlarm
            {
                TenantId = batch.TenantId,
                ZoneId = alarm.ZoneId,
                Type = alarm.Type,
                Severity = alarm.Severity,
                Message = alarm.Message,
                RaisedUtc = alarm.SinceMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(alarm.SinceMs).UtcDateTime
                    : reportedUtc,
                IsActive = true,
            });
            changed = true;
            _logger.LogWarning("Twin alarm raised: {Message}", alarm.Message);
        }

        foreach (var row in activeRows)
        {
            if (batch.Alarms.Any(a => a.ZoneId == row.ZoneId && a.Type == row.Type))
            {
                continue;
            }
            row.IsActive = false;
            row.ClearedUtc = reportedUtc;
            changed = true;
            _logger.LogInformation("Twin alarm cleared: {Type} on {ZoneId}", row.Type, row.ZoneId);
        }

        return changed;
    }

    public async Task<List<ActiveAlarmDto>> GetActiveAlarmsAsync(string tenantId,
        CancellationToken ct = default)
    {
        return await _db.TwinAlarms.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderByDescending(a => a.RaisedUtc)
            .Select(a => new ActiveAlarmDto
            {
                TenantId = a.TenantId,
                ZoneId = a.ZoneId,
                Type = a.Type,
                Severity = a.Severity,
                Message = a.Message,
                RaisedUtc = a.RaisedUtc,
            })
            .ToListAsync(ct);
    }

    /// <summary>Combined view for the dashboard: reported state + desired config.</summary>
    public async Task<List<ZoneStateEventDto>> GetZonesAsync(string tenantId,
        CancellationToken ct = default)
    {
        var statuses = await _db.ZoneStatuses.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Floor).ThenBy(s => s.ZoneId)
            .ToListAsync(ct);
        var controls = await _db.ZoneControls.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .ToDictionaryAsync(c => c.ZoneId, ct);

        return statuses.Select(s => new ZoneStateEventDto
        {
            TenantId = s.TenantId,
            ZoneId = s.ZoneId,
            Name = s.Name,
            Floor = s.Floor,
            State = s.HvacState,
            SetpointF = s.SetpointF,
            TempF = s.TempF,
            SensorTempF = s.SensorTempF,
            Occupied = s.Occupied,
            Faults = s.FaultsCsv.Length > 0 ? s.FaultsCsv.Split(',').ToList() : new List<string>(),
            Mode = controls.TryGetValue(s.ZoneId, out var c) ? c.Mode : "schedule",
            ManualSetpointF = controls.TryGetValue(s.ZoneId, out var c2) ? c2.ManualSetpointF : null,
            Timestamp = s.ReportedUtc,
        }).ToList();
    }

    /// <summary>Incident timeline: every command sent plus every alarm raised or
    /// cleared, merged and sorted newest first — the twin's flight recorder.</summary>
    public async Task<List<TimelineEventDto>> GetTimelineAsync(string tenantId, int limit = 50,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var commands = await _db.DeviceCommands.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.SentUtc)
            .Take(limit)
            .ToListAsync(ct);
        var alarms = await _db.TwinAlarms.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.RaisedUtc)
            .Take(limit)
            .ToListAsync(ct);

        var events = new List<TimelineEventDto>();
        events.AddRange(commands.Select(c => new TimelineEventDto
        {
            Timestamp = c.SentUtc,
            ZoneId = c.ZoneId,
            Kind = "command",
            Description = c.Type.StartsWith("setSetpoint")
                ? $"Setpoint set to {c.Value:0.#}°F"
                : c.Type switch
                {
                    "clearSetpoint" => "Returned to schedule",
                    _ => c.Type.Replace("injectFault:", "Fault injected: ")
                              .Replace("clearFault:", "Fault cleared: "),
                },
        }));
        events.AddRange(alarms.Select(a => new TimelineEventDto
        {
            Timestamp = a.RaisedUtc,
            ZoneId = a.ZoneId,
            Kind = "alarm-raised",
            Description = a.Message,
        }));
        events.AddRange(alarms.Where(a => a.ClearedUtc is not null).Select(a => new TimelineEventDto
        {
            Timestamp = a.ClearedUtc!.Value,
            ZoneId = a.ZoneId,
            Kind = "alarm-cleared",
            Description = $"Alarm cleared: {a.Type}",
        }));

        return events.OrderByDescending(e => e.Timestamp).Take(limit).ToList();
    }

    private void AddAudit(string tenantId, string zoneId, string type, double value)
    {
        _db.DeviceCommands.Add(new DeviceCommand
        {
            TenantId = tenantId,
            ZoneId = zoneId,
            Type = type,
            Value = value,
            SentUtc = DateTime.UtcNow,
        });
    }

    private async Task<ZoneControl> UpsertControlAsync(string tenantId, string zoneId,
        CancellationToken ct)
    {
        var control = await _db.ZoneControls.FirstOrDefaultAsync(
            c => c.TenantId == tenantId && c.ZoneId == zoneId, ct);
        if (control is null)
        {
            control = new ZoneControl { TenantId = tenantId, ZoneId = zoneId };
            _db.ZoneControls.Add(control);
        }
        return control;
    }
}
