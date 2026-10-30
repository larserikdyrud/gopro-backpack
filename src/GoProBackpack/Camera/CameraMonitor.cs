using Microsoft.Extensions.Options;

namespace GoProBackpack.Camera;

/// <summary>
/// Polls /gopro/camera/state every 500 ms and publishes a <see cref="CameraSnapshot"/>.
/// On (re)connect: wired_usb?p=1, exit webcam mode if needed, Auto Power Down = Never,
/// then keep-alive every 3 s.
/// </summary>
public sealed class CameraMonitor(
    GoProClient client,
    IOptions<BackpackOptions> options,
    TimeProvider time,
    HealthWatchdog health,
    ILogger<CameraMonitor> logger) : BackgroundService
{
    private readonly BackpackOptions _options = options.Value;
    private volatile CameraSnapshot? _latest;

    /// <summary>Latest successful reading, or null if none within the online timeout.</summary>
    public CameraSnapshot? Current
    {
        get
        {
            var s = _latest;
            return s is not null && time.GetUtcNow() - s.At < _options.CameraOnlineTimeout ? s : null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connected = false;
        var lastKeepAlive = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            health.Beat(nameof(CameraMonitor));
            try
            {
                client.Address ??= CameraLocator.Locate(_options.CameraSerial);
                if (client.Address is null)
                {
                    connected = false;
                }
                else
                {
                    if (!connected)
                        connected = await ConnectAsync(stoppingToken);

                    var statuses = connected ? await client.GetStatusesAsync(stoppingToken) : null;
                    if (statuses is null)
                    {
                        if (connected)
                            logger.LogWarning("Camera at {Address} stopped answering", client.Address);
                        connected = false;
                        client.Address = null; // re-locate: the USB interface may have changed
                    }
                    else
                    {
                        _latest = CameraSnapshot.FromStatuses(statuses, time.GetUtcNow());
                        if (time.GetUtcNow() - lastKeepAlive >= _options.KeepAliveInterval)
                        {
                            await client.KeepAliveAsync(stoppingToken);
                            lastKeepAlive = time.GetUtcNow();
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Camera poll failed");
                connected = false;
            }

            await Task.Delay(_options.CameraPollInterval, time, stoppingToken);
        }
    }

    private async Task<bool> ConnectAsync(CancellationToken ct)
    {
        if (!await client.EnableWiredUsbControlAsync(ct))
        {
            // A camera stuck in webcam mode may refuse control; try to get it out (design doc section 3).
            if (await client.GetWebcamStatusAsync(ct) is > 0)
                await client.WebcamExitAsync(ct);
            return false;
        }

        if (await client.GetWebcamStatusAsync(ct) is > 0)
        {
            logger.LogWarning("Camera is in webcam mode, sending webcam/exit");
            await client.WebcamExitAsync(ct);
        }

        // TODO: read the option id for "Never" from the camera's capabilities (open point, section 11).
        // await client.SetSettingAsync(59, neverOption, ct);

        logger.LogInformation("Camera connected at {Address}", client.Address);
        return true;
    }
}
