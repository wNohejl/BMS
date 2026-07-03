using EdgeMonitor.Api.Models;

namespace EdgeMonitor.Api.Services;

/// <summary>
/// Abstraction over the SignalR hub context so services stay unit-testable
/// without a running hub (tests use a fake; production uses SignalRTelemetryBroadcaster).
/// </summary>
public interface ITelemetryBroadcaster
{
    Task ReadingReceivedAsync(ReadingEventDto reading, CancellationToken ct = default);
    Task AlertTriggeredAsync(AlertEventDto alert, CancellationToken ct = default);
    Task ZoneStateChangedAsync(ZoneStateEventDto zone, CancellationToken ct = default);
    Task ActiveAlarmsChangedAsync(List<ActiveAlarmDto> alarms, CancellationToken ct = default);
}
