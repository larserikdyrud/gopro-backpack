using GoProBackpack.Crsf;
using GoProBackpack.Telemetry;

namespace GoProBackpack.Tests;

public class TelemetryEncoderTests
{
    [Fact]
    public void Flight_mode_is_null_terminated_and_capped_at_10_chars()
    {
        var frame = TelemetryEncoder.FlightMode("ABCDEFGHIJKLMN");

        Assert.Equal(CrsfFrame.TypeFlightMode, frame.Type);
        Assert.Equal("ABCDEFGHIJ\0"u8.ToArray(), frame.Payload);
    }

    [Fact]
    public void Battery_frame_is_big_endian_with_status_times_ten()
    {
        var frame = TelemetryEncoder.Battery(51, StatusCode.Rec, 0x012345, 87);

        Assert.Equal(CrsfFrame.TypeBattery, frame.Type);
        Assert.Equal(new byte[] { 0x00, 51, 0x00, 20, 0x01, 0x23, 0x45, 87 }, frame.Payload);
    }

    [Theory]
    [InlineData(StatusCode.NoCam, 0, 0, false, "NO CAM")]
    [InlineData(StatusCode.Rec, 754, 0, false, "REC 12:34")]
    [InlineData(StatusCode.Rec, 6120, 0, false, "REC 1h42")]
    [InlineData(StatusCode.Ready, 0, 87, false, "RDY 87m")]
    [InlineData(StatusCode.Ready, 0, 87, true, "START..")]
    public void Main_text_follows_design_doc(StatusCode status, int recSeconds, int sdMinutes, bool startPending, string expected)
    {
        Assert.Equal(expected, TelemetryEncoder.MainText(status, recSeconds, sdMinutes, startPending));
    }
}
