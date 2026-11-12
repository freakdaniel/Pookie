using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pookie.App.Storage;

namespace Pookie.Audio;

internal sealed class WidevineConsent(string root)
{
    internal string FilePath => Path.Combine(root, "consent.json");

    internal bool IsAccepted(string licenseText)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
            var json = document.RootElement;
            return json.GetProperty("schema").GetInt32() == 1 &&
                json.GetProperty("licenseSha256").GetString() == Fingerprint(licenseText) &&
                json.GetProperty("acceptedAtUtc").TryGetDateTimeOffset(out _);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException) { return false; }
    }

    internal void RequireAccepted(string licenseText)
    {
        if (!IsAccepted(licenseText)) throw new DrmPlaybackException("Для использования Widevine сначала прими условия в окне запуска Pookie.");
    }

    internal async Task AcceptAsync(string licenseText, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(licenseText)) throw new InvalidDataException("Widevine license is empty.");
        AppDataPaths.CreatePrivateDirectory(root);
        var temporary = Path.Combine(root, ".consent-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteNumber("schema", 1);
                json.WriteString("licenseSha256", Fingerprint(licenseText));
                json.WriteString("acceptedAtUtc", DateTimeOffset.UtcNow);
                json.WriteEndObject();
            }
            await File.WriteAllBytesAsync(temporary, buffer.ToArray(), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string Fingerprint(string text) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").Trim())));
}

internal sealed record WidevineCandidate(WidevinePackage Package, string LicenseText, byte[]? Archive);

internal sealed class WidevineSetup(WidevineConsent consent,
    Func<CancellationToken, Task<WidevineCandidate>> resolve,
    Func<WidevineCandidate, CancellationToken, Task<string>> prepare)
{
    internal WidevineConsent Consent { get; } = consent;
    internal WidevineCandidate? Candidate { get; private set; }
    internal bool IsAccepted => Candidate is { } candidate && Consent.IsAccepted(candidate.LicenseText);
    internal string LicenseText => Candidate?.LicenseText ?? throw new InvalidOperationException("Widevine terms have not been obtained.");

    internal Task ResolveAsync(CancellationToken token) => Task.Run(async () =>
        Candidate = await resolve(token).ConfigureAwait(false), token);

    internal Task AcceptAsync(CancellationToken token) => Task.Run(() => Consent.AcceptAsync(LicenseText, token), token);

    // Package verification, decompression and native/file initialization may
    // complete synchronously even behind an async API. Never run them on UI.
    internal Task<string> PrepareAsync(CancellationToken token) => Task.Run(async () =>
    {
        var candidate = Candidate ?? throw new DrmPlaybackException("Сначала нужно получить актуальные условия Widevine.");
        Consent.RequireAccepted(candidate.LicenseText);
        var library = await prepare(candidate, token).ConfigureAwait(false);
        Candidate = candidate with { Archive = null };
        return library;
    }, token);

    internal static WidevineSetup Default() => new(new(WidevineRuntime.DirectoryPath), WidevineRuntime.ResolveAsync,
        WidevineRuntime.InstallAsync);
}
