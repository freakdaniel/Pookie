using System.Security.Cryptography;

namespace Pookie.App.Messages;

// Shared by visible rows; completed GIF bytes live in the account's encrypted
// disk cache, not an unbounded in-memory download dictionary.
internal sealed class ChatGifMediaCache(ChatMediaDiskCache? disk, Func<string, CancellationToken, Task<byte[]>> download, CancellationToken account)
{
    private sealed class Entry(CancellationTokenSource cancellation)
    {
        internal readonly CancellationTokenSource Cancellation = cancellation;
        internal Task<byte[]> Task = null!;
        internal int Users;
        internal bool Completed;
    }
    private readonly object sync = new();
    private readonly Dictionary<string, Entry> pending = [];
    private readonly SemaphoreSlim admission = new(2, 2);

    internal async Task<byte[]> GetAsync(ChatMediaReference reference, string url, long sender, long recipient, CancellationToken setting, CancellationToken caller)
    {
        if (!KlipyClient.IsMediaUrl(url)) throw new InvalidDataException("Недопустимый адрес GIF.");
        caller.ThrowIfCancellationRequested();
        var id = reference.Code;
        Entry entry;
        lock (sync)
        {
            if (!pending.TryGetValue(id, out entry!))
            {
                if (pending.Count >= 64) throw new IOException("Очередь загрузки GIF заполнена.");
                entry = new(CancellationTokenSource.CreateLinkedTokenSource(account, setting));
                pending[id] = entry;
                var owned = entry;
                entry.Task = Task.Run(() => LoadAsync(owned, reference, url, sender, recipient));
                _ = entry.Task.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            entry.Users++;
        }
        try { return await entry.Task.WaitAsync(caller).ConfigureAwait(false); }
        finally
        {
            lock (sync)
            {
                if (--entry.Users == 0)
                {
                    if (entry.Completed) entry.Cancellation.Dispose();
                    else
                    {
                        if (pending.GetValueOrDefault(id) == entry) pending.Remove(id);
                        entry.Cancellation.Cancel();
                    }
                }
            }
        }
    }
    private async Task<byte[]> LoadAsync(Entry entry, ChatMediaReference reference, string url, long sender, long recipient)
    {
        var token = entry.Cancellation.Token; var entered = false;
        try
        {
            await admission.WaitAsync(token).ConfigureAwait(false); entered = true;
            if (disk != null && await disk.ReadAsync(reference.Code + ":gif", token).ConfigureAwait(false) is { } encrypted)
            {
                try
                {
                    var package = ChatMediaCrypto.Decrypt(encrypted, reference.Key, sender, recipient);
                    if (package.GifUrl == url && package.Attachments is [{ MediaType: "image/gif", Data.Length: > 0 and <= 8 * 1024 * 1024 } image])
                        return image.Data;
                }
                catch (Exception error) when (error is CryptographicException or InvalidDataException or ArgumentException) { }
                finally { CryptographicOperations.ZeroMemory(encrypted); }
            }
            var bytes = await download(url, token).ConfigureAwait(false);
            await StoreAsync(reference, url, sender, recipient, bytes, token).ConfigureAwait(false);
            return bytes;
        }
        finally
        {
            if (entered) admission.Release();
            lock (sync)
            {
                if (pending.GetValueOrDefault(reference.Code) == entry) pending.Remove(reference.Code);
                entry.Completed = true;
                if (entry.Users == 0) entry.Cancellation.Dispose();
            }
        }
    }
    internal async Task StoreAsync(ChatMediaReference reference, string url, long sender, long recipient, byte[] bytes, CancellationToken token)
    {
        if (disk == null || bytes.Length is 0 or > 8 * 1024 * 1024 || !ChatGifAnimation.CanAnimate(bytes)) return;
        var encrypted = await Task.Run(() => ChatMediaCrypto.EncryptLegacy(new(sender, recipient, "", [new("animation.gif", "image/gif", bytes)], url), reference.Key), token).ConfigureAwait(false);
        try { await disk.PutAsync(reference.Code + ":gif", encrypted, token).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }
}
