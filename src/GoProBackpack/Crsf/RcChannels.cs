namespace GoProBackpack.Crsf;

/// <summary>Unpacks the 0x16 payload: 16 channels × 11 bits, little-endian bit order.</summary>
public static class RcChannels
{
    public const int Count = 16;
    public const int PayloadLength = 22;

    public static int[] Unpack(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < PayloadLength)
            throw new ArgumentException($"RC channels payload must be {PayloadLength} bytes", nameof(payload));

        var channels = new int[Count];
        var bitBuffer = 0u;
        var bits = 0;
        var index = 0;
        foreach (var b in payload[..PayloadLength])
        {
            bitBuffer |= (uint)b << bits;
            bits += 8;
            while (bits >= 11 && index < Count)
            {
                channels[index++] = (int)(bitBuffer & 0x7FF);
                bitBuffer >>= 11;
                bits -= 11;
            }
        }
        return channels;
    }

    /// <summary>Inverse of <see cref="Unpack"/>; used by tests and simulators.</summary>
    public static byte[] Pack(IReadOnlyList<int> channels)
    {
        var payload = new byte[PayloadLength];
        var bitBuffer = 0u;
        var bits = 0;
        var pos = 0;
        for (var i = 0; i < Count; i++)
        {
            bitBuffer |= ((uint)channels[i] & 0x7FF) << bits;
            bits += 11;
            while (bits >= 8)
            {
                payload[pos++] = (byte)bitBuffer;
                bitBuffer >>= 8;
                bits -= 8;
            }
        }
        return payload;
    }
}
