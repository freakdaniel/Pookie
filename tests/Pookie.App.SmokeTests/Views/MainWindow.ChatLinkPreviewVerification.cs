using Aprillz.MewUI;
using System.Text.Json;
using Pookie.SoundCloud;
using Aprillz.MewUI.Input;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatLinkPreviewsAsync()
    {
        const string profileUrl = "https://soundcloud.com/fixture-profile";
        const string missingUrl = "https://soundcloud.com/fixture/missing-link";
        var draft = chatInput.Text;
        var originalTransport = api.BrowserTransport!;
        var transport = new ChatLinkPreviewFixture(originalTransport);
        var imagePath = Path.Combine(Path.GetTempPath(), "pookie-composer-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            api.BrowserTransport = transport;
            chatInput.Text = "Текст";
            await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatInput.ActualWidth > 100);
            var compact = chatComposer.ActualHeight;
            var character = chatInput.GetCharRectInWindow(0);
            var buttonCenter = chatSend.Bounds.Y + chatSend.Bounds.Height / 2;
            if (Math.Abs(character.Y + character.Height / 2 - buttonCenter) > 1.5)
                throw new InvalidOperationException($"Composer text is not vertically centred: text={character}, send={chatSend.Bounds}, editor={chatInput.Bounds}, viewport={chatInput.TextViewportBounds}, padding={chatInput.Padding}, line={chatInput.DefaultLineHeight}");

            chatInput.Text = "";
            ((ITextInputClient)chatInput).HandleTextInput(new("https://soundcloud.com/fix"));
            ((ITextInputClient)chatInput).HandleTextInput(new("ture-profile"));
            if (chatInput.Text != profileUrl) throw new InvalidOperationException("Pending recognition inserted spaces into a manually typed profile URL");
            if (chatMusicEditor!.Count != 1 || !chatComposerLinks[profileUrl].Loading || chatComposerLinks[profileUrl].Label != "Ищу...")
                throw new InvalidOperationException("A pasted profile did not immediately become a loading link element");
            await WaitForLoginFrameAsync(); CaptureUiPreview("chat-link-pending", chatComposer);
            chatMusicRecognizeTimer.Stop(); await RecognizeChatMusicAsync();
            await WaitForLikedLayoutAsync(() => chatComposerLinks[profileUrl].Label == "Профиль из ссылки" && !chatComposerSurface.IsResizing);
            if (chatInput.Text != profileUrl || chatMusicCache[profileUrl].User?.Id != 7 || Math.Abs(chatComposer.ActualHeight - compact) > 1)
                throw new InvalidOperationException($"Profile recognition changed the draft or grew a single-line composer: {compact}->{chatComposer.ActualHeight}");
            await WaitForLoginFrameAsync(); CaptureUiPreview("chat-link-profile", chatComposer);

            var count = transport.ProfileResolutions;
            OpenTopSearch(); query.Value = profileUrl;
            await WaitForLikedLayoutAsync(() => topSearchBar.LinkLabel == "Профиль из ссылки" && !topSearchBar.LinkTransitioning);
            if (transport.ProfileResolutions != count) throw new InvalidOperationException("Search and chat did not share their metadata request");
            CaptureUiPreview("search-link-profile", topSearchBar); CloseTopSearch(clear: true);

            chatInput.Text = missingUrl;
            chatMusicRecognizeTimer.Stop(); await RecognizeChatMusicAsync();
            await WaitForLikedLayoutAsync(() => chatMusicEditor.Count == 0 && !chatComposerSurface.IsResizing);
            if (chatInput.Text != missingUrl || !chatComposerLinks[missingUrl].Failed)
                throw new InvalidOperationException("An unavailable link did not return to editable source text");
            CaptureUiPreview("chat-link-fallback", chatComposer);

            OpenTopSearch(); query.Value = missingUrl;
            await WaitForLikedLayoutAsync(() => !topSearchBar.ShowsLink && topSearchInput.Opacity == 1);
            if (query.Value != missingUrl) throw new InvalidOperationException("Search lost the unavailable source URL");
            CaptureUiPreview("search-link-fallback", topSearchBar); CloseTopSearch(clear: true);

            chatInput.Text = "";
            using (var image = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media.chat-small.png")!)
            {
                using var file = File.Create(imagePath);
                await image.CopyToAsync(file);
            }
            await AddChatFilesAsync([imagePath]);
            await WaitForLikedLayoutAsync(() => chatAttachmentPreview.Source != null && !chatComposerSurface.IsResizing);
            if (chatAttachmentStrip.Bounds.X - chatComposer.Bounds.X < 12 || chatAttachmentStrip.Bounds.Y - chatComposer.Bounds.Y < 10 ||
                chatAttachmentStrip.Bounds.Right > chatComposer.Bounds.Right - 12 || chatAttachmentPreview.Bounds.Bottom > chatAttach.Bounds.Y)
                throw new InvalidOperationException("Attachment preview lacks inset space inside the composer corners");
            CaptureUiPreview("chat-attachment-insets", chatComposer);
            Console.WriteLine("CHAT_LINK_PREVIEWS_OK: profiles, shared search metadata, pending spinner, source fallback, centred text, stable single-line height and inset attachment preview");
        }
        finally
        {
            api.BrowserTransport = originalTransport;
            ClearChatAttachments(); File.Delete(imagePath); chatInput.Text = draft;
        }
    }

    private sealed class ChatLinkPreviewFixture(ISoundCloudTransport inner) : ISoundCloudBrowserTransport
    {
        internal int ProfileResolutions;
        public Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken token = default)
        {
            if (uri.AbsolutePath == "/resolve" && Uri.UnescapeDataString(uri.Query).Contains("fixture-profile", StringComparison.Ordinal))
            {
                ProfileResolutions++;
                return Task.FromResult(JsonDocument.Parse("""{"kind":"user","id":7,"username":"Профиль из ссылки"}"""));
            }
            if (uri.AbsolutePath == "/resolve" && Uri.UnescapeDataString(uri.Query).Contains("missing-link", StringComparison.Ordinal))
                return Task.FromException<JsonDocument>(new SoundCloudException("Ссылка недоступна", 404));
            return inner.GetJsonAsync(uri, token);
        }
        public Task<JsonDocument> RequestChatAsync(ChatRequest request, CancellationToken token = default) => inner.RequestChatAsync(request, token);
        public Task<JsonDocument> ReadTrackAsync(TrackReadRequest request, CancellationToken token = default) => inner.ReadTrackAsync(request, token);
        public Task SetLikedAsync(long userId, long trackId, bool liked, CancellationToken token = default) => inner.SetLikedAsync(userId, trackId, liked, token);
    }
}
