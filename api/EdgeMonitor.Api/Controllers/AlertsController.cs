using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EdgeMonitor.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private static readonly string[] ValidConditions = { "above", "below" };
    private static readonly string[] ValidSensorTypes = { "temperature", "power", "occupancy" };

    private readonly EdgeMonitorDbContext _db;

    public AlertsController(EdgeMonitorDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<Alert>>> Get(
        [FromQuery] string tenantId = "tenant-demo", CancellationToken ct = default)
    {
        var alerts = await _db.Alerts.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedUtc)
            .ToListAsync(ct);
        return Ok(alerts);
    }

    [HttpPost]
    public async Task<ActionResult<Alert>> Create(
        [FromBody] CreateAlertRequest request, CancellationToken ct = default)
    {
        if (!ValidConditions.Contains(request.Condition))
        {
            return BadRequest($"Condition must be one of: {string.Join(", ", ValidConditions)}");
        }
        if (!ValidSensorTypes.Contains(request.SensorType))
        {
            return BadRequest($"SensorType must be one of: {string.Join(", ", ValidSensorTypes)}");
        }
        if (string.IsNullOrWhiteSpace(request.ZoneId))
        {
            return BadRequest("ZoneId is required");
        }

        var alert = new Alert
        {
            TenantId = string.IsNullOrWhiteSpace(request.TenantId) ? "tenant-demo" : request.TenantId,
            ZoneId = request.ZoneId,
            SensorType = request.SensorType,
            Condition = request.Condition,
            ThresholdValue = request.ThresholdValue,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow,
        };

        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { tenantId = alert.TenantId }, alert);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id,
        [FromQuery] string tenantId = "tenant-demo", CancellationToken ct = default)
    {
        // Tenant filter on the delete too — an id alone must never cross tenants.
        var alert = await _db.Alerts
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
        if (alert is null)
        {
            return NotFound();
        }

        _db.Alerts.Remove(alert);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
