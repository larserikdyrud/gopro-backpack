using GoProBackpack;
using GoProBackpack.Camera;
using GoProBackpack.Control;
using GoProBackpack.Crsf;
using GoProBackpack.Telemetry;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Site config on the Pi overrides the defaults shipped next to the binary.
builder.Configuration.AddJsonFile("/etc/gopro-backpack/appsettings.json", optional: true, reloadOnChange: false);
builder.Services.Configure<BackpackOptions>(builder.Configuration.GetSection(BackpackOptions.Section));

builder.Services.AddSystemd();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RcState>();
builder.Services.AddSingleton(sp => new RecordController(sp.GetRequiredService<IOptions<BackpackOptions>>().Value));
builder.Services.AddHttpClient<GoProClient>((sp, http) =>
    http.Timeout = sp.GetRequiredService<IOptions<BackpackOptions>>().Value.HttpTimeout);

// Hosted services that other services also depend on are registered once as singletons.
builder.Services.AddSingleton<HealthWatchdog>();
builder.Services.AddSingleton<CrsfPort>();
builder.Services.AddSingleton<CameraMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HealthWatchdog>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CrsfPort>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CameraMonitor>());
builder.Services.AddHostedService<ControlLoop>();
builder.Services.AddHostedService<TelemetryPublisher>();

// A loop that dies must not take the host down silently; systemd restarts the process instead.
builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

builder.Build().Run();
