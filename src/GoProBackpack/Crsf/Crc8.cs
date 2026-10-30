namespace GoProBackpack.Crsf;

/// <summary>CRSF CRC8, polynomial 0xD5, computed over type + payload.</summary>
public static class Crc8
{
    private static readonly byte[] Table = BuildTable();

    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data)
            crc = Table[crc ^ b];
        return crc;
    }

    private static byte[] BuildTable()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (byte)i;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0xD5) : (byte)(crc << 1);
            table[i] = crc;
        }
        return table;
    }
}
