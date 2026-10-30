using System.Buffers.Binary;
using System.Text;
using GoProBackpack.Crsf;

namespace GoProBackpack.Telemetry;

/// <summary>State code sent in the battery frame's current field (×10, shown as 0.0–3.0 A on the Boxer).</summary>
public enum StatusCode
{
    NoCam = 0,
    Ready = 1,
    Rec = 2,
    Fault = 3,
}

/// <summary>Builds the 0x21 flight-mode and 0x08 battery frames (design doc section 6). Pure, no I/O.</summary>
public static class TelemetryEncoder
{
    public const int MaxFlightModeLength = 10;

    public static CrsfFrame FlightMode(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text.Length > MaxFlightModeLength ? text[..MaxFlightModeLength] : text);
        var payload = new byte[ascii.Length + 1]; // null-terminated
        ascii.CopyTo(payload, 0);
        return new CrsfFrame(CrsfFrame.TypeFlightMode, payload);
    }

    /// <param name="piVoltageDecivolts">PiV (RxBt), 0 if unknown.</param>
    /// <param name="status">Stat (Curr).</param>
    /// <param name="sdMinutes">SDmn (Capa), remaining recording time in minutes.</param>
    /// <param name="batteryPercent">GPBt (Bat%).</param>
    public static CrsfFrame Battery(int piVoltageDecivolts, StatusCode status, int sdMinutes, int batteryPercent)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0), (ushort)Math.Clamp(piVoltageDecivolts, 0, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), (ushort)((int)status * 10));
        var capacity = Math.Clamp(sdMinutes, 0, 0xFFFFFF);
        payload[4] = (byte)(capacity >> 16);
        payload[5] = (byte)(capacity >> 8);
        payload[6] = (byte)capacity;
        payload[7] = (byte)Math.Clamp(batteryPercent, 0, 100);
        return new CrsfFrame(CrsfFrame.TypeBattery, payload);
    }

    /// <summary>Main FM text: REC 12:34, REC 1h42, RDY 87m, START.., NO CAM.</summary>
    public static string MainText(StatusCode status, int recordingSeconds, int sdMinutes, bool startPending) => status switch
    {
        StatusCode.NoCam => "NO CAM",
        StatusCode.Rec when recordingSeconds >= 100 * 60 => $"REC {recordingSeconds / 3600}h{recordingSeconds / 60 % 60:00}",
        StatusCode.Rec => $"REC {recordingSeconds / 60:00}:{recordingSeconds % 60:00}",
        _ when startPending => "START..",
        _ => $"RDY {sdMinutes}m",
    };

    // TODO: warning texts (SD FULL, SD ERR, HOT, COLD, BUSY, STOP ERR) alternating with the main text every 2 s.
}
