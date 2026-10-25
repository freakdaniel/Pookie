using System.Buffers.Binary;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyTrackDescriptionAsync()
    {
        var original = trackDetailDescription.Text;
        var open = trackDescriptionLinks.OpenLink;
        var loadIcon = trackDescriptionLinks.LoadIcon;
        using var favicon = ImageSource.FromBgraPixels(1, 1, [70, 170, 240, 255]);
        var iconRequests = new List<string>();
        var clipboard = trackDetailDescription.ClipboardService;
        var fixtureClipboard = new MusicComposerClipboard("");
        var opened = new List<Uri>();
        var url = "https://example.com/music/" + new string('x', 120) + "?q=привет";
        var fixture = "Описание 🎧 Привет\nПодробнее: " + url + "\nЕщё: www.example.org/artist).\njavascript:alert(1)";
        try
        {
            await VerifyFaviconCacheAsync();
            timer.Stop();
            if (OperatingSystem.IsWindows())
            {
                ProbeShowWindow(Window.Handle, 5);
                if (!ProbeSetWindowPos(Window.Handle, -1, 0, 0, 0, 0, 0x53)) throw new InvalidOperationException("Could not expose native description check.");
            }
            Window.Activate();
            await WaitForLikedLayoutAsync(() => Window.IsActive);
            trackDescriptionLinks.LoadIcon = async (uri, token) =>
            {
                iconRequests.Add(uri.Host); await Task.Yield(); token.ThrowIfCancellationRequested();
                return uri.Host == "example.com" ? favicon : null;
            };
            trackDescriptionLinks.OpenLink = opened.Add;
            trackDetailDescription.ClipboardService = fixtureClipboard;
            trackDetailDescription.Text = fixture;
            await WaitForLikedLayoutAsync(() => trackDescriptionLinks.Links.Count == 2 &&
                trackDetailDescription.ActualHeight > 70 && !trackDetailDescription.IsArrangeDirty);
            await WaitForLikedLayoutAsync(() => trackDescriptionLinks.Icons.Count == 2 && trackDescriptionLinks.Icons.Any(icon => icon.HasSource));
            if (iconRequests.Count != 2 || trackDescriptionLinks.Icons.Count(icon => !icon.HasSource) != 1)
                throw new InvalidOperationException("Description favicons did not load or retain the offline fallback.");
            if (trackDescriptionLinks.Inlines.Length != 2 || trackDescriptionLinks.PaintSpans.Any(span => span.Decoration != default))
                throw new InvalidOperationException("Description links retained underlines or omitted their inline favicon.");
            var link = trackDescriptionLinks.Links[0];
            var rects = trackDescriptionLinks.BoundsFor(link);
            if (rects.Length < 2 || trackDescriptionLinks.Links[1].Uri.AbsoluteUri != "https://www.example.org/artist")
                throw new InvalidOperationException("Description URLs did not wrap or remove adjacent prose punctuation.");
            var point = new Point(rects[0].X + Math.Min(12, rects[0].Width / 2), rects[0].Y + rects[0].Height / 2);
            var normal = trackDescriptionLinks.ColorFor(link);
            RouteWaveformPointer(point);
            await WaitForLikedLayoutAsync(() => trackDetailDescription.Cursor == CursorType.Hand && trackDescriptionLinks.HoveredUri == link.Uri);
            if (trackDescriptionLinks.ColorFor(link) != normal)
                throw new InvalidOperationException("Description hover colour jumped instead of animating.");
            await WaitForLikedLayoutAsync(() => trackDescriptionLinks.ColorFor(link).R > normal.R && trackDescriptionLinks.ColorFor(link).R < 218);
            await WaitForLikedLayoutAsync(() => trackDescriptionLinks.ColorFor(link).R == 218);
            CaptureUiPreview("track-description-link-hover");
            RouteWaveformClick(point);
            if (opened.Count != 1 || opened[0] != new Uri(url)) throw new InvalidOperationException("Clicking a description URL did not activate the correct link.");
            if (trackDetailDescription.IsFocused || Window.FocusManager.FocusedElement == trackDetailDescription)
                throw new InvalidOperationException("Clicking a description URL left the text caret focused.");

            var start = new Point(rects[0].X + 2, rects[0].Y + rects[0].Height / 2);
            var last = rects[^1]; var end = new Point(last.Right - 2, last.Y + last.Height / 2);
            RouteWaveformPointer(start); PressActionPointer(start, true);
            await WaitForLikedLayoutAsync(() => trackDetailDescription.IsFocused);
            RouteActionHeldPointer(end); PressActionPointer(end, false);
            await WaitForLoginFrameAsync();
            if (!trackDetailDescription.IsFocused || trackDetailDescription.SelectionLength < 10 || opened.Count != 1)
                throw new InvalidOperationException("Dragging through a description link opened it or failed to select text.");
            CaptureUiPreview("track-description-selected");

            RouteSelectableTextKey(Key.A, ModifierKeys.Control);
            RouteSelectableTextKey(Key.C, ModifierKeys.Control);
            if (!fixtureClipboard.TryGetText(out var copied) || copied != fixture)
                throw new InvalidOperationException("Copying the selected description lost Unicode, line breaks or source URLs.");
            if (typeof(ITextInputClient).IsAssignableFrom(trackDetailDescription.GetType()))
                throw new InvalidOperationException("The description is still an input control.");
            RouteWaveformPointer(new Point(2, 2));
            await WaitForLikedLayoutAsync(() => trackDescriptionLinks.ColorFor(link) == normal);
            if (trackDescriptionLinks.HoveredUri != null) throw new InvalidOperationException("Description link hover did not clear.");
            trackDetailDescription.Text = "Описание без ссылок";
            if (trackDescriptionLinks.Links.Count != 0 || trackDescriptionLinks.Icons.Count != 0 || trackDetailDescription.SelectionLength != 0)
                throw new InvalidOperationException("Description links or selection survived changing the track text.");
            await VerifyTelegramDescriptionAsync(fixtureClipboard, opened);
            await VerifyDescriptionReplacementAsync();
            await VerifyDescriptionCaretAsync();
            Console.WriteLine("TRACK_DESCRIPTION_OK: wrapped HTTP/WWW links, trimmed punctuation, animated hover without underline, circular favicon/fallback, link click, drag selection without activation, Unicode/line-break copying, noneditable text and text replacement reset");
        }
        finally
        {
            Window.ReleaseMouseCapture(); Window.FocusManager.ClearFocus();
            trackDetailDescription.ClipboardService = clipboard;
            trackDescriptionLinks.LoadIcon = loadIcon;
            trackDescriptionLinks.OpenLink = open; trackDetailDescription.Text = original;
            timer.Start();
        }
    }

    private async Task VerifyDescriptionCaretAsync()
    {
        var description = trackDetailDescription;
        if (typeof(TextBase).IsAssignableFrom(description.GetType()) || typeof(ITextInputClient).IsAssignableFrom(description.GetType()))
            throw new InvalidOperationException("Selectable description retained an editor or text input client.");
        byte[] Pixels()
        {
            var bounds = description.Bounds;
            using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(
                (int)Math.Ceiling((bounds.Width + 2) * Window.DpiScale),
                (int)Math.Ceiling((bounds.Height + 2) * Window.DpiScale), Window.DpiScale));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface);
            try { context.Clear(Color.Transparent); context.Translate(-bounds.X, -bounds.Y); description.Render(context); }
            finally { context.EndFrame(); }
            return ((ICpuPixelSurface)surface).CopyPixels();
        }

        description.Text = "Описание 👨‍👩‍👧‍👦";
        await WaitForLoginFrameAsync();
        description.Focus();
        RouteSelectableTextKey(Key.End);
        RouteSelectableTextKey(Key.Left, ModifierKeys.Shift);
        if (description.SelectedText != "👨‍👩‍👧‍👦")
            throw new InvalidOperationException("Description keyboard selection split a displayed emoji sequence.");
        Window.FocusManager.ClearFocus();
        description.Text = "Описание трека 🎧\n\n";
        await WaitForLikedLayoutAsync(() => !description.IsArrangeDirty && description.ActualHeight > 30);
        await WaitForLoginFrameAsync();
        var unfocused = Pixels();
        description.Focus();
        await WaitForLoginFrameAsync();
        if (!description.IsFocused || !Pixels().SequenceEqual(unfocused))
            throw new InvalidOperationException("Focusing a selectable description painted an insertion caret.");
        await Task.Delay(600);
        await WaitForLoginFrameAsync();
        if (!Pixels().SequenceEqual(unfocused))
            throw new InvalidOperationException("A focused description changed pixels during the former caret blink interval.");
        CaptureUiPreview("track-description-no-caret");

        Console.WriteLine("DESCRIPTION_CARET_OK: selectable TextBlock shares message selection without any editor, input client or caret pixels across a blink interval");
    }

    private void RouteSelectableTextKey(Key key, ModifierKeys modifiers = 0) =>
        typeof(Window).Assembly.GetType("Aprillz.MewUI.Input.WindowInputRouter", true)!
            .GetMethod("KeyDown", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [Window, new KeyEventArgs(key, 0, modifiers)]);

    private async Task VerifyDescriptionReplacementAsync()
    {
        foreach (var replacement in new[] { "🎧 Новый трек", "e\u0301 Описание с диакритикой", "\r\nНовое описание", "" })
        {
            trackDetailDescription.Text = "https://example.com/previous-track";
            await WaitForLikedLayoutAsync(() => trackDetailDescription.GetTextLayout(trackDetailDescription.ActualWidth).Lines.Count > 0 && !trackDetailDescription.IsArrangeDirty);
            var link = trackDescriptionLinks.Links.Single();
            var rect = trackDescriptionLinks.BoundsFor(link).First();
            RouteWaveformPointer(new(rect.X + 8, rect.Y + rect.Height / 2));
            // Previous link/icon ranges must never decorate replacement text.
            trackDetailDescription.Text = replacement;
            await WaitForLikedLayoutAsync(() => !trackDetailDescription.IsArrangeDirty);
            if (trackDescriptionLinks.Links.Count != 0 || trackDescriptionLinks.Icons.Count != 0 || trackDescriptionLinks.HoveredUri != null)
                throw new InvalidOperationException("Old description decorations survived a document replacement.");
        }
        Console.WriteLine("DESCRIPTION_REPLACEMENT_OK: changing tracks during hover safely replaces links with emoji, combining characters, CRLF and empty descriptions");
    }

    private async Task VerifyTelegramDescriptionAsync(MusicComposerClipboard clipboard, List<Uri> opened)
    {
        var forms = new[] { "tgk: kurokovvv", "tg kurokovvv", "tg: kurokovvv", "тг kurokovvv", "тг: kurokovvv", "тгк kurokovvv" };
        var text = string.Join('\n', forms);
        trackDetailDescription.Text = text;
        await WaitForLikedLayoutAsync(() => !trackDetailDescription.IsArrangeDirty && trackDescriptionLinks.Links.Count == forms.Length);
        for (var i = 0; i < forms.Length; i++)
        {
            var link = trackDescriptionLinks.Links[i];
            if (text.Substring(link.Start, link.Length) != forms[i] || link.Uri.AbsoluteUri != "https://t.me/kurokovvv")
                throw new InvalidOperationException($"Telegram shorthand did not preserve its source or resolve correctly: {forms[i]}");
            var rect = trackDescriptionLinks.BoundsFor(link).First();
            var point = new Point(rect.X + 8, rect.Y + rect.Height / 2);
            RouteWaveformPointer(point);
            if (trackDescriptionLinks.HoveredUri != link.Uri || trackDetailDescription.Cursor != CursorType.Hand)
                throw new InvalidOperationException("Telegram shorthand did not behave as a hovered link.");
            var previous = opened.Count; RouteWaveformClick(point);
            if (opened.Count != previous + 1 || opened[^1] != link.Uri || trackDetailDescription.IsFocused)
                throw new InvalidOperationException("Telegram shorthand did not open on click or left a focused caret.");
        }
        trackDetailDescription.SelectAll(); trackDetailDescription.Copy();
        if (!clipboard.TryGetText(out var copied) || copied != text)
            throw new InvalidOperationException("Copying Telegram shorthand replaced the original description with generated URLs.");

        trackDetailDescription.Text = "ТГК: @Kurokovvv, TG:\tkuro_kovvv. тгк\u00a0kurokovvv";
        if (!trackDescriptionLinks.Links.Select(link => link.Uri.AbsoluteUri).SequenceEqual(
            new[] { "https://t.me/Kurokovvv", "https://t.me/kuro_kovvv", "https://t.me/kurokovvv" }))
            throw new InvalidOperationException("Telegram shorthand lost case, @ prefixes, underscores or horizontal whitespace.");
        trackDetailDescription.Text = "notg kurokovvv\nтг\nkurokovvv\nтг: коротко\ntg kurokovvv-suffix\ntg: " + new string('a', 33) +
            "\ntg kurokovvv.example\nhttps://example.com/tg:kurokovvv";
        if (trackDescriptionLinks.Links.Count != 1 || trackDescriptionLinks.Links[0].Uri.AbsoluteUri != "https://example.com/tg:kurokovvv")
            throw new InvalidOperationException("Telegram shorthand matched inside a word, invalid handle, adjacent line or explicit URL.");
        Console.WriteLine("TELEGRAM_DESCRIPTION_OK: all six shorthand formats, native hover/click, unchanged clipboard, @/case/whitespace, boundaries and explicit URL precedence");
    }

    private async Task VerifyFaviconCacheAsync()
    {
        using var input = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media.chat-small.png")!;
        using var data = new MemoryStream(); input.CopyTo(data); var png = data.ToArray();
        var ico = new byte[22 + png.Length];
        ico[2] = ico[4] = 1; ico[6] = 48; ico[7] = 32; ico[10] = 1; ico[12] = 32;
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(14), png.Length);
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(18), 22); png.CopyTo(ico, 22);
        using var decoded = Storage.FaviconLoader.Decode(ico);
        if (decoded is not { PixelWidth: 48, PixelHeight: 32 } || Storage.FaviconLoader.Decode(ico[..30]) != null)
            throw new InvalidOperationException("PNG favicon containers or truncated ICO data were decoded incorrectly.");
        // One 32-bit DIB pixel and its transparent AND mask.
        var bitmap = new byte[22 + 40 + 8]; ico.AsSpan(0, 22).CopyTo(bitmap);
        bitmap[6] = bitmap[7] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(14), 48);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(22), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(26), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(30), 2);
        bitmap[34] = 1; bitmap[36] = 32; bitmap[62] = 120; bitmap[65] = 255; bitmap[66] = 128;
        using var dib = Storage.FaviconLoader.Decode(bitmap);
        var pixel = new byte[4]; dib?.CopyPixels(pixel, 4);
        if (dib == null || pixel[3] != 0) throw new InvalidOperationException("ICO transparency mask was lost.");
        var endpoint = new Uri("https://" + Guid.NewGuid().ToString("N") + ".invalid/private-path?q=track");
        var handler = new FaviconFixtureHandler(ico);
        using (var loader = new Storage.FaviconLoader(imageDiskCache, handler))
        {
            if (await loader.LoadAsync(endpoint, lifetime.Token) == null ||
                await loader.LoadAsync(endpoint, lifetime.Token) == null || handler.Requests != 1)
                throw new InvalidOperationException("Favicon fetch or per-site memory cache failed.");
        }
        var diskHandler = new FaviconFixtureHandler(ico);
        using var fromDisk = new Storage.FaviconLoader(imageDiskCache, diskHandler);
        if (await fromDisk.LoadAsync(endpoint, lifetime.Token) == null || diskHandler.Requests != 0)
            throw new InvalidOperationException("Favicon disk cache fetched the site again.");
    }

    private sealed class FaviconFixtureHandler(byte[] icon) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.PathAndQuery != "/favicon.ico")
                throw new InvalidOperationException("Favicon request included the track's URL path or query.");
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(icon) });
        }
    }

}
