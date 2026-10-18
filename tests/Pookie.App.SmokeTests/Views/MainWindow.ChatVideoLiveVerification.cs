using Aprillz.MewUI;
using Pookie.App.Auth;
using Pookie.App.Browser;
using Pookie.App.Storage;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatVideoLiveUiAsync()
    {
        try
        {
            var peer = Environment.GetEnvironmentVariable("POOKIE_CHAT_VIDEO_LIVE_PEER")
                ?? throw new InvalidOperationException("Set POOKIE_CHAT_VIDEO_LIVE_PEER to the explicitly authorized chat recipient.");
            var path = Environment.GetEnvironmentVariable("POOKIE_CHAT_VIDEO_FIXTURE")
                ?? throw new InvalidOperationException("Set POOKIE_CHAT_VIDEO_FIXTURE to a local video.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var savedVault = SessionVault.Open(new AppDataPaths());
            var account = savedVault.Load() ?? throw new InvalidOperationException("No saved account for the live chat video check.");
            AttachBrowser(new NativeBrowserSession(account));
            me = await browser!.GetMeAsync(timeout.Token);
            api.Session = browser.Account; signedIn.Value = true;
            await ShowWorkspaceAsync();
            await NavigateAsync(Page.Chats);
            for (var pageIndex = 0; pageIndex < 5 && chatListCursor != null && !chatConversations.Any(c => c.Recipient.Username.Equals(peer, StringComparison.OrdinalIgnoreCase)); pageIndex++)
                await LoadMoreChatsAsync();
            var conversation = chatConversations.FirstOrDefault(c => c.Recipient.Username.Equals(peer, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The authorized chat recipient was not found.");
            await SelectChatAsync(conversation.Recipient);
            await WaitForTrackPageLiveAsync(() => chatMessageItems.Count > 0 && chatColumns.ActualWidth > 760);
            Console.WriteLine($"VIDEO_LIVE_CHAT_READY: rows={chatMessageItems.Count}, viewport={Window.ClientSize}");
            VerifyChatVideoDirtyClip();
            await VerifyChatVideoPlaybackBackdropAsync(path);
            Console.WriteLine("VIDEO_LIVE_CHAT_OK: real session/chat, local video playback, no message sends");
        }
        catch (Exception error)
        { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine("VIDEO_LIVE_CHAT_FAILED: " + error); }
        finally { Window.Close(); }
    }
}
