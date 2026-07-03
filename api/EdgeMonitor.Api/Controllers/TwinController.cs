using EdgeMonitor.Api.Models;
using EdgeMonitor.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EdgeMonitor.Api.Controllers;

/// <summary>
/// Digital-twin operations: intentionally break the building and watch it react.
/// Faults are injected at the engine's device layer; alarms come back from the
/// engine's behavior-based AlarmMonitor via status snapshots.
/// </summary>
[ApiController]
[Route("api/twin")]
public class TwinController : ControllerBase
{
    private static readonly string[] ValidFaults =
    {
        "sensorOffline", "sensorDrift", "damperStuck", "damperChatter",
        "hvacOverload", "refrigerantLeak",
    };

    private readonly ControlService _control;
    private readonly ScenarioService _scenarios;

    public TwinController(ControlService control, ScenarioService scenarios)
    {
        _control = control;
        _scenarios = scenarios;
    }

    [HttpGet("scenarios")]
    public ActionResult<IReadOnlyList<ScenarioInfo>> GetScenarios() => Ok(ScenarioService.List());

    [HttpPost("scenarios/{name}/run")]
    public IActionResult RunScenario(string name, [FromQuery] string tenantId = "tenant-demo")
    {
        if (!ScenarioService.Exists(name))
        {
            return NotFound($"Unknown scenario '{name}'. GET api/twin/scenarios for the list.");
        }
        if (!_scenarios.TryStartScenario(name, tenantId))
        {
            return Conflict("A scenario or replay is already running — let it finish first.");
        }
        return Accepted();
    }

    [HttpPost("replay")]
    public async Task<IActionResult> Replay([FromQuery] string tenantId = "tenant-demo",
        [FromQuery] int minutes = 10, CancellationToken ct = default)
    {
        if (!await _scenarios.TryStartReplayAsync(tenantId, minutes, ct))
        {
            return Conflict("Nothing to replay in that window, or a scenario is already running.");
        }
        return Accepted();
    }

    [HttpGet("alarms")]
    public async Task<ActionResult<List<ActiveAlarmDto>>> GetAlarms(
        [FromQuery] string tenantId = "tenant-demo", CancellationToken ct = default)
    {
        return Ok(await _control.GetActiveAlarmsAsync(tenantId, ct));
    }

    [HttpGet("timeline")]
    public async Task<ActionResult<List<TimelineEventDto>>> GetTimeline(
        [FromQuery] string tenantId = "tenant-demo", [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        return Ok(await _control.GetTimelineAsync(tenantId, limit, ct));
    }

    [HttpPost("zones/{zoneId}/faults")]
    public async Task<IActionResult> InjectFault(string zoneId,
        [FromBody] FaultRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return BadRequest("zoneId is required");
        }
        if (!ValidFaults.Contains(request.Fault))
        {
            return BadRequest($"Fault must be one of: {string.Join(", ", ValidFaults)}");
        }

        var tenantId = string.IsNullOrWhiteSpace(request.TenantId) ? "tenant-demo" : request.TenantId;
        await _control.InjectFaultAsync(tenantId, zoneId, request.Fault, ct);
        return Accepted();
    }

    [HttpDelete("zones/{zoneId}/faults/{fault}")]
    public async Task<IActionResult> ClearFault(string zoneId, string fault,
        [FromQuery] string tenantId = "tenant-demo", CancellationToken ct = default)
    {
        if (!ValidFaults.Contains(fault))
        {
            return BadRequest($"Fault must be one of: {string.Join(", ", ValidFaults)}");
        }

        await _control.ClearFaultAsync(tenantId, zoneId, fault, ct);
        return Accepted();
    }
}
