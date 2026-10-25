using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatNavigationUiAsync()
    {
        try
        {
            var transport = new ChatFixtureTransport();
            chatMedia.Dispose();chatMedia=new(new ChatFixtureMediaHandler());
            api.Session = new("chatfixture", "fixture-token", "Test"); api.BrowserTransport = transport;
            me = new() { Id = 42, Username = "Ты" }; signedIn.Value = true;
            await ShowWorkspaceAsync();
            Window.Activate();await WaitForLikedLayoutAsync(()=>Window.IsActive);

            // Fractional pixel constraints must converge to a painted frame, including
            // the empty thread shown beside the inbox before a conversation is selected.
            foreach (var width in new[] { 1280, 1173, 1001, 820, 1280 })
            {
                Window.WindowSize = WindowSize.Resizable(width, 900, minWidth: 760, minHeight: 600);
                Console.WriteLine($"CHAT_NAVIGATION_OPEN: width={width}, dpi={Window.Dpi}");
                await NavigateAsync(Page.Chats);
                chatTimer.Stop();
                await WaitForLoginFrameAsync();
                await WaitForLikedLayoutAsync(() => chatInboxBindings.Count >= 2);
                if (chatRecipient != null || chatInput.IsEnabled)
                    throw new InvalidOperationException("Opening the inbox selected a conversation or enabled its composer.");
                BeginNavigation(new(Page.Home));
                await WaitForLoginFrameAsync();
            }

            await NavigateAsync(Page.Chats);
            chatTimer.Stop();
            await SelectChatAsync(transport.Other);
            var viewer = (ScrollViewer)chatMessagesView.FindVisualChild<ScrollViewer>()!;
            await WaitForLikedLayoutAsync(() => chatMessages.Count == 30 && !chatScrollBottom);
            await WaitForLoginFrameAsync();
            if (SmoothScroll.Maximum(viewer) - viewer.VerticalOffset > 2 / Window.DpiScale)
                throw new InvalidOperationException("Opening a conversation did not settle at its latest message.");

            viewer.SetScrollOffsets(0, SmoothScroll.Maximum(viewer) * .45);
            await WaitForLoginFrameAsync();
            var anchor = ChatRows.Where(row => row.Bound && row.Message != null &&
                    row.Root.Bounds.Bottom > viewer.Bounds.Y + 1 && row.Root.Bounds.Y < viewer.Bounds.Bottom)
                .OrderBy(row => row.Root.Bounds.Y).First();
            var anchorId = anchor.Message!.Id;
            var anchorY = anchor.Root.Bounds.Y - viewer.Bounds.Y;
            await SelectChatAsync(transport.Third);
            await WaitForLoginFrameAsync();
            await SelectChatAsync(transport.Other);
            await WaitForLikedLayoutAsync(() => chatRestoreOffset == null && !chatScrollBottom);
            await WaitForLoginFrameAsync();
            var restored = ChatRows.FirstOrDefault(row => row.Bound && row.Message?.Id == anchorId);
            if (restored == null || Math.Abs(restored.Root.Bounds.Y - viewer.Bounds.Y - anchorY) > 2 / Window.DpiScale)
                throw new InvalidOperationException("Switching conversations did not restore the visible history anchor.");

            RequestChatBottom();
            await WaitForLikedLayoutAsync(() => !chatScrollBottom);
            var composerHeight = chatComposer.ActualHeight;
            chatInput.Text = "Первая строка\nВторая строка\nТретья строка";
            await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && chatComposer.ActualHeight > composerHeight + 10);
            await WaitForLoginFrameAsync();
            if (SmoothScroll.Maximum(viewer) - viewer.VerticalOffset > 2 / Window.DpiScale)
                {
                    CaptureUiPreview("chats-composer-anchor-failure");
                    throw new InvalidOperationException($"Growing the composer moved the latest message out of view: offset={viewer.VerticalOffset}, max={SmoothScroll.Maximum(viewer)}, viewport={viewer.Bounds}, bottom={chatWasAtBottom}, following={chatScrollBottom}, restore={chatRestoreOffset}, width={chatViewportWidth}, height={chatViewportHeight}, composer={chatComposer.Bounds}.");
                }
            chatInput.Text = "";
            await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && Math.Abs(chatComposer.ActualHeight - composerHeight) < 2 / Window.DpiScale);

            BeginNavigation(new(Page.Home));
            await WaitForLoginFrameAsync();
            await NavigateAsync(Page.Chats);
            chatTimer.Stop();
            await WaitForLoginFrameAsync();
            if (chatRecipient?.Id != transport.Other.Id || chatMessages.Count != 30 || !chatInput.IsEnabled)
                throw new InvalidOperationException("Reopening chats lost the selected conversation.");
            await VerifyChatWorkspaceAsync(transport);
            Console.WriteLine($"UI_CHAT_NAVIGATION_OK: inbox, repeated navigation, resizing, history anchor, growing composer and bottom scroll; dpi={Window.Dpi}");
        }
        catch (Exception error) { VerificationFailure = error; Console.Error.WriteLine(error); }
        finally { Window.Close(); }
    }
}
