using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyPlaylistDialogPolishAsync(SoundCloudTrack track, CollectionFixtureTransport transport)
    {
        transport.PickerCount = 4; await OpenPlaylistPickerAsync(track);
        await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing && !playlistPickerLoading.Skeleton.IsVisible);
        ScrollViewer? scroll = null;
        VisualTree.Visit(playlistPickerLoading.Root, child => { if (child is ScrollViewer viewer) scroll = viewer; });
        if (scroll == null || SmoothScroll.Maximum(scroll) > .5) throw new InvalidOperationException("Four playlists have an unexpected resting scrollbar.");
        var viewport = scroll.ViewportHeight; var sampled = 0;
        void CheckViewport()
        {
            if (!playlistDialogPages.Animating) return;
            sampled++;
            if (playlistDialogPages.ContentOverlaps)
                throw new InvalidOperationException("The two dialog pages painted text/buttons simultaneously.");
            if (Math.Abs(scroll.ViewportHeight - viewport) > .5 || SmoothScroll.Maximum(scroll) > .5)
                throw new InvalidOperationException("Switching dialog pages created a transient playlist scrollbar.");
        }
        Window.FrameRendered += CheckViewport;
        try
        {
            ShowPlaylistCreation(true);
            await WaitForLikedLayoutAsync(() => !playlistDialogPages.Animating && !playlistDialogContent.Resizing);
            if (sampled < 3 || Math.Abs(playlistDialogContent.BodyBounds.Bottom - playlistCreateSave.Bounds.Bottom - 24) > 1)
                throw new InvalidOperationException("Creation did not animate or retained empty space beneath its submit button.");
            VisualTree.Visit(playlistCreateForm, child =>
            { if (child is TextBlock { Text: "Доступ" }) throw new InvalidOperationException("The redundant privacy heading remains."); });
            var icons = new HashSet<object>();
            VisualTree.Visit(playlistCreatePrivacy, child => { if (child is Image { Source: { } source }) icons.Add(source); });
            if (!icons.Contains(Icons.Source("globe", TrackButtons.OnSurface)) || !icons.Contains(Icons.Source("lock-simple", TrackButtons.OnSurface)))
                throw new InvalidOperationException("The privacy selector lost its globe/lock icons.");
            var left = playlistCreatePrivacy.SelectionBounds.X;
            SetPlaylistPrivacy(true);
            await WaitForLikedLayoutAsync(() => playlistCreatePrivacy.Animating && playlistCreatePrivacy.SelectionBounds.X > left + 1);
            CaptureUiPreview("dialog-privacy-moving");
            await WaitForLikedLayoutAsync(() => !playlistCreatePrivacy.Animating);
            if (playlistCreatePrivacy.SelectionBounds.X <= left || playlistCreatePublic.StyleName != TrackButtons.Text || playlistCreatePrivate.StyleName != TrackButtons.Text)
                throw new InvalidOperationException("Privacy did not slide one neutral pill between transparent tabs.");
            CaptureUiPreview("dialog-create-compact");
            ShowPlaylistCreation(false);
            await WaitForLikedLayoutAsync(() => !playlistDialogPages.Animating && !playlistDialogContent.Resizing);
        }
        finally { Window.FrameRendered -= CheckViewport; }
        await VerifyOverlayCloseBlurAsync();
        transport.PickerCount = 2; await OpenPlaylistPickerAsync(track);
        await WaitForLikedLayoutAsync(() => !playlistDialogContent.Resizing);
        Console.WriteLine("PLAYLIST_DIALOG_POLISH_OK: stable viewport through both transitions, measured compact form, no privacy heading, moving neutral pill/icons and bounded pixel-verified close-button blur");
    }

    private async Task VerifyOverlayCloseBlurAsync()
    {
        var bands = new OverlayGlassBands().Width(40).Height(40).Right().Top().Margin(0, 12, 24, 0).IsHitTestVisible(false);
        workspace.Add(bands);
        var frames = 0;
        void Count() { frames++; if (frames < 4) Window.InvalidateVisual(); }
        Window.FrameRendered += Count;
        try
        {
            await WaitForLikedLayoutAsync(() => frames >= 4 && ((MaterialTrackButton)playlistPickerClose).HasOverlayBlur);
            var button = (MaterialTrackButton)playlistPickerClose;
            if (button.OverlayBlurBounds.Width > 90 || button.OverlayBlurBounds.Height > 90)
                throw new InvalidOperationException("Overlay glass captured more than the button and its halo.");
            using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(20, 3, 1));
            using var renderer = Window.GraphicsFactory.CreateContext(surface);
            renderer.BeginFrame(surface);
            using var viewport = WindowPresentation.CaptureViewport(surface);
            renderer.Clear(Color.Black); renderer.Translate(-button.Bounds.X - 10, -button.Bounds.Y - 7);
            button.Render(renderer); renderer.EndFrame();
            var pixels = ((ICpuPixelSurface)surface).CopyPixels();
            var values = Enumerable.Range(0, 20).Select(i => (int)pixels[i * 4]).ToArray();
            if (values.Max() - values.Min() > 25) throw new InvalidOperationException("Sharp background stripes survived inside the close button's glass.");
            CaptureUiPreview("dialog-close-blurred");
        }
        finally { Window.FrameRendered -= Count; workspace.Remove(bands); bands.Dispose(); }
    }
    private sealed class OverlayGlassBands : Control
    {
        protected override void OnRender(IGraphicsContext context)
        {
            for (var x = 0; x < Bounds.Width; x += 2)
                context.FillRectangle(new(Bounds.X + x, Bounds.Y, 2, Bounds.Height), x % 4 == 0 ? Color.White : Color.Black);
        }
    }
}
