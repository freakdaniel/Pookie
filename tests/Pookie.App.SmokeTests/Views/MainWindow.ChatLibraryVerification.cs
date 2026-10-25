using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatLibraryPreviewsAsync()
    {
        var previousMedia = chatMedia; var previousPreferences = preferences;
        var handler = new ChatFixtureMediaHandler(); chatMedia = new(handler);
        preferences = preferences with { ChatAutoLoad = true };
        try
        {
            var package = new ChatMediaPackage(90,42,"",[
                new("same","application/octet-stream",[1,2,3]),
                new("same","image/png",Fixture("chat-small.png")),
                new("same","image/jpeg",Fixture("chat-4k.jpg"))]);
            var message = new ChatMessage("library-cold-photo",90,handler.Add(package,legacy:true),DateTimeOffset.UtcNow);
            await chatHistory!.SavePageAsync(42,90,new([message],null));
            await chatHistory.SaveMediaMetadataAsync(90,message.Id,Files(package),"");
            handler.DelayDownload = true;
            await RenderChatMediaLibraryAsync("Фото");
            await WaitForLikedLayoutAsync(() => handler.DownloadReady != null && chatLibraryPreviews.Count == 2 && chatLibraryPreviews.Any(e=>e.Tile.Loading && e.Tile.Shimmering));
            CaptureUiPreview("chats-library-photo-skeletons");

            var library = chatMediaLibrary; var body = chatDetailsBody; var generation = chatDetailsGeneration;
            var identity = ((Grid)chatNormalIdentity).Children.OfType<Button>().Single(b=>b.Content is Grid);
            RouteWaveformPointer(SettingsCenter(chatName)); await Task.Delay(250);
            if (chatIdentityCard.HoverEnabled || identity.Background.A != 0)
                throw new InvalidOperationException("The conversation header still adds a hover background.");
            RouteWaveformClick(SettingsCenter(chatName));
            if (chatMediaLibrary != library || chatDetailsBody != body || chatDetailsGeneration != generation || chatMediaLibraryKind != "Фото")
                throw new InvalidOperationException("Clicking the open profile header rebuilt its information panel.");

            handler.DownloadReady!.TrySetResult();
            await WaitForLikedLayoutAsync(() => chatLibraryPreviewActive == 0 && chatLibraryPreviews.Count == 2 && chatLibraryPreviews.All(e=>e.Tile.Ready && !e.Tile.Animating));
            if (handler.Downloads != 1 || !chatLibraryPreviews.Select(e=>e.Index).Order().SequenceEqual(new[]{1,2}) || ReferenceEquals(chatLibraryPreviews[0].Tile.Source,chatLibraryPreviews[1].Tile.Source))
                throw new InvalidOperationException("Cold media previews lost original attachment indices or duplicated their download.");
            CaptureUiPreview("chats-library-photo-previews");

            var unknown = new ChatMessage("library-unknown-photo",90,handler.Add(new(90,42,"",[new("new.png","image/png",Fixture("chat-small.png"))])),DateTimeOffset.UtcNow.AddSeconds(1));
            await chatHistory.SavePageAsync(42,90,new([unknown],null));
            await RenderChatMediaLibraryAsync("Фото");
            await WaitForLikedLayoutAsync(() => chatLibraryPreviewActive == 0 && chatLibraryPreviews.Count == 3 && chatLibraryPreviews.All(e=>e.Index >= 0 && e.Tile.Ready && !e.Tile.Animating));

            var videoPackage = new ChatMediaPackage(90,42,"",[new("clip.mp4","video/mp4",Fixture("chat-video-portrait.mp4"))]);
            var video = new ChatMessage("library-cold-video",90,handler.Add(videoPackage,legacy:true),DateTimeOffset.UtcNow.AddSeconds(2));
            await chatHistory.SavePageAsync(42,90,new([video],null));
            await chatHistory.SaveMediaMetadataAsync(90,video.Id,Files(videoPackage),"");
            await RenderChatMediaLibraryAsync("Видео");
            await WaitForLikedLayoutAsync(() => chatLibraryPreviewActive == 0 && chatLibraryPreviews is [{Tile.Ready:true,Tile.Animating:false}]);
            CaptureUiPreview("chats-library-video-preview");

            var retryPackage = new ChatMediaPackage(90,42,"",[new("retry.png","image/png",Fixture("chat-small.png"))]);
            var retry = new ChatMessage("library-retry-photo",90,handler.Add(retryPackage,legacy:true),DateTimeOffset.UtcNow.AddSeconds(3));
            await chatHistory.SavePageAsync(42,90,new([retry],null));
            await chatHistory.SaveMediaMetadataAsync(90,retry.Id,Files(retryPackage),"");
            handler.FailNextDownload = true; await RenderChatMediaLibraryAsync("Фото");
            await WaitForLikedLayoutAsync(() => chatLibraryPreviews.Any(e=>e.Message.Id == retry.Id && e.Tile.Notice.Contains("Повторить")));
            var retryTile = chatLibraryPreviews.Single(e=>e.Message.Id == retry.Id).Tile;
            RouteWaveformClick(SettingsCenter(retryTile));
            await WaitForLikedLayoutAsync(() => chatLibraryPreviewActive == 0 && retryTile.Ready && !retryTile.Animating);

            await VerifyChatMediaPillClipAsync();
        }
        finally
        {
            handler.DownloadReady?.TrySetResult();
            ClearChatLibraryPreviews(); chatMedia.Dispose(); chatMedia = previousMedia; preferences = previousPreferences;
        }

        static MediaFile[] Files(ChatMediaPackage package) => package.Attachments.Select(a=>new MediaFile(a.Name,a.MediaType,a.Data.Length,a.AsFile,a.Video,a.Voice)).ToArray();
        static byte[] Fixture(string name)
        {
            using var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("Pookie.TestFixtures.Media."+name)!;
            using var output = new MemoryStream(); resource.CopyTo(output); return output.ToArray();
        }
    }
    private Task VerifyChatMediaPillClipAsync()
    {
        using var pill = new ChatTabs();
        foreach (var name in new[]{"Музыка","Фото","Видео","Файлы","Ссылки"}) pill.AddTab(name,()=>{});
        var bounds = new Rect(30,20,180,48); pill.Measure(bounds.Size); pill.Arrange(bounds);
        pill.ScrollBy(30); pill.Arrange(bounds);
        if (pill.SelectionBounds.X >= bounds.X || pill.SelectionBounds.Right <= bounds.X)
            throw new InvalidOperationException("The clipping fixture did not move the active pill across its left edge.");
        var scale = Window.DpiScale; var background = Color.FromRgb(100,40,20);
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels((int)(240*scale),(int)(100*scale),scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface); context.Clear(background); pill.Render(context); context.EndFrame();
        var cpu = (Aprillz.MewUI.Rendering.ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var index = (int)(44*scale)*cpu.StrideBytes+(int)(28*scale)*4;
        if (pixels[index] != background.B || pixels[index+1] != background.G || pixels[index+2] != background.R)
            throw new InvalidOperationException("The active media pill escapes the same mask that clips its labels.");
        return Task.CompletedTask;
    }
}
