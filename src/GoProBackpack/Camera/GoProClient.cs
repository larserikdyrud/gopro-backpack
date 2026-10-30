using System.Net;
using System.Text.Json;

namespace GoProBackpack.Camera;

/// <summary>
/// Thin Open GoPro HTTP calls on port 8080. Never throws out of the loops: every call
/// returns null/false on failure.
/// </summary>
public sealed class GoProClient(HttpClient http, ILogger<GoProClient> logger)
{
    public IPAddress? Address { get; set; }

    public async Task<Dictionary<int, int>?> GetStatusesAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync("/gopro/camera/state", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("status", out var status))
            return null;

        var result = new Dictionary<int, int>();
        foreach (var p in status.EnumerateObject())
        {
            if (int.TryParse(p.Name, out var id) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var v))
                result[id] = v;
        }
        return result;
    }

    public Task<bool> EnableWiredUsbControlAsync(CancellationToken ct) => GetAsync("/gopro/camera/control/wired_usb?p=1", ct);
    public Task<bool> ShutterStartAsync(CancellationToken ct) => GetAsync("/gopro/camera/shutter/start", ct);
    public Task<bool> ShutterStopAsync(CancellationToken ct) => GetAsync("/gopro/camera/shutter/stop", ct);
    public Task<bool> HilightAsync(CancellationToken ct) => GetAsync("/gopro/media/hilight/moment", ct);
    public Task<bool> KeepAliveAsync(CancellationToken ct) => GetAsync("/gopro/camera/keep_alive", ct);
    public Task<bool> WebcamExitAsync(CancellationToken ct) => GetAsync("/gopro/webcam/exit", ct);

    public Task<bool> SetSettingAsync(int setting, int option, CancellationToken ct) =>
        GetAsync($"/gopro/camera/setting?setting={setting}&option={option}", ct);

    /// <summary>Webcam status; 0 = off. Null if the camera did not answer.</summary>
    public async Task<int?> GetWebcamStatusAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync("/gopro/webcam/status", ct);
        return doc is not null && doc.RootElement.TryGetProperty("status", out var s) && s.TryGetInt32(out var v) ? v : null;
    }

    private async Task<bool> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(Uri(path), ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogDebug("GET {Path} failed: {Message}", path, ex.Message);
            return false;
        }
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(Uri(path), ct);
            if (!response.IsSuccessStatusCode)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            logger.LogDebug("GET {Path} failed: {Message}", path, ex.Message);
            return null;
        }
    }

    private Uri Uri(string path) =>
        new($"http://{Address ?? throw new InvalidOperationException("Camera address unknown")}:8080{path}");
}
