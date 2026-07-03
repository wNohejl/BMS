using EdgeMonitor.Dashboard.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace EdgeMonitor.Dashboard.Services;

/// <summary>
/// Wraps the SignalR HubConnection to the API's /hubs/telemetry endpoint.
/// Locally that's the in-process hub; in production Azure SignalR sits behind
/// the same URL — the dashboard code doesn't change.
/// </summary>
public class SignalRService : IAsyncDisposable
{
    private readonly string _hubUrl;
    private HubConnection? _connection;

    public event Action<ReadingEvent>? ReadingReceived;
    public event Action<AlertEvent>? AlertTriggered;
    public event Action<ZoneState>? ZoneStateChanged;
    public event Action<List<ActiveAlarm>>? AlarmsChanged;

    public SignalRService(string apiBaseUrl)
    {
        _hubUrl = apiBaseUrl.TrimEnd('/') + "/hubs/telemetry";
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task StartAsync()
    {
        if (_connection is not null)
        {
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ReadingEvent>("ReadingReceived", r => ReadingReceived?.Invoke(r));
        _connection.On<AlertEvent>("AlertTriggered", a => AlertTriggered?.Invoke(a));
        _connection.On<ZoneState>("ZoneStateChanged", z => ZoneStateChanged?.Invoke(z));
        _connection.On<List<ActiveAlarm>>("AlarmsChanged", a => AlarmsChanged?.Invoke(a));

        await _connection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
