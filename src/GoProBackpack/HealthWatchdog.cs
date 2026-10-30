using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting.Systemd;

namespace GoProBackpack;

/// <summary>
/// Sends WATCHDOG=1 to systemd only while every registered loop has beaten recently.
/// A hung loop stops the pings, and systemd (WatchdogSec=10) restarts the service.
/// </summary>
public sealed class HealthWatchdog(ISystemdNotifier notifier, TimeProvider time, ILogger<HealthWatchdog> logger)
    : BackgroundService
{
    private static readonly string[] Loops = ["CameraMonitor", "ControlLoop", "TelemetryPublisher"];
    private static readonly TimeSpan MaxSilence = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _beats = new();

    public void Beat(string loop) => _beats[loop] = time.GetUtcNow();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow();
            var stale = Loops.Where(l => !_beats.TryGetValue(l, out var at) || now - at > MaxSilence).ToList();

            if (stale.Count == 0)
                notifier.Notify(new ServiceState("WATCHDOG=1"));
            else
                logger.LogWarning("Withholding watchdog ping, stale loops: {Loops}", string.Join(", ", stale));

            await Task.Delay(TimeSpan.FromSeconds(2), time, stoppingToken);
        }
    }
}
