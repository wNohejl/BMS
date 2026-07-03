using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdgeMonitor.Api.Services;

/// <summary>
/// Local dev command channel ($0 mode): writes {"type","zoneId","value"[,"fault"]}
/// JSON files into the directory the C++ engine's CommandListener polls.
/// Mirrors the telemetry path in reverse, tmp-then-rename included.
/// </summary>
public class FileSystemCommandChannel : ICommandChannel
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _commandDir;
    private readonly ILogger<FileSystemCommandChannel> _logger;

    public FileSystemCommandChannel(IConfiguration config, ILogger<FileSystemCommandChannel> logger)
    {
        _commandDir = config["Listener:CommandDirectory"] ?? "/tmp/edgemonitor/commands/";
        _logger = logger;
    }

    public async Task SendAsync(string type, string zoneId, double value, string? fault = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(_commandDir);

        var json = JsonSerializer.Serialize(new { type, zoneId, value, fault }, JsonOpts);
        var stem = $"cmd_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}";
        var tmpPath = Path.Combine(_commandDir, stem + ".tmp");
        var finalPath = Path.Combine(_commandDir, stem + ".json");

        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, finalPath);

        _logger.LogInformation("Command sent to engine: {Type} zone={ZoneId} value={Value} fault={Fault}",
            type, zoneId, value, fault ?? "-");
    }
}
