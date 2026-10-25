// SPDX-License-Identifier: BSD-3-Clause
using System.Collections;

namespace Pookie.App.Storage;

internal sealed class CacheBudget(long maximumBytes)
{
    internal readonly object Sync = new();
    private readonly LinkedList<(long Bytes, Action Remove)> entries = new();
    private long bytes;
    internal long Bytes { get { lock (Sync) return bytes; } }
    internal LinkedListNode<(long Bytes, Action Remove)> Add(long weight, Action remove)
    {
        var node = entries.AddLast((Math.Max(1, weight), remove)); bytes += node.Value.Bytes; return node;
    }
    internal void Touch(LinkedListNode<(long Bytes, Action Remove)> node) { entries.Remove(node); entries.AddLast(node); }
    internal void Remove(LinkedListNode<(long Bytes, Action Remove)> node) { entries.Remove(node); bytes -= node.Value.Bytes; }
    internal void Trim() { while (bytes > maximumBytes && entries.First != null) entries.First.Value.Remove(); }
}

/// <summary>Entry and byte bounded LRU. Eviction releases cache ownership only;
/// images currently bound to controls remain valid. Shared aliases are charged
/// conservatively, so their combined retained pixels cannot exceed the budget.</summary>
internal sealed class ResourceCache<TKey,TValue>(int capacity, CacheBudget budget, Func<TValue,long> weight) : IDictionary<TKey,TValue>, IReadOnlyDictionary<TKey,TValue> where TKey : notnull
{
    private readonly Dictionary<TKey,(TValue Value, LinkedListNode<(long Bytes, Action Remove)> Node, LinkedListNode<TKey> Local)> items = new();
    private readonly LinkedList<TKey> recency = new();
    public TValue this[TKey key] { get { if (TryGetValue(key,out var value)) return value; throw new KeyNotFoundException(); } set { lock (budget.Sync) { Remove(key); items[key]=(value,budget.Add(weight(value),()=>Remove(key)),recency.AddLast(key)); while(items.Count>capacity) Remove(recency.First!.Value); budget.Trim(); } } }
    public bool TryGetValue(TKey key,out TValue value) { lock(budget.Sync) { if(items.TryGetValue(key,out var entry)){budget.Touch(entry.Node);recency.Remove(entry.Local);recency.AddLast(entry.Local);value=entry.Value;return true;}value=default!;return false;} }
    public bool Remove(TKey key) { lock(budget.Sync) { if(!items.Remove(key,out var entry))return false;budget.Remove(entry.Node);recency.Remove(entry.Local);return true;} }
    public void Clear() { lock(budget.Sync) { foreach(var entry in items.Values)budget.Remove(entry.Node);items.Clear();recency.Clear(); } }
    public int Count { get { lock(budget.Sync)return items.Count; } }
    public ICollection<TKey> Keys { get { lock(budget.Sync)return items.Keys.ToArray(); } }
    public ICollection<TValue> Values { get { lock(budget.Sync)return items.Values.Select(p=>p.Value).ToArray(); } }
    IEnumerable<TKey> IReadOnlyDictionary<TKey,TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey,TValue>.Values => Values;
    public bool IsReadOnly => false;
    public bool ContainsKey(TKey key) { lock(budget.Sync)return items.ContainsKey(key); }
    public void Add(TKey key,TValue value) { lock(budget.Sync){if(items.ContainsKey(key))throw new ArgumentException("Duplicate cache key.");this[key]=value;} }
    public void Add(KeyValuePair<TKey,TValue> pair)=>Add(pair.Key,pair.Value);
    public bool Contains(KeyValuePair<TKey,TValue> pair)=>TryGetValue(pair.Key,out var value)&&EqualityComparer<TValue>.Default.Equals(value,pair.Value);
    public bool Remove(KeyValuePair<TKey,TValue> pair) { lock(budget.Sync)return Contains(pair)&&Remove(pair.Key); }
    public void CopyTo(KeyValuePair<TKey,TValue>[] array,int index) { foreach(var pair in this)array[index++]=pair; }
    public IEnumerator<KeyValuePair<TKey,TValue>> GetEnumerator() { lock(budget.Sync)return items.Select(p=>new KeyValuePair<TKey,TValue>(p.Key,p.Value.Value)).ToArray().AsEnumerable().GetEnumerator(); }
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}

/// <summary>Deduplicates admitted work, bounds active ownership, and separates
/// in-flight tasks from completed results. Waiting admission honors cancellation.</summary>
internal sealed class AsyncResourceCache<TKey, TValue>(int capacity, CacheBudget budget, Func<TValue, long> weight)
    where TKey : notnull where TValue : class
{
    private sealed record Cached(TValue? Value, Task<TValue?> Task);
    private sealed record Request(Task<TValue?> Task, CancellationToken? Owner);
    private readonly object sync = new();
    private readonly Dictionary<TKey, Request> running = new();
    private readonly ResourceCache<TKey, Cached> completed = new(capacity, budget, entry => entry.Value == null ? 1 : weight(entry.Value));
    private readonly SemaphoreSlim admission = new(32, 32);
    internal int Count { get { lock (sync) return running.Count + completed.Count; } }
    internal int PendingCount { get { lock (sync) return running.Count; } }

    // Prewarming preserves the same shared promise as regular loading.
    internal Task<TValue?> this[TKey key]
    {
        get
        {
            lock (sync)
            {
                if (running.TryGetValue(key, out var request)) return request.Task;
                if (completed.TryGetValue(key, out var entry)) return entry.Task;
                throw new KeyNotFoundException();
            }
        }
        set
        {
            lock (sync)
            {
                completed.Remove(key);
                if (value.IsCompletedSuccessfully)
                {
                    completed[key] = new(value.Result, value);
                    running.Remove(key);
                    return;
                }
                running[key] = new(value, null);
            }
            _ = ObserveAsync(key, value);
        }
    }
    private async Task ObserveAsync(TKey key, Task<TValue?> task)
    {
        try
        {
            var value = await task.ConfigureAwait(false);
            lock (sync)
                if (running.GetValueOrDefault(key)?.Task == task && value != null) completed[key] = new(value, task);
        }
        catch { /* Failed prewarming remains retryable. */ }
        finally { lock (sync) if (running.GetValueOrDefault(key)?.Task == task) running.Remove(key); }
    }

    private static Task<TValue?> Wait(Request request, CancellationToken token) =>
        request.Owner == null || request.Owner == token ? request.Task : request.Task.WaitAsync(token);

    internal Task<TValue?> GetAsync(TKey key, Func<Task<TValue?>> fetch, CancellationToken token)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<TValue?>(token);
        lock (sync)
        {
            if (completed.TryGetValue(key, out var cached)) return cached.Task;
            if (running.TryGetValue(key, out var existing)) return Wait(existing, token);
        }
        return admission.Wait(0) ? Admit(key, fetch, token) : QueueAsync(key, fetch, token);
    }
    private async Task<TValue?> QueueAsync(TKey key, Func<Task<TValue?>> fetch, CancellationToken token)
    {
        await admission.WaitAsync(token).ConfigureAwait(false);
        return await Admit(key, fetch, token).ConfigureAwait(false);
    }
    private Task<TValue?> Admit(TKey key, Func<Task<TValue?>> fetch, CancellationToken token)
    {
        TaskCompletionSource<TValue?> owner;
        lock (sync)
        {
            if (completed.TryGetValue(key, out var cached)) { admission.Release(); return cached.Task; }
            if (running.TryGetValue(key, out var existing)) { admission.Release(); return Wait(existing, token); }
            owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
            running[key] = new(owner.Task, token);
        }
        _ = FetchAsync(key, fetch, owner, token);
        return owner.Task;
    }
    private async Task FetchAsync(TKey key, Func<Task<TValue?>> fetch, TaskCompletionSource<TValue?> completion, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var value = await fetch().ConfigureAwait(false);
            lock (sync)
                if (value != null && !token.IsCancellationRequested && running.GetValueOrDefault(key)?.Task == completion.Task)
                    completed[key] = new(value, completion.Task);
            completion.TrySetResult(value);
        }
        catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error) { completion.TrySetException(error); }
        finally
        {
            lock (sync) if (running.GetValueOrDefault(key)?.Task == completion.Task) running.Remove(key);
            admission.Release();
        }
    }
}
