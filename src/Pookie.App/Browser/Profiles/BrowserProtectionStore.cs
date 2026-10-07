using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pookie.SoundCloud;

namespace Pookie.App.Browser;

// Exported session metadata, not a native cookie backup. Contains no OAuth token.
// NativeBrowserSession never restores it into website storage: the browser owns
// cookie attributes and expiration. Binding prevents reuse by another account.
internal sealed class BrowserProtectionStore(string profilePath)
{
    private readonly string file = Path.Combine(profilePath, "protection.json");
    private static string Binding(WebSession account) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(account.ClientId + "\n" + account.UserAgent + "\n" + account.AccessToken)));

    public void Restore(WebSession account)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 10000) return;
            var saved = JsonSerializer.Deserialize(File.ReadAllText(file), BrowserProtectionJson.Default.BrowserProtection);
            if (saved == null || saved.Binding != Binding(account) || saved.UpdatedUtc < DateTimeOffset.UtcNow.AddYears(-1)) return;
            account.UpdateDataDomeClientId(saved.Value);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public void Save(WebSession account, string value)
    {
        if (!(account with { DataDomeClientId = value }).IsValid()) return;
        var temporary = file + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new BrowserProtection(Binding(account), value, DateTimeOffset.UtcNow),
                BrowserProtectionJson.Default.BrowserProtection));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}

internal sealed record BrowserProtection(string Binding, string Value, DateTimeOffset UpdatedUtc);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(BrowserProtection))]
internal partial class BrowserProtectionJson : JsonSerializerContext;
