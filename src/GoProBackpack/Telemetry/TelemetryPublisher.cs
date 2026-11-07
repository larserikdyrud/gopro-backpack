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
        // Status 33: only PRIMARY_STORAGE OK (0) is ready; other values are SD faults.
        // HOT/COLD/BUSY warning texts stay for the alternating-FM follow-up.
        { SdStatus: not 0 } => StatusCode.Fault,
        _ when consecutiveStartAttempts >= 3 => StatusCode.Fault,
        _ => StatusCode.Ready,
    };

    // Status 35 is seconds (Open GoPro REMAINING_VIDEO_TIME); show whole minutes on the radio.
    private static int SdMinutes(CameraSnapshot? s) => (s?.RemainingVideo ?? 0) / 60;
}
