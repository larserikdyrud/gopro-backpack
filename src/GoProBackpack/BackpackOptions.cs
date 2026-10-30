namespace GoProBackpack;

/// <summary>
/// All tunables from the design doc (section 5 timing table, section 7 config).
/// Bound from the "Backpack" section of appsettings.json and /etc/gopro-backpack/appsettings.json.
/// </summary>
public sealed class BackpackOptions
{
    public const string Section = "Backpack";

    public string SerialPort { get; set; } = "/dev/ttyAMA0";
    public int BaudRate { get; set; } = 115200;

    /// <summary>1-based channel numbers as shown in EdgeTX.</summary>
    public int RecChannel { get; set; } = 6;
    public int MarkChannel { get; set; } = 7;

    /// <summary>Switch hysteresis in CRSF units (172–1811, 992 = centre).</summary>
    public int OnThreshold { get; set; } = 1400;
    public int OffThreshold { get; set; } = 600;

    public TimeSpan LinkTimeout { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan LinkStableTime { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan StopHoldTime { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan StartRetryInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan StopRetryInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int MaxStopAttempts { get; set; } = 3;
    public TimeSpan CameraPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan CameraOnlineTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Optional camera serial number, used when the USB interface cannot be found.</summary>
    public string? CameraSerial { get; set; }
}
