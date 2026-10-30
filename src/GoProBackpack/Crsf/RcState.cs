namespace GoProBackpack.Crsf;

/// <summary>Latest RC channels and when they arrived. Written by <see cref="CrsfPort"/>, read by the control loop.</summary>
public sealed class RcState(TimeProvider time)
{
    private readonly Lock _lock = new();
    private int[] _channels = new int[RcChannels.Count];
    private DateTimeOffset? _lastFrameAt;

    public void OnChannels(int[] channels)
    {
        lock (_lock)
        {
            _channels = channels;
            _lastFrameAt = time.GetUtcNow();
        }
    }

    public RcSample Read()
    {
        lock (_lock)
            return new RcSample((int[])_channels.Clone(), _lastFrameAt);
    }
}

/// <param name="Channels">0-based array; EdgeTX CH6 is index 5.</param>
/// <param name="LastFrameAt">Null until the first valid frame.</param>
public readonly record struct RcSample(int[] Channels, DateTimeOffset? LastFrameAt);
