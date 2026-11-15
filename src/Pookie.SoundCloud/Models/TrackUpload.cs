namespace Pookie.SoundCloud;

// The original upload, independent of the compressed playback transcodings.
public sealed record TrackUpload(string? Codec, string? Container, long? BitRate, long? SampleRate, long? Channels)
{
    public bool IsLossless => Codec?.Trim().ToLowerInvariant() is { } codec &&
        (codec is "aif" or "aiff" or "flac" or "alac" or "wav" or "m4a" or "caf" ||
         codec.StartsWith("pcm", StringComparison.Ordinal));
}
