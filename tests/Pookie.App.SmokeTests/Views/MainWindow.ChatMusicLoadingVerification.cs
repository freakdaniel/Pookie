using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatMusicLoadingAsync(ChatFixtureTransport transport)
    {
        const string single = "Один новый трек\nhttps://soundcloud.com/fixture/animated-single";
        const string multiple = "Вот https://soundcloud.com/fixture/animated-one и https://soundcloud.com/fixture/animated-two";
        const string waveformUrl = "https://wave.sndcdn.com/chat-track-fixture.json";
        var previousWaveform = waveformRequests[waveformUrl];
        var waveform = new TaskCompletionSource<float[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        waveformRequests[waveformUrl] = waveform.Task;
        transport.MusicReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new List<(double Width, double Reveal)>();
        ChatBubbleRow? groupBubble = null;
        void Sample()
        {
            if (groupBubble?.Attachments.Children.FirstOrDefault() is ChatCompactMusicView view)
                frames.Add((groupBubble.Bubble.ActualWidth, view.Loading.Reveal));
        }
        try
        {
            transport.AddIncoming(single); transport.AddOwn(multiple); await RefreshChatsAsync();
            await WaitForLikedLayoutAsync(() => ChatRows.Any(row => row.Message?.Body == multiple && row.Bubble.ActualWidth > 300) &&
                ChatRows.Any(row => row.Message?.Body == single && row.Bubble.ActualHeight > 150));
            ChatBubbleRow singleBubble = null!; ChatCompactMusicView[] group = []; ChatMusicView full = null!;
            await WaitForLikedLayoutAsync(() =>
            {
                groupBubble = ChatRows.Single(row => row.Message?.Body == multiple);
                singleBubble = ChatRows.Single(row => row.Message?.Body == single);
                group = groupBubble.Attachments.Children.OfType<ChatCompactMusicView>().ToArray();
                full = (ChatMusicView)singleBubble.Attachments.Children[0];
                return group.Length == 2 && group.All(view => view.IsCurrent() && view.Loading.Skeleton.Running) &&
                    full.IsCurrent() && full.Loading.Skeleton.Running && !chatScrollBottom && groupBubble.Motion.Opacity > .999;
            });
            var loadedGroup = groupBubble ?? throw new InvalidOperationException("Music group did not realize.");
            if (group.Any(view => view.Row.Root.Opacity != 0 || !view.Loading.Loading) || full.Row.Root.Opacity != 0 || loadedGroup.Body.MusicCount != 2)
                throw new InvalidOperationException("Music placeholders exposed fake metadata/raw URLs before loading.");
            CaptureUiPreview("chats-music-loading");
            var initialWidth = loadedGroup.Bubble.ActualWidth;
            chatViewport.BeforePaint += Sample;
            transport.MusicReady.SetResult();
            await WaitForLikedLayoutAsync(() => group.All(view => view.Loading.Reveal == 1) && !loadedGroup.Content.IsResizing && loadedGroup.Bubble.ActualWidth > initialWidth + 40);
            var finalWidth = loadedGroup.Bubble.ActualWidth;
            if (finalWidth > 485 || !frames.Any(frame => frame.Width > initialWidth + 1 && frame.Width < finalWidth - 1) ||
                !frames.Any(frame => frame.Reveal is > 0 and < 1) || !full.Loading.Loading || !full.Loading.Skeleton.Running)
                throw new InvalidOperationException("Music metadata snapped its size/opacity or revealed an empty waveform before loading.");
            waveform.SetResult([.2f, .9f, .4f, .7f]);
            // Skeleton shutdown runs on completion, after eased opacity can
            // already exceed .999. Wait for the terminal frame before asserting it.
            await WaitForLikedLayoutAsync(() => full.Row.Waveform.HasSamples && full.Loading.Reveal == 1 &&
                loadedGroup.MusicBackdrop.Opacity > .999 && !singleBubble.Content.IsResizing);
            if (!ReferenceEquals(full, singleBubble.Attachments.Children[0]) || full.Loading.Skeleton.Running || group.Any(view => view.Loading.Skeleton.Running))
                throw new InvalidOperationException("Music loading replaced its row or retained a running skeleton after reveal.");
            CaptureUiPreview("chats-music-loaded");
        }
        finally
        {
            chatViewport.BeforePaint -= Sample;
            transport.MusicReady?.TrySetResult(); transport.MusicReady = null;
            waveform.TrySetResult([.2f, .9f]); waveformRequests[waveformUrl] = previousWaveform;
        }
    }
}
