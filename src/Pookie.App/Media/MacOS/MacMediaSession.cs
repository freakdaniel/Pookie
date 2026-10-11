using System.Runtime.InteropServices;
using Pookie.Logging;
using Serilog.Events;

namespace Pookie.Media.MacOS;

internal sealed class MacMediaSession : IMediaSession
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint RemoteHandler(nint block, nint commandEvent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ArtworkHandler(nint block, ObjC.Size size);
    private readonly Action<Action> dispatch;
    private readonly nint center;
    private readonly List<(nint Command, nint Target, ObjC.Block Block)> targets = [];
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private CancellationTokenSource? artworkRequest;
    private MediaSnapshot? snapshot;
    private nint image, artwork;
    private ObjC.Block? artworkBlock;
    private bool disposed;
    public event Action<MediaCommand>? Command;

    public MacMediaSession(Action<Action> dispatch)
    {
        this.dispatch = dispatch;
        using var pool = new ObjC.Pool();
        center = ObjC.Send(ObjC.objc_getClass("MPNowPlayingInfoCenter"), ObjC.S("defaultCenter"));
        if (center == 0) throw new PlatformNotSupportedException("macOS Now Playing is unavailable.");
        var remote = ObjC.Send(ObjC.objc_getClass("MPRemoteCommandCenter"), ObjC.S("sharedCommandCenter"));
        Add(remote, "playCommand", MediaAction.Play); Add(remote, "pauseCommand", MediaAction.Pause);
        Add(remote, "togglePlayPauseCommand", MediaAction.Toggle); Add(remote, "stopCommand", MediaAction.Stop);
        Add(remote, "nextTrackCommand", MediaAction.Next); Add(remote, "previousTrackCommand", MediaAction.Previous);
        Add(remote, "changePlaybackPositionCommand", MediaAction.Seek, "positionTime");
        Clear();
    }
    private void Add(nint remote, string selector, MediaAction action, string? valueSelector = null)
    {
        var command = ObjC.Send(remote, ObjC.S(selector));
        RemoteHandler callback = (_, commandEvent) =>
        {
            if (disposed) return 200;
            var value = valueSelector == null ? 0 : ObjC.Double(commandEvent, ObjC.S(valueSelector));
            Command?.Invoke(new(action, value));
            return 0;
        };
        var block = new ObjC.Block(callback);
        var target = ObjC.Send(command, ObjC.S("addTargetWithHandler:"), block.Pointer);
        ObjC.Send(target, ObjC.S("retain"));
        ObjC.SetBool(command, ObjC.S("setEnabled:"), false);
        targets.Add((command, target, block));
    }
    public void Update(MediaSnapshot state)
    {
        if (disposed) return;
        using var pool = new ObjC.Pool();
        var old = snapshot;
        snapshot = state;
        if (old?.ArtworkUrl != state.ArtworkUrl || old?.TrackId != state.TrackId)
        {
            ResetArtwork();
            if (Uri.TryCreate(state.ArtworkUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            {
                artworkRequest = new();
                _ = FetchArtworkAsync(uri, state.TrackId, artworkRequest.Token);
            }
        }
        Publish();
        for (var i = 0; i < targets.Count; i++)
            ObjC.SetBool(targets[i].Command, ObjC.S("setEnabled:"), i switch
            { 4 => state.CanNext, 5 => state.CanPrevious, 6 => state.CanSeek, _ => true });
    }
    private void Publish()
    {
        if (snapshot is not { } state || disposed) return;
        using var pool = new ObjC.Pool();
        var values = ObjC.Send(ObjC.objc_getClass("NSMutableDictionary"), ObjC.S("dictionary"));
        void Text(string key, string value) => ObjC.DictionarySet(values, ObjC.S("setObject:forKey:"), ObjC.String(value), ObjC.Constant(key));
        void Number(string key, double value) => ObjC.DictionarySet(values, ObjC.S("setObject:forKey:"),
            ObjC.Number(ObjC.objc_getClass("NSNumber"), ObjC.S("numberWithDouble:"), value), ObjC.Constant(key));
        Text("MPMediaItemPropertyTitle", state.Title); Text("MPMediaItemPropertyArtist", state.Artist);
        Number("MPMediaItemPropertyPlaybackDuration", state.Duration);
        Number("MPNowPlayingInfoPropertyElapsedPlaybackTime", state.Position);
        Number("MPNowPlayingInfoPropertyPlaybackRate", state.Playback == MediaPlayback.Playing ? 1 : 0);
        if (artwork != 0) ObjC.DictionarySet(values, ObjC.S("setObject:forKey:"), artwork, ObjC.Constant("MPMediaItemPropertyArtwork"));
        ObjC.Set(center, ObjC.S("setNowPlayingInfo:"), values);
        ObjC.SetInt(center, ObjC.S("setPlaybackState:"), state.Playback == MediaPlayback.Playing ? 1 : state.Playback == MediaPlayback.Stopped ? 3 : 2);
    }
    private async Task FetchArtworkAsync(Uri uri, string id, CancellationToken token)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) return;
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[16384];
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + count > 4 * 1024 * 1024) return;
                output.Write(buffer, 0, count);
            }
            var bytes = output.ToArray();
            token.ThrowIfCancellationRequested();
            dispatch(() =>
            {
                if (disposed || token.IsCancellationRequested || snapshot?.TrackId != id) return;
                using var pool = new ObjC.Pool();
                var data = ObjC.Data(ObjC.objc_getClass("NSData"), ObjC.S("dataWithBytes:length:"), bytes, (nuint)bytes.Length);
                image = ObjC.Send(ObjC.Send(ObjC.objc_getClass("NSImage"), ObjC.S("alloc")), ObjC.S("initWithData:"), data);
                if (image == 0) return;
                var capturedImage = image;
                ObjC.Send(capturedImage, ObjC.S("retain"));
                ArtworkHandler callback = (_, _) => capturedImage;
                artworkBlock = new(callback, () => ObjC.Release(capturedImage));
                artwork = ObjC.Artwork(ObjC.Send(ObjC.objc_getClass("MPMediaItemArtwork"), ObjC.S("alloc")), ObjC.S("initWithBounds:requestHandler:"), new(512, 512), artworkBlock.Pointer);
                Publish();
            });
        }
        catch (OperationCanceledException) { AppLog.For("Pookie.Media").Debug("Загрузка системной обложки отменена"); }
        catch (Exception error) when (error is HttpRequestException or IOException)
        { AppLog.Failure("Pookie.Media", "Системная обложка недоступна", error, LogEventLevel.Debug); }
    }
    private void ResetArtwork()
    {
        artworkRequest?.Cancel(); artworkRequest?.Dispose(); artworkRequest = null;
        ObjC.Release(artwork); artwork = 0;
        artworkBlock?.Dispose(); artworkBlock = null;
        ObjC.Release(image); image = 0;
    }
    public void Clear()
    {
        if (disposed) return;
        using var pool = new ObjC.Pool();
        snapshot = null;
        ObjC.Set(center, ObjC.S("setNowPlayingInfo:"), 0);
        ObjC.SetInt(center, ObjC.S("setPlaybackState:"), 0);
        foreach (var target in targets) ObjC.SetBool(target.Command, ObjC.S("setEnabled:"), false);
        ResetArtwork();
    }
    public void Dispose()
    {
        if (disposed) return;
        Clear(); disposed = true;
        foreach (var target in targets)
        {
            ObjC.Set(target.Command, ObjC.S("removeTarget:"), target.Target);
            ObjC.Release(target.Target); target.Block.Dispose();
        }
        targets.Clear(); http.Dispose();
    }
}
