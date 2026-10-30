using GoProBackpack.Camera;
using GoProBackpack.Crsf;

namespace GoProBackpack.Control;

/// <summary>Ticks <see cref="RecordController"/> every 50 ms and carries out its commands.</summary>
public sealed class ControlLoop(
    RecordController controller,
    RcState rc,
    CameraMonitor camera,
    GoProClient client,
    TimeProvider time,
    HealthWatchdog health,
    ILogger<ControlLoop> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            health.Beat(nameof(ControlLoop));
            try
            {
                foreach (var command in controller.Tick(rc.Read(), camera.Current, time.GetUtcNow()))
                {
                    logger.LogInformation("Sending {Command}", command);
                    var ok = command switch
                    {
                        CameraCommand.ShutterStart => await client.ShutterStartAsync(stoppingToken),
                        CameraCommand.ShutterStop => await client.ShutterStopAsync(stoppingToken),
                        CameraCommand.Hilight => await client.HilightAsync(stoppingToken),
                        _ => false,
                    };
                    if (!ok)
                        logger.LogWarning("{Command} was not accepted", command);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Control tick failed");
            }

            await Task.Delay(TickInterval, time, stoppingToken);
        }
    }
}
