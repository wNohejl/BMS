namespace EdgeMonitor.Api.Services;

/// <summary>
/// Production command channel: IoT Hub cloud-to-device messages.
/// Stub for the portfolio phase (same pattern as BACnetSensorReader) — the
/// engine's event shape is already identical, so wiring this in changes
/// nothing upstream.
///
/// To implement: add the Microsoft.Azure.Devices package, create a
/// ServiceClient from the IoT Hub service connection string, and send
/// new Message(JsonSerializer.SerializeToUtf8Bytes(new { type, zoneId, value }))
/// to the device. On the C++ side, handle it in CommandListener via
/// IoTHubDeviceClient_LL_SetMessageCallback.
/// </summary>
public class IoTHubCommandChannel : ICommandChannel
{
    private readonly ILogger<IoTHubCommandChannel> _logger;

    public IoTHubCommandChannel(ILogger<IoTHubCommandChannel> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string type, string zoneId, double value, string? fault = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning(
            "IoT Hub C2D command channel is not wired yet — command {Type} for {ZoneId} " +
            "was persisted but not delivered. See IoTHubCommandChannel.cs.", type, zoneId);
        return Task.CompletedTask;
    }
}
