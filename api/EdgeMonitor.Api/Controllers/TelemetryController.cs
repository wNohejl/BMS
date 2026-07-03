using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EdgeMonitor.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly EdgeMonitorDbContext _db;

    public TelemetryController(EdgeMonitorDbContext db)
    {
        _db = db;
    }

    /// <summary>Historical readings, newest first. Every query filters by TenantId.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReadingEventDto>>> Get(
        [FromQuery] string tenantId = "tenant-demo",
        [FromQuery] string? zoneId = null,
        [FromQuery] string? sensorType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 1000);

        var query = _db.SensorReadings.AsNoTracking().Where(r => r.TenantId == tenantId);

        if (!string.IsNullOrEmpty(zoneId))
        {
            query = query.Where(r => r.ZoneId == zoneId);
        }
        if (!string.IsNullOrEmpty(sensorType))
        {
            query = query.Where(r => r.SensorType == sensorType);
        }
        if (from is not null)
        {
            var fromUtc = AsUtc(from.Value);
            query = query.Where(r => r.TimestampUtc >= fromUtc);
        }
        if (to is not null)
        {
            var toUtc = AsUtc(to.Value);
            query = query.Where(r => r.TimestampUtc <= toUtc);
        }

        var readings = await query
            .OrderByDescending(r => r.TimestampUtc)
            .Take(limit)
            .Select(r => ToDto(r))
            .ToListAsync(ct);

        return Ok(readings);
    }

    /// <summary>Latest reading per zone + sensor type — powers the dashboard tiles on first load.</summary>
    [HttpGet("latest")]
    public async Task<ActionResult<List<ReadingEventDto>>> GetLatest(
        [FromQuery] string tenantId = "tenant-demo",
        CancellationToken ct = default)
    {
        var latest = await _db.SensorReadings.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .GroupBy(r => new { r.ZoneId, r.SensorType })
            .Select(g => g.OrderByDescending(r => r.TimestampUtc).First())
            .ToListAsync(ct);

        return Ok(latest.Select(ToDto).ToList());
    }

    private static ReadingEventDto ToDto(SensorReading r) => new()
    {
        SensorType = r.SensorType,
        Value = r.Value,
        Unit = r.Unit,
        ZoneId = r.ZoneId,
        TenantId = r.TenantId,
        DeviceId = r.DeviceId,
        Timestamp = r.TimestampUtc,
    };

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
