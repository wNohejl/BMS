using System.Net.Http.Json;
using EdgeMonitor.Dashboard.Models;

namespace EdgeMonitor.Dashboard.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<ReadingEvent>> GetLatestAsync()
        => await _http.GetFromJsonAsync<List<ReadingEvent>>("api/telemetry/latest") ?? new();

    public async Task<List<ReadingEvent>> GetHistoryAsync(string? zoneId, string? sensorType,
        DateTime fromUtc, DateTime toUtc, int limit = 1000)
    {
        var url = $"api/telemetry?limit={limit}" +
                  $"&from={Uri.EscapeDataString(fromUtc.ToString("O"))}" +
                  $"&to={Uri.EscapeDataString(toUtc.ToString("O"))}";
        if (!string.IsNullOrEmpty(zoneId)) url += $"&zoneId={Uri.EscapeDataString(zoneId)}";
        if (!string.IsNullOrEmpty(sensorType)) url += $"&sensorType={Uri.EscapeDataString(sensorType)}";

        return await _http.GetFromJsonAsync<List<ReadingEvent>>(url) ?? new();
    }

    public async Task<List<AlertRule>> GetAlertsAsync()
        => await _http.GetFromJsonAsync<List<AlertRule>>("api/alerts") ?? new();

    public async Task<AlertRule?> CreateAlertAsync(CreateAlertRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/alerts", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AlertRule>();
    }

    public async Task DeleteAlertAsync(int id)
        => (await _http.DeleteAsync($"api/alerts/{id}")).EnsureSuccessStatusCode();

    // Control flow: Dashboard → API → C++ engine → device
    public async Task<List<ZoneState>> GetZonesAsync()
        => await _http.GetFromJsonAsync<List<ZoneState>>("api/control/zones") ?? new();

    public async Task SetSetpointAsync(string zoneId, double value)
    {
        var response = await _http.PostAsJsonAsync($"api/control/zones/{zoneId}/setpoint",
            new { TenantId = "tenant-demo", Value = value });
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearSetpointAsync(string zoneId)
        => (await _http.DeleteAsync($"api/control/zones/{zoneId}/setpoint")).EnsureSuccessStatusCode();

    // Digital twin: fault injection + live alarms
    public async Task<List<ActiveAlarm>> GetAlarmsAsync()
        => await _http.GetFromJsonAsync<List<ActiveAlarm>>("api/twin/alarms") ?? new();

    public async Task InjectFaultAsync(string zoneId, string fault)
    {
        var response = await _http.PostAsJsonAsync($"api/twin/zones/{zoneId}/faults",
            new { TenantId = "tenant-demo", Fault = fault });
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearFaultAsync(string zoneId, string fault)
        => (await _http.DeleteAsync($"api/twin/zones/{zoneId}/faults/{fault}")).EnsureSuccessStatusCode();

    public async Task<List<TimelineEvent>> GetTimelineAsync(int limit = 30)
        => await _http.GetFromJsonAsync<List<TimelineEvent>>($"api/twin/timeline?limit={limit}") ?? new();

    public async Task<List<ScenarioInfo>> GetScenariosAsync()
        => await _http.GetFromJsonAsync<List<ScenarioInfo>>("api/twin/scenarios") ?? new();

    public async Task RunScenarioAsync(string name)
        => (await _http.PostAsync($"api/twin/scenarios/{name}/run", null)).EnsureSuccessStatusCode();

    public async Task ReplayAsync(int minutes = 10)
        => (await _http.PostAsync($"api/twin/replay?minutes={minutes}", null)).EnsureSuccessStatusCode();
}
