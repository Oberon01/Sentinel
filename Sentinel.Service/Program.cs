using Sentinel.Core;
using Sentinel.Monitors;
using Sentinel.Service;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Sentinel Monitor";
});

// Config
builder.Services.Configure<SentinelOptions>(
    builder.Configuration.GetSection("Sentinel"));

// Serilog
var logPath = builder.Configuration["Sentinel:LogPath"] ?? @"C:\Logs\Sentinel";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logPath, "sentinel-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

builder.Services.AddSerilog();

// Core services
builder.Services.AddSingleton<AlertService>();

// Monitors
builder.Services.AddSingleton<IMonitor, SystemResourceMonitor>();
builder.Services.AddSingleton<IMonitor, NetworkMonitor>();
builder.Services.AddSingleton<IMonitor, FileSystemMonitor>();
builder.Services.AddSingleton<IMonitor, EventLogMonitor>();
builder.Services.AddSingleton<ConfigWatcher>();

// Worker
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();