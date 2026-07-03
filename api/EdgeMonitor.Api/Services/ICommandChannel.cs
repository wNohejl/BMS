namespace EdgeMonitor.Api.Services;

/// <summary>
/// Outbound half of the control flow (Dashboard → API → C++ engine → Device).
/// Local dev writes JSON command files the engine's CommandListener polls;
/// production sends IoT Hub cloud-to-device messages. Same payload either way.
/// `fault` is set for injectFault/clearFault commands (digital-twin fault injection).
/// </summary>
public interface ICommandChannel
{
    Task SendAsync(string type, string zoneId, double value, string? fault = null,
        CancellationToken ct = default);
}
