namespace Pookie.SoundCloud;

public static class SoundCloudMobileStreams
{
    public static SoundCloudTrack WithFallbacks(SoundCloudTrack track)
    {
        if (track.Id <= 0) throw new ArgumentOutOfRangeException(nameof(track));
        var streams = (track.Media?.Transcodings ?? []).ToList();
        foreach (var (path, protocol, mime) in new[] { ("https", "progressive", "audio/mpeg"), ("hls", "hls", "audio/mp4") })
        {
            var url = SoundCloudMobileRoutes.Uri($"/tracks/{SoundCloudMobileRoutes.Urn("tracks", track.Id)}/streams/{path}?secure=true&can_snip=false").AbsoluteUri;
            if (!streams.Any(stream => stream.Url == url)) streams.Add(new() { Url = url, Format = new() { Protocol = protocol, MimeType = mime } });
        }
        return track with { Media = new() { Transcodings = streams.ToArray() } };
    }
}
