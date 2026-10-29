using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pookie.SoundCloud;

// A mobile session must carry its own device identity and client credentials.
// These are supplied by the session owner, never inferred from a desktop web token.
public sealed record SoundCloudMobileIdentity(string AndroidId, string Manufacturer, string Model, string AndroidVersion,
    int AppBuild = 377050, string AppVersion = "2026.10.14-release")
{
    public SoundCloudMobileSignals? Signals { get; init; }
    public bool IsTablet { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? RequestedFeatures { get; init; }
    public string FeaturesHeader => string.Join(',', (RequestedFeatures?.Split(',') ?? [])
        .Where(value => !value.StartsWith("system_playlist_in_library=", StringComparison.Ordinal))
        .Append("system_playlist_in_library=true"));
    public bool IsValid() => AndroidId is { Length: 16 } && AndroidId.All(char.IsAsciiHexDigit) &&
        ValidText(Manufacturer) && ValidText(Model) && ValidText(AndroidVersion) && ValidText(AppVersion) && AppBuild > 0 &&
        (Signals == null || Signals.IsValid()) &&
        (AcceptLanguage == null || AcceptLanguage is { Length: > 0 and <= 35 } && AcceptLanguage.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) &&
        ValidFeatures(RequestedFeatures);

    private static bool ValidFeatures(string? value)
    {
        if (value == null) return true;
        if (value.Length is 0 or > 4096) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in value.Split(','))
        {
            var parts = feature.Split('=');
            if (parts.Length != 2 || parts[0].Length == 0 || !parts[0].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
                parts[1] is not ("true" or "false") || !names.Add(parts[0])) return false;
        }
        return true;
    }

    // MD5 is the Android client's wire format for UDID, not a security primitive.
    public string Udid => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(AndroidId)));
    public string UserAgent
    {
        get
        {
            var device = Model.Contains(Manufacturer, StringComparison.OrdinalIgnoreCase) ? Model : Manufacturer + " " + Model;
            device = new string(device.Normalize(NormalizationForm.FormD).Where(c => c <= 127).ToArray());
            var parts = AndroidVersion.Split('.').Take(3).Select(part => new string(part.Where(char.IsAsciiDigit).ToArray())).ToList();
            while (parts.Count < 3) parts.Add("0");
            return $"SoundCloud/{AppVersion} (Android {string.Join('.', parts.Select(part => part.Length == 0 ? "0" : part))}; {device})";
        }
    }

    internal string BuildString => AppBuild.ToString(CultureInfo.InvariantCulture);
    private static bool ValidText(string? value) => value is { Length: > 0 and <= 256 } && !value.Any(char.IsControl);
    public override string ToString() => "SoundCloud mobile identity (device identifier redacted)";
}

// Optional snapshot supplied with the mobile session. Desktop hardware is never
// reported as Android hardware, and no camera or touch data is invented.
public sealed record SoundCloudMobileSignals(string Product, string Device, string Hardware, string BuildFingerprint,
    string BuildTags, string AndroidCodename, int SdkVersion, int ScreenWidth, int ScreenHeight, double ScreenDensity, string Camera)
{
    // Exported Android SDK fields only. No Windows sensor values are presented
    // as measurements from Android; absent fields remain absent.
    public Dictionary<string, string>? Advanced { get; init; }
    // The Android SDK selects a provider family explicitly. Merely attaching
    // extra exported fields must not switch schema or telemetry cadence.
    public bool AdvancedCollectionEnabled { get; init; }
    private static readonly HashSet<string> AdvancedNames = new(StringComparer.Ordinal)
    {
        "d_name", "d_board", "d_uii", "os_bid", "os_btype", "d_s_is_w", "d_s_is_h", "d_s_dpi", "d_s_mrr", "d_s_cutout",
        "d_or", "d_s_n", "d_s_hdr", "d_s_br", "d_p_thermal", "d_p_idle", "d_p_lidle", "d_p_low", "d_p_save",
        "d_b_state", "d_b_c", "d_b_lvl", "d_b_e", "d_b_ttfc", "d_n_type", "a_debuggable", "a_debugger", "a_debug", "r_up", "r_lc"
    };
    public string DataDomeClientKey { get; init; } = "7FC6D561817844F25B65CDD97F28A1";
    public bool Equals(SoundCloudMobileSignals? other) => ReferenceEquals(this, other) || other != null &&
        Product == other.Product && Device == other.Device && Hardware == other.Hardware && BuildFingerprint == other.BuildFingerprint &&
        BuildTags == other.BuildTags && AndroidCodename == other.AndroidCodename && SdkVersion == other.SdkVersion && ScreenWidth == other.ScreenWidth &&
        AdvancedCollectionEnabled == other.AdvancedCollectionEnabled &&
        ScreenHeight == other.ScreenHeight && ScreenDensity == other.ScreenDensity && Camera == other.Camera && DataDomeClientKey == other.DataDomeClientKey &&
        (Advanced == null ? other.Advanced == null : other.Advanced != null && Advanced.Count == other.Advanced.Count &&
            Advanced.All(pair => other.Advanced.TryGetValue(pair.Key, out var value) && pair.Value == value));
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Product); hash.Add(Device); hash.Add(Hardware); hash.Add(BuildFingerprint); hash.Add(BuildTags);
        hash.Add(AndroidCodename); hash.Add(SdkVersion); hash.Add(ScreenWidth); hash.Add(ScreenHeight); hash.Add(ScreenDensity); hash.Add(Camera); hash.Add(DataDomeClientKey); hash.Add(AdvancedCollectionEnabled);
        if (Advanced != null) foreach (var pair in Advanced.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { hash.Add(pair.Key); hash.Add(pair.Value); }
        return hash.ToHashCode();
    }
    public bool IsValid() => new[] { Product, Device, Hardware, BuildFingerprint, BuildTags, AndroidCodename, Camera }
        .All(value => value is { Length: > 0 and <= 8192 } && !value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) &&
        SdkVersion is > 0 and < 1000 && ScreenWidth is > 0 and <= 32768 && ScreenHeight is > 0 and <= 32768 &&
        double.IsFinite(ScreenDensity) && ScreenDensity is > 0 and <= 100 &&
        DataDomeClientKey is { Length: > 0 and <= 256 } && DataDomeClientKey.All(char.IsAsciiLetterOrDigit) &&
        (Advanced == null || AdvancedCollectionEnabled && Advanced.Count is > 0 and <= 32 && Advanced.All(pair => AdvancedNames.Contains(pair.Key) &&
            pair.Value is { Length: > 0 and <= 8192 } && !pair.Value.Any(char.IsControl)));
    public override string ToString() => "SoundCloud mobile signals (device data redacted)";
}
