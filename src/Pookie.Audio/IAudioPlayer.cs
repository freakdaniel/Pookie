namespace Pookie.Audio;

public enum AudioTransport { File, Progressive, Hls, WidevineHls }

public sealed record AudioSource(string Location, AudioTransport Transport, double Duration = 0)
{
    public string? LicenseAuthToken { get; init; }
    public override string ToString() => $"Audio source ({Transport}, location redacted)";
}

public sealed record AudioState(double Position, double Duration, bool Playing, bool Buffering, bool Ended)
{
    // The available interval around the current position, in track seconds.
    // Seeking may discard earlier data; the interval need not begin at zero.
    public double BufferedStart { get; init; }
    public double BufferedEnd { get; init; }
}

public interface IAudioPlayer : IDisposable, IAsyncDisposable
{
    Task PlayAsync(AudioSource source, CancellationToken cancellationToken = default);
    Task SeekAsync(double seconds, CancellationToken cancellationToken = default);
    void Stop();
    void Pause(bool paused);
    void Volume(double percent);
    AudioState Poll();
}
