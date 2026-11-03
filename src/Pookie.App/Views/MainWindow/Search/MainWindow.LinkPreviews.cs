using Aprillz.MewUI;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<string, Task<LibraryItem>> linkPreviewLoads = new(StringComparer.Ordinal);
    private WebSession? linkPreviewSession;
    private CancellationTokenSource? linkPreviewLifetime;

    private void ClearLinkPreviews()
    {
        linkPreviewLifetime?.Cancel(); linkPreviewLifetime?.Dispose(); linkPreviewLifetime = null;
        linkPreviewLoads.Clear(); linkPreviewSession = null;
    }

    private async Task<LibraryItem> ResolveLinkPreviewItemAsync(string url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (linkPreviewLifetime == null || linkPreviewSession != api.Session)
        {
            ClearLinkPreviews(); linkPreviewSession = api.Session;
            linkPreviewLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        }
        if (!linkPreviewLoads.TryGetValue(url, out var task))
        {
            while (linkPreviewLoads.Count >= 128) linkPreviewLoads.Remove(linkPreviewLoads.First().Key);
            task = chatMusicCache.TryGetValue(url, out var cached) ? Task.FromResult(cached) : api.ResolveLinkAsync(url, linkPreviewLifetime.Token);
            linkPreviewLoads[url] = task;
        }
        try { return await task.WaitAsync(token); }
        catch
        {
            if ((task.IsFaulted || task.IsCanceled) && linkPreviewLoads.GetValueOrDefault(url) == task) linkPreviewLoads.Remove(url);
            throw;
        }
    }

    private async Task<(LibraryItem Item, ImageSource? Cover)> LoadLinkPreviewAsync(string url, CancellationToken token)
    {
        var item = await ResolveLinkPreviewItemAsync(url, token);
        var cover = await GetCollectionArtworkAsync(item).WaitAsync(token);
        return (item, cover);
    }
}
