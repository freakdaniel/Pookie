using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pookie.Audio;

internal sealed record WidevinePlatform(string Os, string Architecture, string Rid, string Library);

internal sealed record WidevinePackage(string Version, string Rid, Uri Url, int Size, string Sha256, string Library)
{
    private const string AppId = "oimompecagnajdejgnnjijobebaeigek";

    internal static WidevinePlatform CurrentPlatform() => ForPlatform(
        OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsWindows() ? "windows" : "macos",
        RuntimeInformation.ProcessArchitecture);

    internal static WidevinePlatform ForPlatform(string os, Architecture architecture) => (os, architecture) switch
    {
        ("linux", Architecture.X64) => new("linux", "x64", "linux-x64", "_platform_specific/linux_x64/libwidevinecdm.so"),
        ("windows", Architecture.X64) => new("win", "x64", "win-x64", "_platform_specific/win_x64/widevinecdm.dll"),
        ("windows", Architecture.Arm64) => new("win", "arm64", "win-arm64", "_platform_specific/win_arm64/widevinecdm.dll"),
        ("macos", Architecture.X64) => new("mac", "x64", "osx-x64", "_platform_specific/mac_x64/libwidevinecdm.dylib"),
        ("macos", Architecture.Arm64) => new("mac", "arm64", "osx-arm64", "_platform_specific/mac_arm64/libwidevinecdm.dylib"),
        _ => throw new DrmPlaybackException("Для этой ОС и архитектуры нет настроенного пакета Widevine. Обычные треки доступны без него.")
    };

    internal static async Task<WidevinePackage> LatestAsync(HttpClient http, WidevinePlatform platform, CancellationToken token)
    {
        // Google's component updater protocol, also used by Kodi InputStream Helper.
        // No Firefox metadata, browser identity, cookies or playback credentials.
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://update.googleapis.com/service/update2/json")
        {
            Content = RequestContent(platform)
        };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var bytes = await HttpRangeStream.ReadBoundedAsync(response.Content, 128 * 1024, token);
        return ParseResponse(Encoding.UTF8.GetString(bytes), platform);
    }

    private static HttpContent RequestContent(WidevinePlatform platform)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject(); json.WriteStartObject("request");
            json.WriteString("protocol", "4.0"); json.WriteString("updaterversion", "151.0.7922.173");
            json.WriteString("acceptformat", "crx3,download,puff,run,xz,zucc");
            json.WriteString("arch", platform.Architecture);
            json.WriteStartObject("os");
            json.WriteString("arch", platform.Architecture); json.WriteString("platform", platform.Os);
            json.WriteEndObject();
            json.WriteStartArray("apps"); json.WriteStartObject();
            json.WriteString("appid", AppId); json.WriteString("version", "0.0.0.0");
            json.WriteString("installsource", "ondemand"); json.WriteStartObject("updatecheck"); json.WriteEndObject();
            json.WriteEndObject(); json.WriteEndArray(); json.WriteEndObject(); json.WriteEndObject();
        }
        return new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json");
    }

    internal static WidevinePackage ParseResponse(string text, WidevinePlatform platform)
    {
        // The updater prefixes JSON with an XSSI guard.
        using var document = JsonDocument.Parse(text.StartsWith(")]}'", StringComparison.Ordinal) ? text[4..] : text);
        var app = document.RootElement.GetProperty("response").GetProperty("apps").EnumerateArray()
            .Single(item => item.GetProperty("appid").GetString() == AppId);
        var update = app.GetProperty("updatecheck");
        if (app.GetProperty("status").GetString() != "ok" || update.GetProperty("status").GetString() != "ok")
            throw new InvalidDataException("Google did not offer a Widevine component for this platform.");
        foreach (var pipeline in update.GetProperty("pipelines").EnumerateArray())
        {
            var operations = pipeline.GetProperty("operations");
            // Accept a complete CRX3, never a differential/patch pipeline.
            if (operations.GetArrayLength() != 2 || operations[0].GetProperty("type").GetString() != "download" ||
                operations[1].GetProperty("type").GetString() != "crx3") continue;
            var download = operations[0];
            var hash = download.GetProperty("out").GetProperty("sha256").GetString()!;
            if (operations[1].GetProperty("in").GetProperty("sha256").GetString() != hash) continue;
            foreach (var location in download.GetProperty("urls").EnumerateArray())
            {
                if (!Uri.TryCreate(location.GetProperty("url").GetString(), UriKind.Absolute, out var url) || !AllowedDownload(url)) continue;
                var package = new WidevinePackage(update.GetProperty("nextversion").GetString()!, platform.Rid,
                    url, download.GetProperty("size").GetInt32(), hash, platform.Library);
                package.Validate();
                return package;
            }
        }
        throw new InvalidDataException("Google did not offer a supported HTTPS Widevine archive.");
    }

    internal void Validate()
    {
        if (!System.Version.TryParse(Version, out _) || string.IsNullOrEmpty(Rid) || Rid.Contains('/') || Rid.Contains('\\') || Rid is "." or ".." ||
            Url == null || !AllowedDownload(Url) || Size is < 12 or > 64 * 1024 * 1024 || Sha256 == null || Sha256.Length != 64 ||
            Sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            string.IsNullOrEmpty(Library) || Library.Contains('\\') || Path.IsPathRooted(Library) || Library.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Invalid Widevine package descriptor.");
    }

    internal static bool AllowedDownload(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && (uri.Host == "www.google.com" || uri.Host == "dl.google.com" ||
            uri.Host.EndsWith(".gvt1.com", StringComparison.OrdinalIgnoreCase));
}

[JsonSerializable(typeof(WidevinePackage))]
internal sealed partial class WidevinePackageJson : JsonSerializerContext;
