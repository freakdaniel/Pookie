using Pookie.Localization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed class ChatVideoFrame : Panel
{
    // Limit both network-independent decode work and retained native surfaces.
    private static readonly SemaphoreSlim slots = new(2, 2);
    internal const int AutoPlayBytes = 15 * 1024 * 1024;
    internal readonly Image Picture = new Image().StretchMode(Stretch.Uniform);
    private readonly Border badge, play;
    private readonly Image backdrop = new Image { StretchMode = Stretch.UniformToFill, Opacity = .6 };
    private readonly TextBlock duration;
    private readonly Size intrinsic;
    private readonly DispatcherTimer timer = new(TimeSpan.FromMilliseconds(66));
    private readonly Func<bool> visible, autoPlay;
    private readonly Func<byte[]?> data;
    private readonly bool eligible;
    private readonly Action<Task>? trackCleanup;
    private readonly CancellationTokenSource lifetime = new();
    private ChatVideoSession? session;
    private WriteableBitmap? bitmap;
    private long version;
    private bool disposed, starting, failed;
    internal bool PlayPromptVisible => play.IsVisible;
    internal bool Playing => session?.Snapshot().Playing == true;
    internal static bool CanAutoPlay(int bytes) => bytes is > 0 and <= AutoPlayBytes;
    internal ChatVideoFrame(ChatVideoInfo? info, int bytes, Func<bool> visible, Func<byte[]?> data, Action activate, Func<bool>? autoPlay = null,
        Action<Task>? trackCleanup = null)
    {
        this.visible = visible; this.data = data; this.autoPlay = autoPlay ?? (() => true);
        this.trackCleanup = trackCleanup;
        intrinsic = info is { Valid: true } ? new(info.Width, info.Height) : new(240, 180);
        eligible = CanAutoPlay(bytes);
        ClipToBounds = true; Cursor = CursorType.Hand;
        if (info is { Valid: true }) Picture.Source = ImageSource.FromBgraPixels(info.ThumbWidth, info.ThumbHeight, info.Pixels);
        duration = new TextBlock().Text(info?.DurationMs > 0 ? VideoTime(info.DurationMs) : UiText.Get("chats.media.video")).FontSize(11).Foreground(Color.White);
        badge = new Border().Background(Color.FromArgb(145, 0, 0, 0)).CornerRadius(10).BorderThickness(0).Padding(6, 3).IsHitTestVisible(false)
            .Child(new StackPanel().Horizontal().Spacing(4).Children(duration, Icons.View("speaker-slash", 12, Color.White).CenterVertical()));
        play = new Border().Background(Color.FromArgb(130, 0, 0, 0)).CornerRadius(22).BorderThickness(0).Width(44).Height(44).IsHitTestVisible(false)
            .Child(Icons.View("play-solid", 22, Color.White).Center());
        play.IsVisible = !eligible || !this.autoPlay();
        Add(backdrop); Add(Picture); Add(badge); Add(play);
        MouseDown += e => { if (e.Button == MouseButton.Left) { e.Handled = true; activate(); } };
        timer.Tick += Tick; timer.Start();
    }
    internal ImageSource? FreezeFrame()
    {
        if (bitmap != null)
        {
            using var read = bitmap.LockForWrite(markDirtyOnDispose: false);
            return ImageSource.FromBgraPixels(bitmap.PixelWidth, bitmap.PixelHeight, read.PixelsBgra32.ToArray(), hasAlpha: false);
        }
        return Picture.Source as ImageSource;
    }
    internal void ShowPoster(ImageSource source, ImageSource blurred) { Picture.Source = source; backdrop.Source = blurred; }
    internal static string VideoTime(long ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(ms >= 3600000 ? @"h\:mm\:ss" : @"m\:ss");
    private async void Tick()
    {
        if (disposed || starting) return;
        if (!eligible || failed || !autoPlay() || !visible()) { StopPlayback(); play.IsVisible = !eligible || failed || !autoPlay(); return; }
        if (session == null)
        {
            if (!slots.Wait(0)) return;
            starting = true;
            var opening = StartPlaybackAsync();
            await opening;
        }
        if (session is not { } current) return;
        if (current.CopyFrame(ref bitmap, ref version)) { Picture.Source = bitmap; Picture.InvalidateVisual(); play.IsVisible = false; }
        var state = current.Snapshot();
        if (state.Failed) { failed = true; StopPlayback(); play.IsVisible = true; }
        else if (state.Ended) await current.SetPausedAsync(false);
        else if (state.Length > 0) duration.Text = VideoTime(Math.Max(0, state.Length - state.Time));
    }
    private async Task StartPlaybackAsync()
    {
        var ownsSlot = true;
        try
        {
            var bytes = data();
            if (bytes == null) return;
            using var startup = new AsyncResourceStartup<ChatVideoSession>(
                () => ChatVideoSession.StartAsync(bytes, silent: true, preview: true), lifetime.Token);
            ownsSlot = false;
            var completion = ReleaseUnclaimedSlotAsync(startup);
            TrackRelease(completion);
            await startup.Ready;
            if (disposed || !autoPlay() || !visible() || !startup.TryTake(out var started)) return;
            session = started;
        }
        catch { failed = true; if (!disposed) play.IsVisible = true; }
        finally { starting = false; if (ownsSlot) slots.Release(); }
    }
    private static async Task ReleaseUnclaimedSlotAsync(AsyncResourceStartup<ChatVideoSession> startup)
    {
        try { await startup.Completion.ConfigureAwait(false); }
        finally { if (!startup.Taken) slots.Release(); }
    }
    private void StopPlayback()
    {
        var old = session; session = null;
        if (old != null)
        {
            var closing = ReleaseAsync(old);
            TrackRelease(closing);
        }
        static async Task ReleaseAsync(ChatVideoSession old) { try { await old.DisposeAsync().ConfigureAwait(false); } finally { slots.Release(); } }
    }
    private void TrackRelease(Task closing)
    {
        if (trackCleanup != null) trackCleanup(closing);
        else _ = closing.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    internal void RefreshAutoPlay()
    {
        if (!autoPlay()) { StopPlayback(); play.IsVisible = true; }
    }
    internal void Stop()
    {
        if (disposed) return;
        disposed = true; lifetime.Cancel(); timer.Dispose(); StopPlayback(); Picture.Source = null; bitmap?.Dispose(); bitmap = null; lifetime.Dispose();
    }
    protected override Size MeasureContent(Size availableSize)
    {
        var size = ChatImagePreview.Fit(intrinsic, double.IsFinite(availableSize.Width) ? availableSize.Width : 300, 480);
        Picture.Measure(size); badge.Measure(size); play.Measure(size); return size;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        backdrop.Arrange(Bounds);
        var fitted = ChatImagePreview.Fit(intrinsic, finalSize.Width, 480);
        Picture.Arrange(new Rect(Bounds.X + (finalSize.Width - fitted.Width) / 2, Bounds.Y, fitted.Width, finalSize.Height));
        badge.Arrange(new Rect(Bounds.X + 6, Bounds.Y + 6, badge.DesiredSize.Width, badge.DesiredSize.Height));
        play.Arrange(new Rect(Bounds.X + (finalSize.Width - 44) / 2, Bounds.Y + (finalSize.Height - 44) / 2, 44, 44));
        return finalSize;
    }
    protected override void OnDispose() { Stop(); base.OnDispose(); }
}
