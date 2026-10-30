using System.Net;
using GoProBackpack.Camera;

namespace GoProBackpack.Tests;

public class CameraTests
{
    [Fact]
    public void Camera_address_from_serial_uses_last_three_digits()
    {
        Assert.Equal(IPAddress.Parse("172.24.156.51"), CameraLocator.FromSerial("C3501324500456"));
    }

    [Fact]
    public void Camera_is_dot_51_on_the_local_usb_subnet()
    {
        Assert.Equal(IPAddress.Parse("172.24.156.51"), CameraLocator.CameraAddressFor(IPAddress.Parse("172.24.156.50")));
    }

    [Fact]
    public void Snapshot_reads_design_doc_statuses()
    {
        var s = CameraSnapshot.FromStatuses(new Dictionary<int, int> { [10] = 1, [13] = 75, [70] = 64, [33] = 0 }, DateTimeOffset.UnixEpoch);

        Assert.True(s.Encoding);
        Assert.Equal(75, s.RecordingSeconds);
        Assert.Equal(64, s.BatteryPercent);
        Assert.Equal(0, s.SdStatus);
        Assert.False(s.Busy);
    }
}
