using System.Text;
using System.Text.Json;
using Azure.Messaging.EventHubs.Consumer;
using EdgeMonitor.Api.Models;

namespace EdgeMonitor.Api.Services;

/// <summary>
/// Production listener: reads engine messages from IoT Hub's built-in
/// Event Hub-compatible endpoint and feeds the shared pipeline. Telemetry
/// batches carry "readings"; engine status snapshots carry "zones".
/// Configure Listener:EventHubCompatibleEndpoint with the connection string from
/// Azure Portal → IoT Hub → Built-in endpoints (must include EntityPath).
/// </summary>
public class IoTHubListenerService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<IoTHubListenerService> _logger;

    public IoTHubListenerService(IServiceScopeFactory scopeFactory, IConfiguration config,
        ILogger<IoTHubListenerService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var endpoint = _config["Listener:EventHubCompatibleEndpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning(
                "Listener:EventHubCompatibleEndpoint is not configured — IoT Hub listener disabled");
            return;
        }

        var consumerGroup = _config["Listener:ConsumerGroup"]
                            ?? EventHubConsumerClient.DefaultConsumerGroupName;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var consumer = new EventHubConsumerClient(consumerGroup, endpoint);
                _logger.LogInformation("IoTHubListener connected (consumer group {Group})",
                    consumerGroup);

                await foreach (var partitionEvent in consumer.ReadEventsAsync(ct))
                {
                    if (partitionEvent.Data is null)
                    {
                        continue;
                    }

                    await ProcessEventAsync(partitionEvent.Data.EventBody.ToArray(), ct);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IoT Hub listener failed — retrying in 10s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ProcessEventAsync(byte[] body, CancellationToken ct)
    {
        try
        {
            var json = Encoding.UTF8.GetString(body);
            using var doc = JsonDocument.Parse(json);
            using var scope = _scopeFactory.CreateScope();

            if (doc.RootElement.TryGetProperty("zones", out _))
            {
                var status = JsonSerializer.Deserialize<ZoneStatusBatchDto>(json, JsonOpts);
                if (status is { Zones.Count: > 0 })
                {
                    var control = scope.ServiceProvider.GetRequiredService<ControlService>();
                    await control.ProcessStatusAsync(status, ct);
                }
                return;
            }

            var batch = JsonSerializer.Deserialize<TelemetryBatchDto>(json, JsonOpts);
            if (batch is { Readings.Count: > 0 })
            {
                var telemetry = scope.ServiceProvider.GetRequiredService<TelemetryService>();
                await telemetry.ProcessBatchAsync(batch, ct);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Skipping malformed IoT Hub event");
        }
    }
}
