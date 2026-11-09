using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

namespace Pookie.Audio;

internal static class PlayReadyLicense
{
    internal const int MaxMessageBytes = 65536;

    internal static byte[] Pssh(byte[] data)
    {
        if (data.Length >= 32 && data.Length <= MaxMessageBytes && data.AsSpan(4, 4).SequenceEqual("pssh"u8)) return data;
        if (data.Length is < 1 or > MaxMessageBytes - 32) throw new InvalidDataException("Invalid PlayReady initialization data length.");
        var pssh = new byte[32 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(pssh, pssh.Length);
        "pssh"u8.CopyTo(pssh.AsSpan(4));
        Convert.FromHexString("9a04f07998404286ab92e65be0885f95").CopyTo(pssh, 12);
        BinaryPrimitives.WriteInt32BigEndian(pssh.AsSpan(28), data.Length);
        data.CopyTo(pssh, 32);
        return pssh;
    }

    internal static async Task<byte[]> AcquireAsync(HttpClient http, string authorization, byte[] message, CancellationToken token)
    {
        if (authorization is not { Length: > 0 and <= 16384 } || authorization.Any(char.IsControl))
            throw new DrmPlaybackException("SoundCloud не предоставил авторизацию DRM-лицензии.");
        var xml = XDocument.Parse(Encoding.Unicode.GetString(message).Trim('\uFEFF', '\0'));
        var challenge = xml.Descendants().Single(node => node.Name.LocalName == "Challenge");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://license.media-streaming.soundcloud.cloud/playback/playready?license_token=" + Uri.EscapeDataString(authorization))
            { Content = new ByteArrayContent(Convert.FromBase64String(challenge.Value)) };
        request.Headers.Referrer = new("https://soundcloud.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://soundcloud.com");
        request.Headers.TryAddWithoutValidation("X-SC-Application-Id", "46941");
        foreach (var header in xml.Descendants().Where(node => node.Name.LocalName == "HttpHeader"))
        {
            var name = header.Elements().First(element => element.Name.LocalName == "name").Value;
            var value = header.Elements().First(element => element.Name.LocalName == "value").Value;
            if (name == "Content-Type") request.Content.Headers.TryAddWithoutValidation(name, value);
            if (name == "SOAPAction") request.Headers.TryAddWithoutValidation(name, value);
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new DrmPlaybackException($"SoundCloud не выдал лицензию PlayReady (HTTP {(int)response.StatusCode})." +
                await DrmLicenseFailure.ReadAsync(response, token).ConfigureAwait(false));
        return await HttpRangeStream.ReadBoundedAsync(response.Content, MaxMessageBytes, token).ConfigureAwait(false);
    }
}
