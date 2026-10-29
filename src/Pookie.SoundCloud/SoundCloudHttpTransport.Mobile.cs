using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pookie.SoundCloud;

public sealed partial class SoundCloudHttpTransport
{
    private readonly SemaphoreSlim telemetry = new(1, 1);
    private readonly object eventGate = new();
    private readonly Queue<long> responseEvents = new();
    private DateTimeOffset? lastTelemetry;

    private async Task RecordMobileResponseAsync(Uri uri, WebSession account, CancellationToken token, bool allowSend)
    {
        if (account.MobileIdentity is not { Signals: { } signals } mobile) return;
        lock (configurationGate)
            if (configuration is { IsCompletedSuccessfully: true } && configuration.Result.Enabled("datadome") == false) return;
        lock (eventGate)
        {
            if (responseEvents.Count < 80) responseEvents.Enqueue(clock.GetUtcNow().ToUnixTimeMilliseconds());
        }
        if (!allowSend) return; // Android processes a challenge through WebView, independently of the telemetry POST.
        if (!await telemetry.WaitAsync(0, token).ConfigureAwait(false)) return;
        try
        {
            long[] events;
            lock (eventGate)
            {
                var interval = signals.AdvancedCollectionEnabled ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(10);
                if (lastTelemetry is { } last && (clock.GetUtcNow() - last < interval || responseEvents.Count < 5)) return;
                events = responseEvents.ToArray();
            }
            lastTelemetry = clock.GetUtcNow();
            var serializedEvents = Encoding.UTF8.GetString(Json(writer =>
            {
                writer.WriteStartArray();
                foreach (var date in events)
                {
                    writer.WriteStartObject(); writer.WriteNumber("id", 1); writer.WriteString("message", "response validation");
                    writer.WriteString("source", "sdk"); writer.WriteNumber("date", date); writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }));
            var fields = new Dictionary<string, string>
            {
                ["cid"] = account.DataDomeClientId ?? "", ["ddk"] = signals.DataDomeClientKey,
                ["request"] = WithContext(uri, account).AbsoluteUri, ["ua"] = mobile.UserAgent,
                ["events"] = serializedEvents, ["inte"] = "android-java-manual", ["ddv"] = "3.0.14",
                ["os"] = "Android", ["osr"] = mobile.AndroidVersion, ["osn"] = signals.AndroidCodename,
                ["osv"] = signals.SdkVersion.ToString(CultureInfo.InvariantCulture),
                ["camera"] = signals.Camera,
                ["mdl"] = mobile.Model, ["prd"] = signals.Product, ["mnf"] = mobile.Manufacturer, ["dev"] = signals.Device,
                ["hrd"] = signals.Hardware, ["fgp"] = signals.BuildFingerprint, ["tgs"] = signals.BuildTags, ["d_ifv"] = mobile.AndroidId
            };
            if (!signals.AdvancedCollectionEnabled)
            {
                fields.Add("ddvc", mobile.AppVersion);
                fields.Add("screen_x", signals.ScreenWidth.ToString(CultureInfo.InvariantCulture));
                fields.Add("screen_y", signals.ScreenHeight.ToString(CultureInfo.InvariantCulture));
                fields.Add("screen_d", signals.ScreenDensity.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                fields.Add("ddvv", mobile.AppVersion);
                fields.Add("d_s_is_w", signals.ScreenWidth.ToString(CultureInfo.InvariantCulture));
                fields.Add("d_s_is_h", signals.ScreenHeight.ToString(CultureInfo.InvariantCulture));
                if (signals.Advanced != null) foreach (var pair in signals.Advanced) fields[pair.Key] = pair.Value;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api-sdk.datadome.co/sdk/")
            {
                Content = new FormUrlEncodedContent(fields), Version = System.Net.HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(1)); // Same independent telemetry deadline as the Android SDK.
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                Observed?.Invoke(new("POST", "/sdk/", (int)response.StatusCode) { Host = "api-sdk.datadome.co" });
                if (!response.IsSuccessStatusCode) return;
                using var json = JsonDocument.Parse(await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false));
                if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("cookie", out var cookie) &&
                    cookie.ValueKind == JsonValueKind.String && cookie.GetString() is { } value)
                {
                    if (value.StartsWith("datadome=", StringComparison.OrdinalIgnoreCase)) UpdateProtection(account, value[9..].Split(';', 2)[0]);
                    lock (eventGate)
                        for (var index = 0; index < events.Length && responseEvents.Count > 0; index++) responseEvents.Dequeue();
                }
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or SoundCloudException ||
                error is OperationCanceledException && !token.IsCancellationRequested)
            {
                // Telemetry failure is observable but does not discard a valid API
                // response. It also never marks a blocked request as verified.
                Observed?.Invoke(new("POST", "/sdk/", 0) { Host = "api-sdk.datadome.co" });
            }
        }
        finally { telemetry.Release(); }
    }
}
