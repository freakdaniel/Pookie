using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyTrackQualityUiAsync(TrackPageFixtureBrowser browser)
    {
        await WaitForLikedLayoutAsync(() => trackDetailHd.IsVisible && !trackDetailHd.IsArrangeDirty &&
            trackDetailMetadataLine.MetadataOpacity == 1 && !trackDetailMetadataLine.IsMetadataRevealing);
        if (trackDetailAuthor.FontSize != 14 || trackDetailMeta.FontSize != trackDetailAuthor.FontSize ||
            Math.Abs(trackDetailAuthor.Bounds.Y - trackDetailMeta.Bounds.Y) > .5)
            throw new InvalidOperationException("Track author, genre and age have different font sizes or baselines.");
        var separator = trackDetailHdGroup.Children.OfType<TextBlock>().Single();
        if (separator.Text != "·" || separator.FontSize != trackDetailMeta.FontSize ||
            !trackDetailHdGroup.IsVisible || separator.Bounds.X < trackDetailMeta.Bounds.Right || separator.Bounds.Right >= trackDetailHd.Bounds.X)
            throw new InvalidOperationException("HD is missing its aligned metadata separator.");
        var hint = TooltipHints.TextFor(trackDetailHd);
        if (hint == null || !hint.Contains("без потерь") || !hint.Contains("PCM 24 бит") || !hint.Contains("44,1 кГц") || !hint.Contains("стерео"))
            throw new InvalidOperationException("HD tooltip omitted original-upload metadata.");
        Window.Activate(); MoveActionPointer(trackDetailHd.Bounds.Center);
        await WaitForLikedLayoutAsync(() => tooltipLayer.IsOpen && tooltipLayer.Reveal > .99);
        CaptureUiPreview("track-hd-tooltip", tooltipLayer);
        MoveActionPointer(new(2, 2));
        await WaitForLikedLayoutAsync(() => !tooltipLayer.IsVisible);

        var original = trackDetailState!;
        try
        {
            await VerifyTrackMetadataPublicationAsync(browser, original);
            foreach (var upload in new TrackUpload?[] { null, new("mp3", "mp3", 320000, 44100, 2), new("aac", "m4a", 256000, 44100, 2) })
            {
                trackDetailState = original with { Upload = upload };
                RenderTrackDetailQuality();
                await WaitForLoginFrameAsync();
                if (trackDetailHd.IsVisible || trackDetailHdGroup.IsVisible || TooltipHints.TextFor(trackDetailHd) != null)
                    throw new InvalidOperationException("Lossy or unknown upload was labeled lossless HD.");
            }
            trackDetailState = original;
            RenderTrackDetailQuality();
            var author = trackDetailAuthor.Text; var meta = trackDetailMeta.Text;
            try
            {
                trackDetailAuthor.Text = new string('W', 200);
                trackDetailMeta.Text = "· " + new string('M', 200);
                await WaitForLoginFrameAsync();
                if (trackDetailHd.Bounds.Left < trackDetailMeta.Bounds.Right - .5 ||
                    trackDetailHd.Bounds.Right > trackDetailMetadataLine.Bounds.Right + .5 ||
                    trackDetailAuthor.Bounds.Right > trackDetailMeta.Bounds.Left)
                    throw new InvalidOperationException("Long metadata overlapped or clipped the HD icon.");
            }
            finally { trackDetailAuthor.Text = author; trackDetailMeta.Text = meta; }

            browser.DelayedQuality = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = LoadTrackDetailMetadataAsync(original.Track.Id, navigationGeneration, lifetime.Token);
            trackDetailState = original with { Track = original.Track with { Id = original.Track.Id + 1 }, Upload = null, MetadataLoading = true };
            RenderTrackDetailHeader();
            browser.DelayedQuality.SetResult(); await pending;
            if (trackDetailState.Upload != null || trackDetailHd.IsVisible || trackDetailMetadataLine.MetadataOpacity != 0 ||
                trackDetailMetadataLine.IsMetadataRevealing || trackDetailAuthor.Opacity != 1)
                throw new InvalidOperationException("Late original-upload metadata labeled another track HD.");
        }
        finally
        {
            browser.DelayedQuality = null; browser.DelayTrack = 0; browser.FailQuality = false;
            trackDetailState = original; RenderTrackDetailHeader();
        }
        Console.WriteLine("TRACK_QUALITY_UI_OK: uniform 14px metadata, HD separator/tooltip, immediate author, synchronized metadata fade in either response order, no restart, quality failure fallback, unknown/lossy hidden, long-text ellipsis and stale response protection");
    }

    private async Task VerifyTrackMetadataPublicationAsync(TrackPageFixtureBrowser browser, TrackDetailState original)
    {
        var partialFrame = false;
        var authorHidden = false; var mismatchedOpacity = false; var restarted = false;
        var intermediateFrames = 0; var refreshDuringFade = true; var capturedFade = false;
        void AuditFrame()
        {
            if (!trackDetailMetadataLine.IsVisible || !trackDetailAuthor.IsVisible || trackDetailAuthor.Opacity != 1) authorHidden = true;
            var opacity = trackDetailMetadataLine.MetadataOpacity;
            if (opacity > 0 &&
                (trackDetailState!.MetadataLoading || !trackDetailHdGroup.IsVisible || !trackDetailHd.IsVisible)) partialFrame = true;
            if (opacity is > 0 and < 1)
            {
                intermediateFrames++;
                mismatchedOpacity |= trackDetailHdGroup.Opacity != opacity;
                if (refreshDuringFade && opacity is > .15 and < .9)
                {
                    refreshDuringFade = false;
                    RenderTrackDetailHeader();
                    restarted |= trackDetailMetadataLine.MetadataOpacity != opacity;
                }
                if (!capturedFade && opacity is > .15 and < .85)
                { capturedFade = true; CaptureUiPreview("track-metadata-fading"); }
            }
        }
        Window.FrameRendered += AuditFrame;
        try
        {
            foreach (var qualityLast in new[] { true, false })
            {
                browser.DelayedQuality = qualityLast ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
                browser.DelayTrack = qualityLast ? 0 : original.Track.Id;
                intermediateFrames = 0; refreshDuringFade = true;
                trackDetailState = original with { MetadataLoading = true, Upload = null };
                RenderTrackDetailHeader();
                var pending = LoadTrackDetailMetadataAsync(original.Track.Id, navigationGeneration, lifetime.Token);
                await WaitForLoginFrameAsync();
                if (pending.IsCompleted || !trackDetailState.MetadataLoading || !trackDetailMetadataLine.IsVisible ||
                    trackDetailAuthor.Opacity != 1 || trackDetailMetadataLine.MetadataOpacity != 0 || trackDetailHd.IsVisible)
                    throw new InvalidOperationException("Loading hid the known author or exposed incomplete metadata.");
                CaptureUiPreview("track-metadata-author-only");
                if (qualityLast) browser.DelayedQuality!.SetResult(); else browser.Delayed.SetResult(original.Track);
                await pending; await WaitForLoginFrameAsync();
                await WaitForLikedLayoutAsync(() => trackDetailMetadataLine.MetadataOpacity == 1 && !trackDetailMetadataLine.IsMetadataRevealing);
                if (!trackDetailMetadataLine.IsVisible || !trackDetailHdGroup.IsVisible || !trackDetailHd.IsVisible || trackDetailState.MetadataLoading ||
                    partialFrame || authorHidden || mismatchedOpacity || restarted || intermediateFrames < 2 || refreshDuringFade)
                    throw new InvalidOperationException("The first visible metadata frame omitted the HD separator/icon.");
            }
        }
        finally { Window.FrameRendered -= AuditFrame; browser.DelayedQuality = null; browser.DelayTrack = 0; }

        browser.FailQuality = true;
        try
        {
            trackDetailState = original with { MetadataLoading = true, Upload = null };
            RenderTrackDetailHeader();
            await LoadTrackDetailMetadataAsync(original.Track.Id, navigationGeneration, lifetime.Token);
            await WaitForLoginFrameAsync();
            await WaitForLikedLayoutAsync(() => trackDetailMetadataLine.MetadataOpacity == 1);
            if (!trackDetailMetadataLine.IsVisible || trackDetailState.MetadataLoading || trackDetailHdGroup.IsVisible)
                throw new InvalidOperationException("A failed quality request hid metadata or left an orphaned separator.");
        }
        finally
        {
            browser.FailQuality = false;
            trackDetailState = original; RenderTrackDetailHeader();
        }
    }
}
