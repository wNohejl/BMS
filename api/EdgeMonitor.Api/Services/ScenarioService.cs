using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EdgeMonitor.Api.Services;

public record ScenarioStep(int AtSeconds, string Type, string ZoneId, double Value, string? Fault);

public record ScenarioInfo(string Name, string Title, string Description);

/// <summary>
/// Scripted incident scenarios and timeline replay — the twin's demo director.
/// Scenarios run through ControlService (audited, visible on the timeline);
/// replays re-drive the engine straight over the command channel so a replay
/// can never snowball into the audit it replays from.
/// </summary>
public class ScenarioService
{
    private static readonly Dictionary<string, (string Title, string Description, ScenarioStep[] Steps)>
        Catalog = new()
        {
            ["afternoon-meltdown"] = (
                "Afternoon meltdown",
                "A damper sticks, a sensor dies, and a unit loses refrigerant — then everything recovers.",
                new[]
                {
                    new ScenarioStep(0, "injectFault", "zone-2", 0, "damperStuck"),
                    new ScenarioStep(10, "injectFault", "zone-3", 0, "sensorOffline"),
                    new ScenarioStep(20, "injectFault", "zone-4", 0, "refrigerantLeak"),
                    new ScenarioStep(75, "clearFault", "zone-2", 0, "damperStuck"),
                    new ScenarioStep(80, "clearFault", "zone-3", 0, "sensorOffline"),
                    new ScenarioStep(85, "clearFault", "zone-4", 0, "refrigerantLeak"),
                }),
            ["silent-drift"] = (
                "Silent drift",
                "The Lobby sensor starts lying plausibly. Watch the model-residual alarm catch " +
                "what the threshold rules can't.",
                new[]
                {
                    new ScenarioStep(0, "injectFault", "zone-1", 0, "sensorDrift"),
                    new ScenarioStep(150, "clearFault", "zone-1", 0, "sensorDrift"),
                }),
            ["flapping-damper"] = (
                "Flapping damper",
                "An intermittent damper tries to make the alarms flap. Debounce holds one steady alarm.",
                new[]
                {
                    new ScenarioStep(0, "injectFault", "zone-2", 0, "damperChatter"),
                    new ScenarioStep(90, "clearFault", "zone-2", 0, "damperChatter"),
                }),
        };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICommandChannel _commands;
    private readonly ILogger<ScenarioService> _logger;
    private int _running; // 0 = idle, 1 = a scenario or replay is executing

    public ScenarioService(IServiceScopeFactory scopeFactory, ICommandChannel commands,
        ILogger<ScenarioService> logger)
    {
        _scopeFactory = scopeFactory;
        _commands = commands;
        _logger = logger;
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public static IReadOnlyList<ScenarioInfo> List() =>
        Catalog.Select(kv => new ScenarioInfo(kv.Key, kv.Value.Title, kv.Value.Description))
            .ToList();

    public static bool Exists(string name) => Catalog.ContainsKey(name);

    /// <summary>Start a canned scenario in the background. False when one is already running.</summary>
    public bool TryStartScenario(string name, string tenantId)
    {
        if (!Catalog.TryGetValue(name, out var scenario))
        {
            return false;
        }
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        _ = Task.Run(() => RunScenarioAsync(name, scenario.Steps, tenantId));
        return true;
    }

    private async Task RunScenarioAsync(string name, ScenarioStep[] steps, string tenantId)
    {
        try
        {
            _logger.LogInformation("Scenario '{Name}' started for {Tenant}", name, tenantId);
            var elapsed = 0;
            foreach (var step in steps)
            {
                if (step.AtSeconds > elapsed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(step.AtSeconds - elapsed));
                    elapsed = step.AtSeconds;
                }

                using var scope = _scopeFactory.CreateScope();
                var control = scope.ServiceProvider.GetRequiredService<ControlService>();
                switch (step.Type)
                {
                    case "injectFault":
                        await control.InjectFaultAsync(tenantId, step.ZoneId, step.Fault!);
                        break;
                    case "clearFault":
                        await control.ClearFaultAsync(tenantId, step.ZoneId, step.Fault!);
                        break;
                    case "setSetpoint":
                        await control.SetSetpointAsync(tenantId, step.ZoneId, step.Value);
                        break;
                    case "clearSetpoint":
                        await control.ClearSetpointAsync(tenantId, step.ZoneId);
                        break;
                }
            }
            _logger.LogInformation("Scenario '{Name}' finished", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scenario '{Name}' failed", name);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Replay the audited commands of the last N minutes with compressed
    /// spacing. Returns false when busy or when there is nothing to replay.</summary>
    public async Task<bool> TryStartReplayAsync(string tenantId, int minutes,
        CancellationToken ct = default)
    {
        List<DeviceCommand> commands;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EdgeMonitorDbContext>();
            var since = DateTime.UtcNow.AddMinutes(-Math.Clamp(minutes, 1, 120));
            commands = await db.DeviceCommands.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.SentUtc >= since)
                .OrderBy(c => c.SentUtc)
                .ToListAsync(ct);
        }

        var plan = BuildReplayPlan(commands);
        if (plan.Count == 0)
        {
            return false;
        }
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return false;
        }

        _ = Task.Run(() => RunReplayAsync(plan, tenantId));
        return true;
    }

    /// <summary>Pure planning step (unit-tested): audited commands → replay steps,
    /// 2s apart. Rows are decoded from the audit's Type encoding
    /// ("setSetpoint", "injectFault:damperStuck", …).</summary>
    public static List<ScenarioStep> BuildReplayPlan(IEnumerable<DeviceCommand> commands,
        int spacingSeconds = 2)
    {
        var steps = new List<ScenarioStep>();
        foreach (var command in commands.OrderBy(c => c.SentUtc))
        {
            ScenarioStep? step = command.Type switch
            {
                "setSetpoint" => new ScenarioStep(0, "setSetpoint", command.ZoneId, command.Value, null),
                "clearSetpoint" => new ScenarioStep(0, "clearSetpoint", command.ZoneId, 0, null),
                _ when command.Type.StartsWith("injectFault:") =>
                    new ScenarioStep(0, "injectFault", command.ZoneId, 0,
                        command.Type["injectFault:".Length..]),
                _ when command.Type.StartsWith("clearFault:") =>
                    new ScenarioStep(0, "clearFault", command.ZoneId, 0,
                        command.Type["clearFault:".Length..]),
                _ => null,
            };
            if (step is not null)
            {
                steps.Add(step with { AtSeconds = steps.Count * spacingSeconds });
            }
        }
        return steps;
    }

    private async Task RunReplayAsync(List<ScenarioStep> plan, string tenantId)
    {
        try
        {
            _logger.LogInformation("Replaying {Count} commands for {Tenant}", plan.Count, tenantId);
            var elapsed = 0;
            foreach (var step in plan)
            {
                if (step.AtSeconds > elapsed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(step.AtSeconds - elapsed));
                    elapsed = step.AtSeconds;
                }
                // Straight to the engine — replays are not re-audited, so a
                // replay can never include (and re-replay) itself.
                await _commands.SendAsync(step.Type, step.ZoneId, step.Value, step.Fault);
            }
            _logger.LogInformation("Replay finished");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Replay failed");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
