using System.Text.Json;
using EdgeMonitor.Api.Models;

namespace EdgeMonitor.Api.Services;

/// <summary>
/// Local dev listener ($0 mode): polls a directory for JSON files written by
/// the C++ engine's --local flag and feeds them into the same pipeline the
/// IoT Hub listener uses. Two file kinds share the directory:
///   batch_*.json  — telemetry batches  → TelemetryService
///   status_*.json — engine control state → ControlService
/// Polling (instead of FileSystemWatcher) avoids half-written-file races —
/// the engine also writes tmp-then-rename.
/// </summary>
public class FileSystemListenerService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FileSystemListenerService> _logger;
    private readonly string _watchDir;

    public FileSystemListenerService(IServiceScopeFactory scopeFactory, IConfiguration config,
        ILogger<FileSystemListenerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _watchDir = config["Listener:LocalDirectory"] ?? "/tmp/edgemonitor/readings/";
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_watchDir);
        _logger.LogInformation("FileSystemListener watching {Dir} for telemetry and status files",
            _watchDir);

        while (!ct.IsCancellationRequested)
        {
            foreach (var file in Directory.EnumerateFiles(_watchDir, "*.json").OrderBy(f => f))
            {
                try
                {
                    await ProcessFileAsync(file, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // Writer may not be finished — retry on the next pass.
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Malformed file {File} — deleting", file);
                    TryDelete(file);
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ProcessFileAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        using var scope = _scopeFactory.CreateScope();

        if (Path.GetFileName(path).StartsWith("status_", StringComparison.Ordinal))
        {
            var status = JsonSerializer.Deserialize<ZoneStatusBatchDto>(json, JsonOpts);
            if (status is { Zones.Count: > 0 })
            {
                var control = scope.ServiceProvider.GetRequiredService<ControlService>();
                await control.ProcessStatusAsync(status, ct);
            }
        }
        else
        {
            var batch = JsonSerializer.Deserialize<TelemetryBatchDto>(json, JsonOpts);
            if (batch is { Readings.Count: > 0 })
            {
                var telemetry = scope.ServiceProvider.GetRequiredService<TelemetryService>();
                var count = await telemetry.ProcessBatchAsync(batch, ct);
                _logger.LogInformation("Processed {Count} readings from {File}", count,
                    Path.GetFileName(path));
            }
        }

        TryDelete(path);
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete {File} — will retry next pass", path);
        }
    }
}
