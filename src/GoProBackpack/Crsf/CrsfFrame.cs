namespace GoProBackpack.Crsf;

/// <summary>A validated CRSF frame: [sync] [len] [type] [payload] [crc8]. len counts type + payload + crc.</summary>
public readonly record struct CrsfFrame(byte Type, byte[] Payload)
{
    public const byte Sync = 0xC8;
    public const int MaxFrameLength = 64;

    public const byte TypeBattery = 0x08;
    public const byte TypeLinkStatistics = 0x14;
    public const byte TypeRcChannels = 0x16;
    public const byte TypeFlightMode = 0x21;

    public byte[] ToBytes()
    {
        var bytes = new byte[Payload.Length + 4];
        bytes[0] = Sync;
        bytes[1] = (byte)(Payload.Length + 2);
        bytes[2] = Type;
        Payload.CopyTo(bytes, 3);
        bytes[^1] = Crc8.Compute(bytes.AsSpan(2, Payload.Length + 1));
        return bytes;
    }
}
