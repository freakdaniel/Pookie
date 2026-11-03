using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private CancellationTokenSource? searchLinkLoading;
    private Task<LibraryItem>? searchLinkTask;
    private string? searchLinkUrl;
    private WebSession? searchLinkSession;

    private void ClearSearchLink()
    {
        searchLinkLoading?.Cancel(); searchLinkLoading?.Dispose(); searchLinkLoading = null;
        searchLinkTask = null; searchLinkUrl = null; searchLinkSession = null;
        topSearchBar.ShowLink(null);
    }

    private Task<LibraryItem> GetSearchLinkAsync(string url)
    {
        if (searchLinkUrl == url && searchLinkSession == api.Session && searchLinkTask is { IsCanceled: false, IsFaulted: false })
            return searchLinkTask;
        ClearSearchLink();
        searchLinkUrl = url; searchLinkSession = api.Session;
        searchLinkLoading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        return searchLinkTask = ResolveSearchLinkAsync(url, searchLinkLoading.Token);
    }

    private async Task<LibraryItem> ResolveSearchLinkAsync(string url, CancellationToken token)
    {
        // Debounce typed URLs; a paste is represented as a chip before the next frame.
        await Task.Delay(200, token);
        return await ResolveLinkPreviewItemAsync(url, token);
    }

    private void RefreshSearchLinkPreview()
    {
        var url = query.Value.Trim();
        if (!searching.Value || !CanUseWorkspace || demo || !SoundCloudWebClient.IsSoundCloudUrl(url))
        { ClearSearchLink(); return; }
        var task = GetSearchLinkAsync(url); var session = api.Session;
        var token = searchLinkLoading!.Token;
        topSearchBar.ShowLink(LinkPreviewData.Pending(url));
        Run(async () =>
        {
            bool Current() => !disposed && searching.Value && searchLinkTask == task && api.Session == session && query.Value.Trim() == url;
            try
            {
                await task;
                if (!Current()) return;
                var (item, cover) = await LoadLinkPreviewAsync(url, token);
                if (!Current()) return;
                topSearchBar.ShowLink(new(url, item.Title, cover));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception error) when (error is SoundCloudException or HttpRequestException or System.Text.Json.JsonException)
            {
                if (Current()) topSearchBar.ShowMissingLink(url);
                Pookie.Logging.AppLog.For("Pookie.Search").Debug(error, "Не удалось распознать ссылку поиска");
            }
        });
    }

    private async Task OpenSearchLinkAsync(string url)
    {
        var generation = navigationGeneration; var session = api.Session;
        var token = loading?.Token ?? lifetime.Token;
        LibraryItem item;
        try { item = await GetSearchLinkAsync(url).WaitAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        if (disposed || generation != navigationGeneration || api.Session != session || query.Value.Trim() != url) return;
        await OpenLibraryCardAsync(item);
    }
}
