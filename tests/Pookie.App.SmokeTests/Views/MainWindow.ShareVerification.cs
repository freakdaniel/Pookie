using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Pookie.SoundCloud;
using System.Net;
using System.Xml.Linq;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySharePanelAsync(SoundCloudTrack track, LibraryItem playlist)
    {
        var previous = shareClipboard;
        var clipboard = new ShareClipboard(); shareClipboard = clipboard;
        try
        {
            MoveActionPointer(new(2, 2)); collectionFilter.Focus();
            var trigger = Window.FocusManager.FocusedElement;
            track = track with { PermalinkUrl = "https://soundcloud.com/fixture/shared-track/s-secret?secret_token=s-private", Duration = 180000, PlaybackCount = 65400 };
            ShareTrack(track);
            await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running && !shareContent.Resizing && sharePreview.Opacity == 1 && shareCopy.ActualWidth > 0);
            if (shareLink.Focusable || !shareFocusAnchor.IsFocused || !shareTimeAction.IsVisible || shareLink.Text != track.PermalinkUrl)
                throw new InvalidOperationException("Share panel lost its target, private link, initial focus or read-only URL.");
            VerifyShareChrome();
            await VerifySharePageTransitionsAsync();
            if (shareStartSeconds != null || shareTimeAction.Width != 48 || shareTab.Value != 0 || shareDestinations.Count != 5)
                throw new InvalidOperationException("Share retained playback position, another tab or missing destinations.");
            if (shareDestinations.Any(button => TooltipHints.TextFor(button) != null || button.ToolTip != null))
                throw new InvalidOperationException("Share destinations retained redundant tooltips above their visible captions.");
            RouteWaveformClick(SettingsCenter(shareTimeAction));
            await WaitForLikedLayoutAsync(() => shareTimePopup?.Reveal == 1 && shareMinutes.IsFocused);
            await WaitForLoginFrameAsync();
            if (!shareMinutes.IsFocused || shareMinutes.SelectionLength != shareMinutes.Text.Length)
                throw new InvalidOperationException("Time popup lost initial focus or select-all.");
            var popupLayer = (ChatPopupLayer)VisualTree.Find(shareTimePopup!, element => element is ChatPopupLayer)!;
            await WaitForLikedLayoutAsync(() => popupLayer.HasGlassBackdrop);
            var popupCard = (GlassCard)VisualTree.Find(shareTimePopup!, element => element is GlassCard)!;
            RouteWaveformClick(new(popupCard.Bounds.X + 12, popupCard.Bounds.Y + 12));
            await WaitForLoginFrameAsync();
            if (!shareTimePopup!.IsOpen || !shareDialog.IsOpen || shareMinutes.IsFocusWithin || shareSeconds.IsFocusWithin || !popupCard.IsFocused ||
                new[] { shareHours, shareMinutes, shareSeconds }.Any(field => field.SelectionLength != 0))
                throw new InvalidOperationException("Blank popup click closed it or retained an input caret/selection.");
            RouteWaveformClick(SettingsCenter(shareMinutes));
            if (!shareMinutes.IsFocused) throw new InvalidOperationException("Time input did not resume after a blank click.");
            await VerifyPopupGlassSourceAsync(popupLayer);
            CaptureUiPreview("share-time-popup");
            RouteShareKey(Key.Tab);
            if (!shareSeconds.IsFocused) throw new InvalidOperationException("Time popup Tab escaped the editor.");
            shareMinutes.Text = "01"; shareSeconds.Text = "23";
            if (shareLink.Text != track.PermalinkUrl) throw new InvalidOperationException("Time draft changed the link before applying.");
            RouteShareKey(Key.Enter);
            if (shareTimePopup!.IsOpen || shareTimeAction.Width != 48)
                throw new InvalidOperationException("Applying time did not close the popup or snapped the width.");
            await Task.Delay(60, lifetime.Token); await WaitForLoginFrameAsync();
            if (shareTimeAction.Width <= 48 || shareTimePrefix.Opacity <= 0)
                throw new InvalidOperationException("Selected time failed to animate into the icon button.");
            await WaitForLikedLayoutAsync(() => !shareTimeMotion.IsRunning);
            if (shareTimeLabel.Text != "1:23" || shareTimePrefix.Opacity != 1 || shareTimeLabel.Bounds.X >= shareTimeAction.Bounds.Right - 20)
                throw new InvalidOperationException("Time label did not settle left of the icon.");
            if (!shareLink.Text.EndsWith("#t=1:23", StringComparison.Ordinal) || !shareLink.Text.Contains("secret_token=s-private"))
                throw new InvalidOperationException("Timed sharing removed the private access token or failed to add the timestamp.");
            RouteWaveformClick(SettingsCenter(shareCopy));
            if (clipboard.Text != shareLink.Text || TooltipHints.TextFor(shareCopy) != "Скопировано" ||
                !shareCopyIcon.Confirmed || !shareCopyIcon.Animating)
                throw new InvalidOperationException("Share copy button did not write the displayed link.");
            await Task.Delay(250, lifetime.Token); await WaitForLoginFrameAsync();
            CaptureUiPreview("share-track");
            await VerifyShareCopyFeedbackAsync(clipboard);
            ToggleShareTime();
            await WaitForLikedLayoutAsync(() => shareTimePopup!.Reveal == 1 && shareMinutes.IsFocused);
            var appliedLink = shareLink.Text;
            if (shareHours.IsVisible || !shareMinutes.IsVisible || TooltipHints.TextFor(shareMinutes) != null || TooltipHints.TextFor(shareSeconds) != null)
                throw new InvalidOperationException("Sub-hour timecode retained hours or redundant field tooltips.");
            var restingBackground = shareMinutes.Background;
            shareSeconds.Text = "99";
            if (!shareTimeFields.Invalid || !shareTimeFields.Animating || shareMinutes.Background != restingBackground)
                throw new InvalidOperationException("Invalid timecode did not start smooth field feedback.");
            await Task.Delay(65, lifetime.Token); await WaitForLoginFrameAsync();
            if (shareMinutes.Background == restingBackground || shareTimeFields.Offset == 0)
                throw new InvalidOperationException("Invalid timecode did not animate red and shake.");
            await WaitForLikedLayoutAsync(() => !shareTimeFields.Animating);
            if (shareMinutes.Background != ShareTimeFields.ErrorSurface || shareMinutes.Foreground != ShareTimeFields.ErrorText)
                throw new InvalidOperationException("Invalid timecode did not settle on red inputs and digits.");
            CaptureUiPreview("share-time-error");
            shareSeconds.Text = "23";
            await WaitForLikedLayoutAsync(() => !shareTimeFields.Animating);
            if (shareTimeFields.Invalid || shareMinutes.Background != TrackButtons.TonalSurface || shareMinutes.Foreground != TrackButtons.OnSurface)
                throw new InvalidOperationException("Correcting timecode did not restore normal field colors.");
            foreach (var (hours, minutes, seconds) in new[] { ("00", "00", "99"), ("00", "60", "00"), ("00", "03", "01"), ("00", "-1", "00"), ("00", "aa", "00"), ("00", "", "00") })
            {
                shareHours.Text = hours; shareMinutes.Text = minutes; shareSeconds.Text = seconds;
                if (shareTimeApply.IsEnabled || !shareCopy.IsEnabled || shareLink.Text != appliedLink)
                    throw new InvalidOperationException("Invalid time draft changed the applied link or enabled confirmation.");
            }
            RouteShareKey(Key.Escape);
            if (shareTimePopup!.IsOpen || !shareDialog.IsOpen || shareLink.Text != appliedLink)
                throw new InvalidOperationException("Escape did not cancel only the time draft.");
            ToggleShareTime();
            if (shareMinutes.Text != "01" || shareSeconds.Text != "23")
                throw new InvalidOperationException("Cancelled draft replaced the committed time.");
            shareHours.Text = "00"; shareMinutes.Text = "00"; shareSeconds.Text = "00"; CommitShareTime();
            if (!shareCopy.IsEnabled || !shareLink.Text.EndsWith("#t=0:00", StringComparison.Ordinal))
                throw new InvalidOperationException("Zero timestamp cannot be shared.");
            await WaitForLikedLayoutAsync(() => !shareTimeMotion.IsRunning);
            var expandedWidth = shareTimeAction.Width;
            ToggleShareTime(); ApplyShareTime(null);
            if (shareTimeAction.Width != expandedWidth || shareLink.Text != track.PermalinkUrl)
                throw new InvalidOperationException("Reset snapped button width or retained the timestamp.");
            await WaitForLikedLayoutAsync(() => !shareTimeMotion.IsRunning);
            if (shareTimeAction.Width != 48 || shareTimePrefix.Opacity != 0)
                throw new InvalidOperationException("Reset failed to collapse the time button.");
            SelectShareTab(2);
            await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareEmbedCopy.ActualHeight > 0);
            var iframe = XElement.Parse(shareEmbedCode.Text);
            var source = new Uri(iframe.Attribute("src")!.Value);
            var decoded = WebUtility.UrlDecode(source.Query);
            if (source.Host != "w.soundcloud.com" || !decoded.Contains(track.PermalinkUrl) ||
                !decoded.Contains("auto_play=false") || iframe.Attribute("height")!.Value != "166")
                throw new InvalidOperationException("Embed lost private access or produced an invalid/default-autoplay iframe.");
            CopyShareEmbed();
            if (clipboard.Text != shareEmbedCode.Text || !shareEmbedCopyGlyph.Confirmed)
                throw new InvalidOperationException("Embed did not copy its actual displayed HTML.");
            shareEmbedColor.Text = "\"bad";
            if (shareEmbedCopy.IsEnabled || shareEmbedCode.Text != "" || !shareMessage.IsVisible)
                throw new InvalidOperationException("Invalid embed color remained copyable.");
            shareEmbedColor.Text = "#14ABCD"; SetShareEmbedLayout(true);
            if (!shareEmbedCode.Text.Contains("height=\"450\"") || !shareEmbedCode.Text.Contains("%2314abcd"))
                throw new InvalidOperationException("Embed layout/color selection did not change the generated iframe.");
            await Task.Delay(250, lifetime.Token); await WaitForLoginFrameAsync();
            CaptureUiPreview("share-embed");
            await VerifyShareWidgetAsync(track);
            var users = chatConversations.ToArray();
            try
            {
                var recipient = new SoundCloudUser { Id = 77, Username = "Получатель", FullName = "Александр Соколов", AvatarUrl = "https://i1.sndcdn.com/share-avatar-fixture.jpg" };
                var avatarPixels = new byte[40 * 40 * 4];
                for (var y = 0; y < 40; y++) for (var x = 0; x < 40; x++)
                { var index = (y * 40 + x) * 4; avatarPixels[index] = 220; avatarPixels[index + 1] = (byte)(110 + y * 2); avatarPixels[index + 2] = (byte)(70 + x * 2); avatarPixels[index + 3] = 255; }
                artworkRequests[recipient.AvatarUrl] = Task.FromResult<ImageSource?>(ImageSource.FromBgraPixels(40, 40, avatarPixels));
                chatConversations.Clear(); chatConversations.Add(new("fixture-share", recipient, null, true));
                chatConversations.Add(new("fixture-share-two", new() { Id = 78, Username = "тихий берег", FullName = "Ночной плейлист" }, null, true));
                chatConversations.Add(new("fixture-share-three", new() { Id = 79, Username = "Lena", FullName = "Елена" }, null, true));
                SelectShareTab(1); await SearchShareRecipientsAsync();
                if (shareRecipientChoices.Children.Count != 3) throw new InvalidOperationException("Recent share recipients were not shown.");
                await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareRecipientChoices.ActualHeight > 100);
                var row = (Button)shareRecipientChoices.Children[0];
                var avatar = (Image)VisualTree.Find(row, element => element is Image { Width: 40 })!;
                var name = (TextBlock)VisualTree.Find(row, element => element is TextBlock { Text: "Получатель" })!;
                if (name.Bounds.X <= avatar.Bounds.Right || name.HorizontalAlignment != HorizontalAlignment.Left || avatar.Source == null)
                    throw new InvalidOperationException("Share recipients lost avatars or left-aligned names.");
                var captions = new List<string>();
                VisualTree.Visit(shareRecipientPicker, element => { if (element is TextBlock text) captions.Add(text.Text); });
                if (captions.Contains("Недавние диалоги") || captions.Contains("Результаты поиска")) throw new InvalidOperationException("Recipient list retained its redundant headings.");
                CaptureUiPreview("share-recipients");
                shareRecipientSearch.Text = "несуществующий получатель"; await SearchShareRecipientsAsync();
                await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareRecipientEmpty.IsVisible);
                if (shareRecipientResults.IsVisible || !shareRecipientEmptyArt.IsVisible || shareRecipientEmptyArt.Source == null ||
                    shareRecipientEmptyTitle.Text != "А тут тупик" || shareRecipientEmptyText.Text != "Я искал вдоль и поперек, но никого не нашел...")
                    throw new InvalidOperationException("Empty recipient search lost the route illustration or its requested copy.");
                CaptureUiPreview("share-recipients-empty");
                shareRecipientSearch.Text = "?"; await SearchShareRecipientsAsync();
                if (!shareSearchClear.IsVisible || shareSearchGlyph.Loading) throw new InvalidOperationException("Recipient search did not expose clear/settle its loading state.");
                await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareSearchClear.ActualWidth > 0);
                RouteWaveformClick(SettingsCenter(shareSearchClear)); await SearchShareRecipientsAsync();
                await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareRecipientSearch.Text.Length == 0 && !shareSearchClear.IsVisible && shareRecipientChoices.Children.Count == 3);
                row = (Button)shareRecipientChoices.Children[0]; RouteWaveformClick(SettingsCenter(row));
                if (shareRecipient?.Id != 77 || shareRecipientDetails.Text != recipient.FullName) throw new InvalidOperationException("Recipient row click did not select its actual profile.");
                shareComposeText.Text = "Послушай";
                await WaitForLikedLayoutAsync(() => !shareContent.Resizing && shareSend.ActualHeight > 0);
                if (!shareSend.IsEnabled || shareRecipientPicker.IsVisible || !shareComposeFields.IsVisible)
                    throw new InvalidOperationException("Selecting a recipient did not reveal a usable draft composer.");
                var draft = SharedMessageDraft("Старый черновик", shareComposeText.Text, shareLink.Text);
                if (draft != "Старый черновик\nПослушай\n" + shareLink.Text || SharedMessageDraft(draft, "", "") != draft)
                    throw new InvalidOperationException("Preparing Share would replace an existing draft.");
                await Task.Delay(250, lifetime.Token); await WaitForLoginFrameAsync();
                CaptureUiPreview("share-message");
            }
            finally { artworkRequests["https://i1.sndcdn.com/share-avatar-fixture.jpg"] = Task.FromResult<ImageSource?>(null); chatConversations.Clear(); chatConversations.AddRange(users); }
            Window.Activate(); await WaitForLikedLayoutAsync(() => Window.IsActive);
            SelectShareTab(0);
            var escape = new KeyEventArgs(Key.Escape, 0); shareDialog.HandleKey(escape);
            await WaitForLikedLayoutAsync(() => !shareDialog.IsVisible);
            if (!escape.Handled || Window.FocusManager.FocusedElement != trigger)
                throw new InvalidOperationException("Dismissing Share failed to restore focus.");
            OpenShare(playlist);
            await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running);
            if (shareTrackStats.IsVisible || shareTimeAction.IsVisible || ((MaterialTrackButton)shareCopy).IsConnected || shareLink.Text.Contains("#t=") || shareTarget?.Playlist?.Id != playlist.Playlist?.Id)
                throw new InvalidOperationException("Playlist sharing retained the previous track's time or target.");
            CaptureUiPreview("share-playlist");
            CloseShare(); ShareTrack(track);
            await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running);
            if (shareTarget?.Track?.Id != track.Id || !shareContent.IsHitTestVisible)
                throw new InvalidOperationException("Reopening Share during dismissal left the old close callback active.");
            ShareTrack(track with { Duration = 7200000 });
            await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running);
            ToggleShareTime();
            shareHours.Text = "01"; shareMinutes.Text = "02"; shareSeconds.Text = "03"; CommitShareTime();
            await WaitForLikedLayoutAsync(() => !shareTimeMotion.IsRunning);
            if (!shareLink.Text.EndsWith("#t=1:02:03", StringComparison.Ordinal) || shareTimeLabel.Text != "1:02:03")
                throw new InvalidOperationException("Hour timestamp was not preserved in the link and time button.");
            ToggleShareTime();
            if (!shareHours.IsVisible || !shareHours.IsFocused || TooltipHints.TextFor(shareHours) != null)
                throw new InvalidOperationException("Long track did not expose/focus hours without a tooltip.");
            shareHours.Text = "999999";
            if (shareTimeApply.IsEnabled) throw new InvalidOperationException("Overflow timecode remained applicable.");
            CloseShare(true);
            if (shareTimePopup!.IsOpen) throw new InvalidOperationException("Closing Share left its time popup open.");
            foreach (var (duration, hours, minutes) in new[] { (59999d, false, true), (60000d, false, true), (3599999d, false, true), (3600000d, true, true) })
            {
                ShareTrack(track with { Duration = duration, PlaybackCount = null });
                await WaitForLikedLayoutAsync(() => shareDialog.IsOpen && !shareDialog.Running);
                ToggleShareTime();
                await WaitForLikedLayoutAsync(() => shareTimePopup!.Reveal == 1);
                if (shareHours.IsVisible != hours || shareMinutes.IsVisible != minutes ||
                    !(hours ? shareHours : minutes ? shareMinutes : shareSeconds).IsFocused || shareTrackPlays.Text != "—")
                    throw new InvalidOperationException("Timecode unit visibility/focus or missing count failed at duration " + duration);
                if (duration < 60000)
                {
                    shareHours.Text = "999999"; shareMinutes.Text = "00"; shareSeconds.Text = "59";
                    if (!shareTimeApply.IsEnabled) throw new InvalidOperationException("Hidden hours affected minute/second validation.");
                    CaptureUiPreview("share-time-seconds");
                    CommitShareTime();
                    if (!shareLink.Text.EndsWith("#t=0:59", StringComparison.Ordinal)) throw new InvalidOperationException("Sub-minute link was not formatted correctly.");
                }
                CloseShare(true);
            }
            await VerifyShareMessageSendingAsync(track);
            await VerifyShareTabResetAsync(track);
            if (Environment.GetEnvironmentVariable("POOKIE_WIDGET_NATIVE") == "1") await VerifyShareWidgetNativeAsync(track);
            Console.WriteLine("SHARE_UI_OK: compact dialog chrome and equal plaque padding, five destination icons including Snapchat, icon copy/tooltip, duration-aware timecode units, animated red/shaking fields without tooltips/error text, plaque duration/plays, draft validation, popup focus/keyboard, animated time width/reset, private widget HTML/color/layout/copy, background message sending, Escape/focus return and reopen");
        }
        finally { CloseShare(true); shareClipboard = previous; }
    }

    private void RouteShareKey(Key key)
    {
        var args = new KeyEventArgs(key, 0);
        typeof(Window).GetMethod("RaisePreviewKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(Window, [args]);
        if (!args.Handled) RouteSettingsKey(key);
    }

    private async Task VerifySharePageTransitionsAsync()
    {
        var samples = 0; var fading = 0; var y = shareFocusAnchor.Bounds.Y - shareContent.BodyBounds.Y;
        void Frame()
        {
            if (!sharePages.Animating) return;
            samples++;
            if (sharePages.ContentOverlaps || Math.Abs(shareFocusAnchor.Bounds.Y - shareContent.BodyBounds.Y - y) > .5)
                throw new InvalidOperationException("Share's shared header moved or its tab pages overlapped during transition.");
            var page = sharePages.PageContent(shareTab.Value).Parent as UIElement;
            if (page?.Opacity is > 0 and < 1) fading++;
        }
        Window.FrameRendered += Frame;
        try
        {
            foreach (var tab in new[] { 1, 2, 0 })
            {
                SelectShareTab(tab);
                await WaitForLikedLayoutAsync(() => !sharePages.Animating && !shareContent.Resizing);
            }
            SelectShareTab(1); await Task.Delay(65, lifetime.Token);
            SelectShareTab(2); await Task.Delay(65, lifetime.Token);
            SelectShareTab(0);
            await WaitForLikedLayoutAsync(() => !sharePages.Animating && !shareContent.Resizing);
            if (samples < 6 || fading < 3 || !shareLinksPanel.IsVisible || shareComposePanel.IsVisible || shareEmbedPanel.IsVisible)
                throw new InvalidOperationException("Share skipped fade-through frames or settled on an outdated page after rapid retargeting.");
            Console.WriteLine("SHARE_PAGE_TRANSITIONS_OK: same fade-through slide as playlists, stationary shared header, separate content, fixed page viewports and rapid retargeting");
        }
        finally { Window.FrameRendered -= Frame; }
    }

    private async Task VerifyShareTabResetAsync(SoundCloudTrack track)
    {
        MoveActionPointer(new(2, 2));
        foreach (var last in new[] { 2, 1, 2 })
        {
            ShareTrack(track); SelectShareTab(last);
            await WaitForLikedLayoutAsync(() => !shareDialog.Running && !shareContent.Resizing);
            CloseShare(); await WaitForLikedLayoutAsync(() => !shareDialog.IsVisible);
            ShareTrack(track);
            Check();
            await WaitForLikedLayoutAsync(() => !shareDialog.Running); Check();
            CaptureUiPreview("share-tabs-reopened-" + last);
            CloseShare(true);
        }
        Console.WriteLine("SHARE_TAB_RESET_OK: Link alone is selected immediately and after opening, following Widget/Message/Widget dismissal cycles");
        void Check()
        {
            if (shareTab.Value != 0 || !shareLinksPanel.IsVisible || shareEmbedPanel.IsVisible || shareComposePanel.IsVisible)
                throw new InvalidOperationException("Reopening Share did not reset its content to Link.");
            for (var index = 0; index < shareSectionTabs.Length; index++)
            {
                var line = (Border)VisualTree.Find(shareSectionTabs[index], element => element is Border { Height: 2 })!;
                if (line.Opacity != (index == 0 ? 1 : 0)) throw new InvalidOperationException("Reopened Share retained an animated underline on tab " + index);
            }
        }
    }

    private async Task VerifyPopupGlassSourceAsync(ChatPopupLayer popup)
    {
        var root = (Panel)Window.Content!;
        Element branch = popup;
        while (branch.Parent != root) branch = branch.Parent!;
        var fixture = new PopupGlassFixture { IsHitTestVisible = false };
        var area = popup.SurfaceBounds;
        var point = new Point(area.X + 30, area.Y + 28);
        fixture.Split = point.X;
        root.Insert(root.Children.ToList().IndexOf(branch), fixture.Row(0));
        async Task SetColors(Color left, Color right)
        {
            var card = (GlassCard)VisualTree.Find(popup, element => element is GlassCard)!;
            var painted = card.PopupBackdropPaintVersion;
            fixture.LeftColor = left; fixture.RightColor = right; fixture.InvalidateVisual();
            // Assert the real window's retained drawing before any offscreen
            // Render() below can hide a stale retained drawing. The new color
            // must already be in the same window frame as the new glass.
            await WaitForLoginFrameAsync();
            if (card.PopupBackdropPaintVersion <= painted)
                throw new InvalidOperationException("The popup did not sample the changed background on its next frame.");
        }
        Color Read(Point location)
        {
            using var scope = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(4, 4, 1));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(2 - location.X, 2 - location.Y);
            popup.Render(context); context.EndFrame();
            var cpu = (ICpuPixelSurface)surface; var pixels = cpu.GetReadOnlyPixelSpan(); var index = 2 * cpu.StrideBytes + 8;
            return Color.FromRgb(pixels[index + 2], pixels[index + 1], pixels[index]);
        }
        try
        {
            var red = Color.FromRgb(240, 30, 30); var blue = Color.FromRgb(30, 30, 240);
            await SetColors(red, red); var first = Read(point);
            await SetColors(blue, blue); var second = Read(point);
            if (first.R < first.B + 30 || second.B < second.R + 30)
                throw new InvalidOperationException($"Popup glass did not follow its actual background or sampled itself: red={first}, blue={second}.");
            await SetColors(red, blue);
            var left = Read(new(point.X - 3, point.Y)); var right = Read(new(point.X + 3, point.Y));
            if (Math.Min(left.R, left.B) < 30 || Math.Min(right.R, right.B) < 30 || Math.Abs(left.R - right.R) > 30)
                throw new InvalidOperationException($"Popup glass retained a sharp background edge instead of blurring: left={left}, right={right}.");
            for (var step = 0; step < 6; step++)
            {
                var showRed = step % 2 == 0;
                fixture.Split = point.X + (showRed ? 24 : -24);
                await SetColors(red, blue);
                var pixel = Read(point);
                if (showRed ? pixel.R < pixel.B + 30 : pixel.B < pixel.R + 30)
                    throw new InvalidOperationException($"Moving backdrop was a frame behind the popup at step {step}: {pixel}.");
            }
            Console.WriteLine("LIVE_POPUP_BACKDROP_OK: six consecutive positions sampled in the same frame");
        }
        finally
        {
            root.Remove(fixture); fixture.Dispose();
            for (var frame = 0; frame < 4; frame++) await WaitForLoginFrameAsync();
        }
    }

    private sealed class PopupGlassFixture : Control
    {
        internal double Split;
        internal Color LeftColor, RightColor;
        protected override void OnRender(IGraphicsContext context)
        {
            context.FillRectangle(Bounds, LeftColor);
            context.FillRectangle(new Rect(Split, Bounds.Y, Math.Max(0, Bounds.Right - Split), Bounds.Height), RightColor);
        }
    }

    private void VerifyShareChrome()
    {
        var cover = shareCover.TranslateRect(new(0, 0, shareCover.ActualWidth, shareCover.ActualHeight), Window);
        var plaque = sharePreview.TranslateRect(new(0, 0, sharePreview.ActualWidth, sharePreview.ActualHeight), Window);
        if (Math.Abs(cover.X - plaque.X - 16) > .1 || Math.Abs(cover.Y - plaque.Y - 16) > .1 || Math.Abs(plaque.Bottom - cover.Bottom - 16) > .1)
            throw new InvalidOperationException($"Unequal share plaque insets: cover={cover}, plaque={plaque}.");
        var controls = 0; VisualTree.Visit(sharePreview, element => { if (element is Button || element is TrackWaveform) controls++; });
        if (controls != 0 || shareDialog.Children.OfType<MaterialTrackButton>().Count() != 1)
            throw new InvalidOperationException("Share plaque gained player controls or lost the external close button.");
        var headings = new List<string>();
        VisualTree.Visit(shareForm, element => { if (element is TextBlock text) headings.Add(text.Text); });
        if (!headings.Contains("Виджет") || headings.Contains("Встроить"))
            throw new InvalidOperationException("Share did not rename its embed tab to Widget.");
        if (headings.Contains("Поделиться") || headings.Contains("Ссылка на трек") || shareCopy.Content is not CopyFeedbackGlyph ||
            shareLinkGlass.Background.A == 255 || shareLinkGlass.CornerRadius != 24 || shareLink.Parent != shareTimeAction.Parent ||
            ((MaterialTrackButton)shareCopy).IsConnected || ((MaterialTrackButton)shareTimeAction).IsConnected)
            throw new InvalidOperationException("Share lost its glass URL/timecode group or separate circular copy button.");
        if (Math.Abs(shareDestinations[0].Bounds.X - shareLinkGlass.Bounds.X) > .1 ||
            Math.Abs(shareDestinations[^1].Bounds.Right - shareCopy.Bounds.Right) > .1)
            throw new InvalidOperationException("Outer social buttons are not aligned with the URL and copy row.");
        var step = SettingsCenter(shareDestinations[1]).X - SettingsCenter(shareDestinations[0]).X;
        // Natural caption widths can snap adjacent button centres to either
        // side of a physical pixel at fractional layout coordinates.
        var spacingTolerance = 1 / Window.DpiScale + .01;
        for (var index = 2; index < shareDestinations.Count; index++)
            if (Math.Abs(SettingsCenter(shareDestinations[index]).X - SettingsCenter(shareDestinations[index - 1]).X - step) > spacingTolerance)
                throw new InvalidOperationException("Social button spacing is uneven: " + string.Join(", ", shareDestinations.Select(button => SettingsCenter(button).X)));
        if (shareDestinations[2].Background != Color.Black)
            throw new InvalidOperationException("X lost its black background.");
        if (shareTrackDuration.Text != "3:00" || shareTrackPlays.Text != "65.4K" ||
            Math.Abs(shareTrackDuration.Bounds.Right - plaque.Right + 16) > .1 ||
            shareTrackPlays.Bounds.Y <= shareTrackDuration.Bounds.Y)
            throw new InvalidOperationException("Share plaque lost right-aligned duration/play count or their order.");
        foreach (var name in new[] { "SimpleIcons.social-telegram", "SimpleIcons.social-vk", "SimpleIcons.social-x", "snapchat", "envelope" })
            if (Icons.Source(name).IntrinsicSize.Width <= 0) throw new InvalidOperationException("Share logo failed to load: " + name);
    }

    private async Task VerifyShareCopyFeedbackAsync(ShareClipboard clipboard)
    {
        var bounds = shareCopy.Bounds;
        await Task.Delay(1000, lifetime.Token);
        if (!shareCopyIcon.Confirmed || shareCopyIcon.Animating || shareCopyIcon.Progress != 1)
            throw new InvalidOperationException("Copy confirmation did not settle or returned too early.");
        CopyShareLink();
        await Task.Delay(1100, lifetime.Token);
        if (!shareCopyIcon.Confirmed || TooltipHints.TextFor(shareCopy) != "Скопировано")
            throw new InvalidOperationException("Repeated copying did not restart the confirmation delay.");
        await WaitForLikedLayoutAsync(() => !shareCopyIcon.Confirmed && shareCopyIcon.Progress is > 0 and < 1);
        CaptureUiPreview("share-copy-return");
        await WaitForLikedLayoutAsync(() => !shareCopyIcon.Animating && shareCopyIcon.Progress == 0);
        if (shareCopy.Bounds != bounds || TooltipHints.TextFor(shareCopy) != "Скопировать ссылку")
            throw new InvalidOperationException("Copy reversal moved the button or retained its success hint.");
        CopyShareLink();
        await WaitForLikedLayoutAsync(() => shareCopyIcon.Progress is > 0 and < 1);
        CaptureUiPreview("share-copy-confirm");
        RefreshShareLink();
        await WaitForLikedLayoutAsync(() => !shareCopyIcon.Animating && shareCopyIcon.Progress == 0);
        clipboard.AcceptWrites = false;
        CopyShareLink();
        if (shareCopyIcon.Confirmed || TooltipHints.TextFor(shareCopy) != "Не удалось скопировать. Повторить")
            throw new InvalidOperationException("Failed copying showed a success check.");
        clipboard.AcceptWrites = true;
        RefreshShareLink();
        Console.WriteLine("SHARE_COPY_FEEDBACK_OK: animated confirmation and return after two seconds, repeat restarts the delay, stable geometry, link reset and failed-write feedback");
    }

    private sealed class ShareClipboard : IClipboardService
    {
        internal string Text = "";
        internal bool AcceptWrites = true;
        public bool TrySetText(string text) { if (!AcceptWrites) return false; Text = text; return true; }
        public bool TryGetText(out string text) { text = Text; return true; }
    }
}
