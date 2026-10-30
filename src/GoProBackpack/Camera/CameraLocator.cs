using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace GoProBackpack.Camera;

/// <summary>
/// Finds the camera's address on the USB (NCM) network. The camera hands the Pi an address
/// 172.2X.1YZ.x by DHCP, where XYZ are the last three digits of its serial; the camera is .51.
/// </summary>
public static partial class CameraLocator
{
    [GeneratedRegex(@"^172\.2\d\.1\d\d\.\d{1,3}$")]
    private static partial Regex UsbAddress();

    public static IPAddress? Locate(string? fallbackSerial)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && UsbAddress().IsMatch(ua.Address.ToString()))
                    return CameraAddressFor(ua.Address);
            }
        }
        return fallbackSerial is { Length: >= 3 } ? FromSerial(fallbackSerial) : null;
    }

    public static IPAddress CameraAddressFor(IPAddress local)
    {
        var b = local.GetAddressBytes();
        return new IPAddress([b[0], b[1], b[2], 51]);
    }

    public static IPAddress FromSerial(string serial)
    {
        var xyz = serial[^3..];
        return IPAddress.Parse($"172.2{xyz[0]}.1{xyz[1]}{xyz[2]}.51");
    }
}
