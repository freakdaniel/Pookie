using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    private readonly PageCollection items = [];
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
            item => item is LoadingSlot ? item : this.key(item));
    }

    public void SetData(IEnumerable<object> value) { data = value.ToArray(); Refresh(); }
    public void SetLoading(int count) { if (loadingCount == count) return; loadingCount = count; Refresh(); }
    private void Refresh()
    {
        var next = data.Concat(Enumerable.Range(0, loadingCount).Select(index => (object)new LoadingSlot(index))).ToArray();
        var prefix = 0;
        while (prefix < Math.Min(items.Count, next.Length) && equal(items[prefix], next[prefix])) prefix++;
        if (prefix == items.Count && prefix == next.Length) return;
        if (prefix == items.Count) { items.AddRange(next[prefix..]); return; }
        if (prefix == next.Length) { items.RemoveTail(prefix); return; }
        var wantedKeys = next.Select(key).ToHashSet();
        for (var i = prefix; i < next.Length; i++)
        {
            if (i >= items.Count) { items.AddRange(next[i..]); break; }
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
        if (items.Count > next.Length) items.RemoveTail(next.Length);
    }

    // Report one range change for a page, rather than invalidating bindings and
    // rescanning the selected key for every individual appended track. Keep
    // incremental notifications so the presenter retains its scroll anchor.
    private sealed class PageCollection : ObservableCollection<object>
    {
        public void AddRange(object[] added)
        {
            CheckReentrancy();
            var start = Count;
            foreach (var item in added) Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, start));
        }

        public void RemoveTail(int start)
        {
            CheckReentrancy();
            var removed = this.Skip(start).ToArray();
            while (Count > start) Items.RemoveAt(Count - 1);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, start));
        }
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
