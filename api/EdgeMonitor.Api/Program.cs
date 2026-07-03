using EdgeMonitor.Api.Data;
using EdgeMonitor.Api.Hubs;
using EdgeMonitor.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<EdgeMonitorDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Azure SignalR in production; in-process hub in development ($0).
var signalRConnection = builder.Configuration["AzureSignalR:ConnectionString"];
var signalRBuilder = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(signalRConnection))
{
    signalRBuilder.AddAzureSignalR(signalRConnection);
}

// Register the right listener for the environment:
// FileSystem = local dev ($0, watches /tmp/edgemonitor/readings/), IoTHub = production.
var listenerMode = builder.Configuration["Listener:Mode"]
                   ?? (builder.Environment.IsDevelopment() ? "FileSystem" : "IoTHub");
if (listenerMode.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<FileSystemListenerService>();
}
else
{
    builder.Services.AddHostedService<IoTHubListenerService>();
}

builder.Services.AddScoped<TelemetryService>();
builder.Services.AddScoped<AlertEvaluationService>();
builder.Services.AddScoped<ControlService>();
builder.Services.AddSingleton<ScenarioService>();
builder.Services.AddSingleton<ITelemetryBroadcaster, SignalRTelemetryBroadcaster>();

// Command channel to the C++ engine (Dashboard → API → engine → device):
// file-based inbox locally, IoT Hub cloud-to-device in production.
if (listenerMode.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ICommandChannel, FileSystemCommandChannel>();
}
else
{
    builder.Services.AddSingleton<ICommandChannel, IoTHubCommandChannel>();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SignalR negotiate from the Blazor dashboard needs credentialed CORS.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? new[] { "http://localhost:5001", "https://localhost:5001" };
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

// v2: gRPC direct channel from the C++ agent (bypasses IoT Hub for low-latency use cases)
// builder.Services.AddGrpc();

var app = builder.Build();

// Dev convenience: create the schema on startup. Production uses EF migrations
// (dotnet ef database update) and keeps Database:AutoCreate = false.
if (app.Configuration.GetValue<bool>("Database:AutoCreate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<EdgeMonitorDbContext>().Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();
app.MapHub<TelemetryHub>("/hubs/telemetry");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
