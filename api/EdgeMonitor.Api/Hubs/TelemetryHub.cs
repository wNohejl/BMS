using Microsoft.AspNetCore.SignalR;

namespace EdgeMonitor.Api.Hubs;

/// <summary>
/// Thin SignalR hub — all broadcast logic lives in ITelemetryBroadcaster.
/// Events pushed to clients: ReadingReceived, AlertTriggered, DeviceStatusChanged.
/// </summary>
public class TelemetryHub : Hub
{
    /// <summary>v2 multi-tenant auth: clients join their tenant group so broadcasts
    /// can be scoped per tenant instead of Clients.All.</summary>
    public Task JoinTenant(string tenantId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");
}
