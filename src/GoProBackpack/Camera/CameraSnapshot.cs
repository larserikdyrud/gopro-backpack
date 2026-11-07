namespace GoProBackpack.Camera;

/// <summary>
/// One reading of /gopro/camera/state, reduced to the statuses the design doc uses (section 7).
/// </summary>
public sealed record CameraSnapshot
{
    public required DateTimeOffset At { get; init; }

    /// <summary>Status 6.</summary>
    public bool Hot { get; init; }
    /// <summary>Status 8.</summary>
    public bool Busy { get; init; }
    /// <summary>Status 10.</summary>
    public bool Encoding { get; init; }
    /// <summary>Status 13, seconds into the current recording.</summary>
    public int RecordingSeconds { get; init; }
    /// <summary>
    /// Status 33 (PRIMARY_STORAGE). Open GoPro: OK=0, SD_CARD_FULL=1, SD_CARD_REMOVED=2,
    /// SD_CARD_FORMAT_ERROR=3, SD_CARD_BUSY=4, SD_CARD_SWAPPED=8. Only 0 counts as OK.
    /// </summary>
    public int SdStatus { get; init; }
    /// <summary>Status 35 (REMAINING_VIDEO_TIME), seconds of video left at current settings.</summary>
    public int RemainingVideo { get; init; }
    /// <summary>Status 70.</summary>
    public int BatteryPercent { get; init; }
    /// <summary>Status 85.</summary>
    public bool Cold { get; init; }

    public static CameraSnapshot FromStatuses(IReadOnlyDictionary<int, int> s, DateTimeOffset at) => new()
    {
        At = at,
        Hot = s.GetValueOrDefault(6) != 0,
        Busy = s.GetValueOrDefault(8) != 0,
        Encoding = s.GetValueOrDefault(10) != 0,
        RecordingSeconds = s.GetValueOrDefault(13),
        SdStatus = s.GetValueOrDefault(33, -1),
        RemainingVideo = s.GetValueOrDefault(35),
        BatteryPercent = s.GetValueOrDefault(70),
        Cold = s.GetValueOrDefault(85) != 0,
    };
}
