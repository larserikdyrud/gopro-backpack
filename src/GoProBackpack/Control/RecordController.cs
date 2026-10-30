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
/// (RC sample, camera snapshot, time) â†’ commands. No I/O; ticked every 50 ms.
/// </summary>
/// <remarks>
/// Invariant: ShutterStop is only ever returned by rule 2 (edge + hold, stable link).
/// RecordControllerTests drives every other event and asserts it never appears.
/// </remarks>
public sealed class RecordController(BackpackOptions options)
{
    private readonly BackpackOptions _o = options;

    // Link tracking.
    private DateTimeOffset? _lastFrameSeen;
    private DateTimeOffset? _linkOkSince;

    // Switch states, updated only from frames received with link OK (hysteresis applied).
    private bool? _rec;   // Rule 1: also the latched value used during link loss.
    private bool? _mark;
    private bool _recReadSinceLink;         // a definite REC value has arrived since the link came back

    // Rule 2 edge detector. Reset on link loss (rule 3); re-seeded once the link is stable (rules 4 and 5).
    private bool _edgeReferenceSet;
    private bool _armed;                    // ON observed with stable link since the reference
    private DateTimeOffset? _offSince;      // ONâ†’OFF edge seen, hold running

    // Rule 2 stop sequence.
    private bool _stopping;
    private int _stopAttempts;
    private DateTimeOffset? _lastStopAt;

    // Rule 1 start retries.
    private DateTimeOffset? _lastStartAt;

    /// <summary>Start has been sent but the camera has not confirmed (FM shows START..).</summary>
    public bool StartPending { get; private set; }

    /// <summary>Starts sent since the camera last confirmed recording. 3 or more counts as FAULT.</summary>
    public int ConsecutiveStartAttempts { get; private set; }

    /// <summary>The last stop failed MaxStopAttempts times (FM shows STOP ERR).</summary>
    public bool StopFailed { get; private set; }

    public IReadOnlyList<CameraCommand> Tick(RcSample rc, CameraSnapshot? camera, DateTimeOffset now)
    {
        var commands = new List<CameraCommand>();
        var linkOk = UpdateLink(rc, now);

        // Stability and the stop hold are timed on frame arrival, not the clock: frames stop the moment the
        // link drops, while "link OK" only lapses after LinkTimeout. A hold must be proven by frames.
        var frameAt = rc.LastFrameAt ?? now;
        var linkStable = linkOk && frameAt - _linkOkSince >= _o.LinkStableTime;

        var previousMark = _mark;
        if (linkOk)
        {
            _rec = Interpret(rc.Channels, _o.RecChannel, _rec);
            _mark = Interpret(rc.Channels, _o.MarkChannel, _mark);
            _recReadSinceLink |= IsDefinite(rc.Channels[_o.RecChannel - 1]);
        }
        else
        {
            ResetEdgeDetector();      // rule 3
            _mark = null;             // a button held across link loss is not a press
            _recReadSinceLink = false; // the latched REC value is for starting only
        }

        var recording = camera?.Encoding == true;

        UpdateStopGesture(linkStable, frameAt);
        RunStopSequence(camera, recording, now, commands);
        RunStart(camera, recording, now, commands);

        // Rule 6: rising edge on MARK while recording.
        if (linkOk && previousMark == false && _mark == true && recording)
            commands.Add(CameraCommand.Hilight);

        return commands;
    }

    private bool UpdateLink(RcSample rc, DateTimeOffset now)
    {
        if (rc.LastFrameAt is not { } frameAt)
            return false;

        // A gap between frames longer than the timeout is an outage, even if no tick saw it.
        if (frameAt != _lastFrameSeen)
        {
            if (_lastFrameSeen is null || frameAt - _lastFrameSeen.Value >= _o.LinkTimeout)
                _linkOkSince = frameAt;
            _lastFrameSeen = frameAt;
        }

        var ok = now - frameAt < _o.LinkTimeout;
        if (!ok)
            _linkOkSince = null;
        return ok;
    }

    private bool? Interpret(int[] channels, int channelNumber, bool? previous)
    {
        var value = channels[channelNumber - 1];
        if (value > _o.OnThreshold) return true;
        if (value < _o.OffThreshold) return false;
        return previous;
    }

    private bool IsDefinite(int value) => value > _o.OnThreshold || value < _o.OffThreshold;

    /// <summary>Rules 2, 4 and 5: the edge + hold that commits a stop.</summary>
    private void UpdateStopGesture(bool linkStable, DateTimeOffset frameAt)
    {
        if (!linkStable || _rec is null || !_recReadSinceLink)
        {
            _offSince = null; // a link that is not stable cancels a hold in progress
            if (!linkStable)
                ResetEdgeDetector();
            return;
        }

        if (!_edgeReferenceSet)
        {
            // Rules 4 and 5: the first value with a stable link is the reference, never an edge.
            _edgeReferenceSet = true;
            _armed = _rec == true;
            return;
        }

        if (_rec == true)
        {
            _armed = true;
            _offSince = null;
            return;
        }

        if (_armed)
        {
            _armed = false;
            _offSince = frameAt;
        }

        if (_offSince is { } since && frameAt - since >= _o.StopHoldTime)
        {
            _offSince = null;
            _stopping = true;
            _stopAttempts = 0;
            _lastStopAt = null;
            StopFailed = false;
        }
    }

    private void ResetEdgeDetector()
    {
        _edgeReferenceSet = false;
        _armed = false;
        _offSince = null;
    }

    /// <summary>Rule 2 continued: up to MaxStopAttempts, StopRetryInterval apart, until the camera stops.</summary>
    private void RunStopSequence(CameraSnapshot? camera, bool recording, DateTimeOffset now, List<CameraCommand> commands)
    {
        // STOP ERR lasts until the camera stops or REC goes back ON.
        if (_rec == true || (camera is not null && !recording))
        {
            _stopping = false;
            StopFailed = false;
            return;
        }

        if (!_stopping)
            return;

        if (_lastStopAt is { } last && now - last < _o.StopRetryInterval)
            return;

        if (_stopAttempts >= _o.MaxStopAttempts)
        {
            _stopping = false;
            StopFailed = true;
            return;
        }

        _stopAttempts++;
        _lastStopAt = now;
        if (camera is not null) // an offline camera still uses up an attempt
            commands.Add(CameraCommand.ShutterStop);
    }

    /// <summary>Rule 1: level-triggered start, latched through link loss, retried until confirmed.</summary>
    private void RunStart(CameraSnapshot? camera, bool recording, DateTimeOffset now, List<CameraCommand> commands)
    {
        if (recording)
        {
            StartPending = false;
            ConsecutiveStartAttempts = 0;
            _lastStartAt = null;
            return;
        }

        if (_rec != true)
        {
            StartPending = false;
            _lastStartAt = null;
            return;
        }

        if (camera is null || camera.Busy)
            return;

        if (_lastStartAt is { } last && now - last < _o.StartRetryInterval)
            return;

        commands.Add(CameraCommand.ShutterStart);
        _lastStartAt = now;
        StartPending = true;
        ConsecutiveStartAttempts++;
    }
}
