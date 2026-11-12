namespace Pookie.App.Hosting;

internal sealed record AppRunOptions
{
    // Windows protected playback uses the system PlayReady backend.
    // Widevine installation and consent belong to the Linux/macOS startup path.
    internal bool RequireWidevineSetup { get; init; } = !OperatingSystem.IsWindows();
    internal Pookie.Audio.WidevineSetup? WidevineSetupOverride { get; init; }
    public bool Preview { get; init; }
    public bool RequireSignIn { get; init; } = true;
    public bool SkipSessionRestore { get; init; }
    public bool SignInImmediately { get; init; }
    public bool AudioEnabled { get; init; } = true;
    public bool SilentAudio { get; init; }
    public bool DiscordPresence { get; init; } = true;
    public bool SystemMediaSession { get; init; } = true;
    public bool IsolatedData { get; init; }

    public static AppRunOptions FromArgs(string[] args) => new()
    {
        Preview = args.Contains("--demo"),
        RequireSignIn = !args.Contains("--demo"),
        SkipSessionRestore = args.Contains("--guest"),
        SignInImmediately = args.Contains("--sign-in"),
        AudioEnabled = !args.Contains("--no-audio"),
        SilentAudio = args.Contains("--silent-audio"),
        DiscordPresence = !args.Contains("--demo"),
        SystemMediaSession = !args.Contains("--no-media-session"),
        IsolatedData = args.Contains("--demo")
    };
}
