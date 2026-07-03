using EdgeMonitor.Api.Models;
using EdgeMonitor.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EdgeMonitor.Api.Controllers;

/// <summary>
/// The inbound edge of the control flow: Dashboard → API → C++ engine → Device.
/// Setpoint changes are persisted, audited, and pushed to the engine over the
/// command channel; the engine's resulting state comes back as ZoneStateChanged.
/// </summary>
[ApiController]
[Route("api/control")]
public class ControlController : ControllerBase
{
    private const double MinSetpointF = 55;
    private const double MaxSetpointF = 90;

    private readonly ControlService _control;

    public ControlController(ControlService control)
    {
        _control = control;
    }

    [HttpGet("zones")]
    public async Task<ActionResult<List<ZoneStateEventDto>>> GetZones(
        [FromQuery] string tenantId = "tenant-demo", CancellationToken ct = default)
    {
        return Ok(await _control.GetZonesAsync(tenantId, ct));
    }

    [HttpPost("zones/{zoneId}/setpoint")]
    public async Task<ActionResult<ZoneControl>> SetSetpoint(string zoneId,
        [FromBody] SetSetpointRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return BadRequest("zoneId is required");
        }
        if (request.Value is < MinSetpointF or > MaxSetpointF)
        {
            return BadRequest($"Setpoint must be between {MinSetpointF} and {MaxSetpointF} °F");
        }

        var tenantId = string.IsNullOrWhiteSpace(request.TenantId) ? "tenant-demo" : request.TenantId;
        var control = await _control.SetSetpointAsync(tenantId, zoneId, request.Value, ct);
        return Ok(control);
    }

    [HttpDelete("zones/{zoneId}/setpoint")]
    public async Task<ActionResult<ZoneControl>> ClearSetpoint(string zoneId,
        [FromQuery] string tenantId = "tenant-demo", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return BadRequest("zoneId is required");
        }

        var control = await _control.ClearSetpointAsync(tenantId, zoneId, ct);
        return Ok(control);
    }
}
