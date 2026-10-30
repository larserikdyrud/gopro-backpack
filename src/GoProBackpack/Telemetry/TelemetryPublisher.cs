using GoProBackpack.Camera;
using GoProBackpack.Control;
using GoProBackpack.Crsf;

namespace GoProBackpack.Telemetry;

/// <summary>Sends FM at 2 Hz (and on change) and the battery frame at 1 Hz.</summary>
public sealed class TelemetryPublisher(
    CrsfPort port,
    CameraMonitor camera,
    RecordController controller,
    HealthWatchdog health,
    ILogger<TelemetryPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? lastText = null;
        var ticks = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            health.Beat(nameof(TelemetryPublisher));
            try
            {
                var snapshot = camera.Current;
                var status = StatusFor(snapshot, controller.ConsecutiveStartAttempts);
                var text = TelemetryEncoder.MainText(
                    status, snapshot?.RecordingSeconds ?? 0, SdMinutes(snapshot), controller.StartPending);

                if (text != lastText || ticks % 5 == 0)
                {
                    port.Write(TelemetryEncoder.FlightMode(text));
                    lastText = text;
                }

                if (ticks % 10 == 0)
                    port.Write(TelemetryEncoder.Battery(0, status, SdMinutes(snapshot), snapshot?.BatteryPercent ?? 0));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Telemetry tick failed");
            }

            ticks++;
            await Task.Delay(Tick, stoppingToken);
        }
    }

    private static StatusCode StatusFor(CameraSnapshot? s, int consecutiveStartAttempts) => s switch
    {
        null => StatusCode.NoCam,
        { Encoding: true } => StatusCode.Rec,
        // TODO: confirm status 33 OK values; also FAULT when overheating stopped the camera (section 6).
        { SdStatus: not 0 } => StatusCode.Fault,
        _ when consecutiveStartAttempts >= 3 => StatusCode.Fault,
        _ => StatusCode.Ready,
    };

    // TODO: status 35 unit is unconfirmed (open point, section 11); assumed seconds here.
    private static int SdMinutes(CameraSnapshot? s) => (s?.RemainingVideo ?? 0) / 60;
}
