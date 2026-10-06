using System.Collections.ObjectModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal enum LoadingRowStyle { Card, CardRow, Waveform, Compact }
internal sealed record LoadingSlot(int Index);

// Keep one notifying source for the lifetime of a page. Replacing ItemsSource on every
// response discards measured row heights and the virtual presenter's scroll anchor.
internal sealed class PaginationItems
{
    private readonly ObservableCollection<object> items = [];
    private object[] data = [];
    private int loadingCount;
    private readonly Func<object, object, bool> equal;
    private readonly Func<object, object> key;
    public ISelectableItemsView View { get; }
    public LoadingRowStyle Style { get; set; }
    public int LoadingCount => loadingCount;
    public int DataCount => data.Length;

    public PaginationItems(LoadingRowStyle style, Func<object, object> key, Func<object, object, bool>? equal = null)
    {
        Style = style; this.equal = equal ?? Equals; this.key = key;
        View = ItemsView.Create(items, item => item switch
        { SoundCloudTrack track => track.Title, LibraryItem entry => entry.Title, _ => "" },
            item => item switch { SoundCloudTrack track => track.Id, LibraryItem entry => entry.Key, _ => item });
    }

    public void SetData(IEnumerable<object> value) { data = value.ToArray(); Refresh(); }
    public void SetLoading(int count) { if (loadingCount == count) return; loadingCount = count; Refresh(); }
    private void Refresh()
    {
        var next = data.Concat(Enumerable.Range(0, loadingCount).Select(index => (object)new LoadingSlot(index))).ToArray();
        var prefix = 0;
        while (prefix < Math.Min(items.Count, next.Length) && equal(items[prefix], next[prefix])) prefix++;
        var wantedKeys = next.Select(key).ToHashSet();
        for (var i = prefix; i < next.Length; i++)
        {
            if (i >= items.Count) { items.Add(next[i]); continue; }
            if (equal(items[i], next[i])) continue;
            if (items[i] is LoadingSlot || Equals(key(items[i]), key(next[i])) || !wantedKeys.Contains(key(items[i])))
            { items[i] = next[i]; continue; }
            var match = -1;
            for (var j = i + 1; j < items.Count; j++)
                if (Equals(key(items[j]), key(next[i]))) { match = j; break; }
            if (match < 0) items.Insert(i, next[i]);
            else
            {
                items.Move(match, i);
                if (!equal(items[i], next[i])) items[i] = next[i];
            }
        }
        while (items.Count > next.Length) items.RemoveAt(items.Count - 1);
    }
}

internal sealed class PaginationTemplate(IDataTemplate contentTemplate, Func<LibrarySkeleton> createSkeleton) : IDataTemplate
{
    public FrameworkElement Build(TemplateContext context)
    {
        var content = contentTemplate.Build(context);
        var skeleton = createSkeleton(); skeleton.IsVisible = false;
        context.Register("page-item-content", content); context.Register("page-item-skeleton", skeleton);
        return new Grid().Columns("*").Rows("Auto").Children(content, skeleton);
    }
    public void Bind(FrameworkElement view, object? item, int index, TemplateContext context)
    {
        var content = context.Get<FrameworkElement>("page-item-content");
        var skeleton = context.Get<LibrarySkeleton>("page-item-skeleton");
        var loading = item is LoadingSlot;
        content.IsVisible = !loading; skeleton.IsVisible = loading; skeleton.SetActive(loading);
        if (!loading) contentTemplate.Bind(content, item, index, context);
    }
    public void Unbind(FrameworkElement view, object? item, int index, TemplateContext context)
    {
        context.Get<LibrarySkeleton>("page-item-skeleton").SetActive(false);
        if (item is not LoadingSlot) contentTemplate.Unbind(context.Get<FrameworkElement>("page-item-content"), item, index, context);
    }
}
