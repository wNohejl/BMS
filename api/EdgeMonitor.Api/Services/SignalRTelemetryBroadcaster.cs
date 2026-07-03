using EdgeMonitor.Api.Hubs;
using EdgeMonitor.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace EdgeMonitor.Api.Services;

public class SignalRTelemetryBroadcaster : ITelemetryBroadcaster
{
    private readonly IHubContext<TelemetryHub> _hub;

    public SignalRTelemetryBroadcaster(IHubContext<TelemetryHub> hub)
    {
        _hub = hub;
    }

    // Portfolio phase: broadcast to all connected clients (single demo tenant).
    // v2 multi-tenant auth: switch to Clients.Group($"tenant:{dto.TenantId}").
    public Task ReadingReceivedAsync(ReadingEventDto reading, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("ReadingReceived", reading, ct);

    public Task AlertTriggeredAsync(AlertEventDto alert, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("AlertTriggered", alert, ct);

    public Task ZoneStateChangedAsync(ZoneStateEventDto zone, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("ZoneStateChanged", zone, ct);

    public Task ActiveAlarmsChangedAsync(List<ActiveAlarmDto> alarms, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("AlarmsChanged", alarms, ct);
}
