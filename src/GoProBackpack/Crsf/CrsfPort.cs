using System.IO.Ports;
using Microsoft.Extensions.Options;

namespace GoProBackpack.Crsf;

/// <summary>
/// Owns the UART (/dev/ttyAMA0, 115200 8N1). Reads CRSF frames from the receiver and
/// exposes the latest RC state; writes telemetry frames back.
/// </summary>
public sealed class CrsfPort(IOptions<BackpackOptions> options, RcState rcState, ILogger<CrsfPort> logger)
    : BackgroundService
{
    private readonly BackpackOptions _options = options.Value;
    private readonly CrsfParser _parser = new();
    private readonly Lock _writeLock = new();
    private SerialPort? _port;

    public void Write(CrsfFrame frame)
    {
        var bytes = frame.ToBytes();
        lock (_writeLock)
        {
            if (_port is { IsOpen: true })
                _port.Write(bytes, 0, bytes.Length);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new byte[256];
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _port ??= Open();
                var read = await _port.BaseStream.ReadAsync(buffer, stoppingToken);
                foreach (var frame in _parser.Feed(buffer.AsMemory(0, read)))
                    Handle(frame);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CRSF read failed, reopening {Port}", _options.SerialPort);
                ClosePort();
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        ClosePort();
    }

    private void Handle(CrsfFrame frame)
    {
        switch (frame.Type)
        {
            case CrsfFrame.TypeRcChannels when frame.Payload.Length >= RcChannels.PayloadLength:
                rcState.OnChannels(RcChannels.Unpack(frame.Payload));
                break;
            case CrsfFrame.TypeLinkStatistics:
                // TODO: keep uplink LQ/RSSI for logging (design doc section 7).
                break;
        }
    }

    private SerialPort Open()
    {
        var port = new SerialPort(_options.SerialPort, _options.BaudRate, Parity.None, 8, StopBits.One);
        port.Open();
        logger.LogInformation("Opened {Port} at {Baud} baud", _options.SerialPort, _options.BaudRate);
        return port;
    }

    private void ClosePort()
    {
        lock (_writeLock)
        {
            _port?.Dispose();
            _port = null;
        }
    }
}
