using GoProBackpack.Camera;
using GoProBackpack.Crsf;

namespace GoProBackpack.Control;

public enum CameraCommand
{
    ShutterStart,
    ShutterStop,
    Hilight,
}

/// <summary>
/// The six rules from design doc section 5 as a pure state machine:
/// (RC sample, camera snapshot, time) → commands. No I/O; ticked every 50 ms.
/// </summary>
/// <remarks>
/// Invariant: ShutterStop is only ever returned by rule 2 (edge + hold, stable link).
/// A unit test must drive every other event and assert it never appears.
/// </remarks>
public sealed class RecordController(BackpackOptions options)
{
    private readonly BackpackOptions _options = options;

    public IReadOnlyList<CameraCommand> Tick(RcSample rc, CameraSnapshot? camera, DateTimeOffset now)
    {
        // TODO: implement rules 1–6 from section 5:
        //  1. Start (level), latched REC through link loss, retry every StartRetryInterval.
        //  2. Stop (edge + hold), only with stable link, up to MaxStopAttempts.
        //  3. Link loss: no stop, reset edge detector.
        //  4. Link back: first value after LinkStableTime is the new reference.
        //  5. Service start: never stop during init.
        //  6. Mark: rising edge while recording.
        return [];
    }

    /// <summary>Most recent stop attempt failed MaxStopAttempts times (FM shows STOP ERR).</summary>
    public bool StopFailed { get; private set; }

    /// <summary>Start has been sent but the camera has not confirmed (FM shows START..).</summary>
    public bool StartPending { get; private set; }
}
