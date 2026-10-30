using GoProBackpack.Crsf;

namespace GoProBackpack.Tests;

public class CrsfTests
{
    [Fact]
    public void Crc8_matches_known_vector()
    {
        // CRC-8/DVB-S2 (poly 0xD5) check value for "123456789".
        Assert.Equal(0xBC, Crc8.Compute("123456789"u8));
    }

    [Fact]
    public void Frame_bytes_have_sync_length_and_crc()
    {
        var bytes = new CrsfFrame(CrsfFrame.TypeFlightMode, [0x41, 0x00]).ToBytes();

        Assert.Equal(CrsfFrame.Sync, bytes[0]);
        Assert.Equal(4, bytes[1]); // type + 2 payload + crc
        Assert.Equal(CrsfFrame.TypeFlightMode, bytes[2]);
        Assert.Equal(Crc8.Compute(bytes.AsSpan(2, 3)), bytes[^1]);
    }

    [Fact]
    public void Channels_round_trip()
    {
        var channels = Enumerable.Range(0, RcChannels.Count).Select(i => 172 + i * 100).ToArray();

        Assert.Equal(channels, RcChannels.Unpack(RcChannels.Pack(channels)));
    }

    [Fact]
    public void Parser_skips_garbage_and_split_frames()
    {
        var channels = Enumerable.Repeat(992, RcChannels.Count).ToArray();
        channels[5] = 1811;
        var frame = new CrsfFrame(CrsfFrame.TypeRcChannels, RcChannels.Pack(channels)).ToBytes();
        var stream = new byte[] { 0x00, 0xFF, 0x12 }.Concat(frame).ToArray();
        var parser = new CrsfParser();

        var first = parser.Feed(stream.AsMemory(0, 10)).ToList();
        var second = parser.Feed(stream.AsMemory(10)).ToList();

        Assert.Empty(first);
        var parsed = Assert.Single(second);
        Assert.Equal(CrsfFrame.TypeRcChannels, parsed.Type);
        Assert.Equal(1811, RcChannels.Unpack(parsed.Payload)[5]);
    }

    [Fact]
    public void Parser_drops_frame_with_bad_crc()
    {
        var frame = new CrsfFrame(CrsfFrame.TypeFlightMode, [0x41, 0x00]).ToBytes();
        frame[^1] ^= 0xFF;
        var parser = new CrsfParser();

        Assert.Empty(parser.Feed(frame));
        Assert.Equal(1, parser.CrcErrors);
    }
}
