namespace GoProBackpack.Crsf;

/// <summary>
/// Incremental CRSF parser. Feed it raw UART bytes; it syncs on 0xC8, checks length and CRC,
/// and silently drops anything invalid.
/// </summary>
public sealed class CrsfParser
{
    private readonly byte[] _buffer = new byte[CrsfFrame.MaxFrameLength];
    private int _count;

    public int CrcErrors { get; private set; }

    public IEnumerable<CrsfFrame> Feed(ReadOnlyMemory<byte> data)
    {
        var frames = new List<CrsfFrame>();
        foreach (var b in data.Span)
        {
            if (_count == 0 && b != CrsfFrame.Sync)
                continue;

            _buffer[_count++] = b;

            if (_count == 2 && (b < 2 || b > CrsfFrame.MaxFrameLength - 2))
            {
                _count = 0;
                continue;
            }

            if (_count < 2 || _count < _buffer[1] + 2)
                continue;

            var len = _buffer[1];
            var body = _buffer.AsSpan(2, len - 1);
            if (Crc8.Compute(body) == _buffer[len + 1])
                frames.Add(new CrsfFrame(body[0], body[1..].ToArray()));
            else
                CrcErrors++;

            _count = 0;
        }
        return frames;
    }
}
