using Pookie.Localization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Aprillz.MewUI;

namespace Pookie.App.Browser;

internal sealed unsafe partial class MacEmbeddedBrowser
{
    private const string ProfileMarker = ".pookie-webkit-data";
    internal static bool OwnsProfileData(string profile) => File.Exists(Path.Combine(profile, ProfileMarker));
    internal static string ProfileIdentifier(string profile) => new Guid(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile))))[..16]).ToString("D");

    private static bool IsDefaultProfile(string profile) => string.Equals(Path.GetFullPath(profile),
        Path.GetFullPath(BrowserProfile.DefaultPath()), StringComparison.Ordinal);

    private static nint WebsiteDataStore(string profile, bool isolated)
    {
        var type = Class("WKWebsiteDataStore");
        if (isolated) return Send(type, Sel("nonPersistentDataStore"));
        if (GetBoolArg(type, Sel("respondsToSelector:"), Sel("dataStoreForIdentifier:")))
        {
            var id = SendArg(Send(Class("NSUUID"), Sel("alloc")), Sel("initWithUUIDString:"), Text(ProfileIdentifier(profile)));
            try { return SendArg(type, Sel("dataStoreForIdentifier:"), id); }
            finally { Release(id); }
        }
        // macOS <14 has exactly one persistent WK store. Only the canonical app
        // profile may use it; custom data roots remain explicitly nonpersistent.
        return Send(type, Sel(IsDefaultProfile(profile) ? "defaultDataStore" : "nonPersistentDataStore"));
    }

    private static void MarkProfile(string profile, bool isolated)
    {
        if (isolated) return;
        var modern = GetBoolArg(Class("WKWebsiteDataStore"), Sel("respondsToSelector:"), Sel("dataStoreForIdentifier:"));
        if (!modern && !IsDefaultProfile(profile)) return;
        Pookie.App.Storage.AppDataPaths.CreatePrivateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, ProfileMarker), modern ? "identifier-store" : "legacy-default-store");
    }

    internal static Task ClearProfileAsync(string profile, CancellationToken token = default)
    {
        if (!OperatingSystem.IsMacOS()) return Task.CompletedTask;
        if (!OwnsProfileData(profile)) return Task.CompletedTask;
        var dispatcher = Application.Current.Dispatcher ??
            throw new InvalidOperationException(UiText.Get("browser.mac.clearOnUiThread"));
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Clear()
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (Class("WKWebsiteDataStore") == 0) NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");
                var type = Class("WKWebsiteDataStore");
                if (!GetBoolArg(type, Sel("respondsToSelector:"), Sel("dataStoreForIdentifier:")) && !IsDefaultProfile(profile))
                { result.TrySetResult(); return; } // Old custom stores were nonpersistent.
                var tasks = new List<Task> { ClearStore(WebsiteDataStore(profile, isolated: false)) };
                // Releases cookies left by older Pookie versions, which always
                // used the default store, without touching another custom root.
                if (IsDefaultProfile(profile) && GetBoolArg(type, Sel("respondsToSelector:"), Sel("dataStoreForIdentifier:")))
                    tasks.Add(ClearStore(Send(type, Sel("defaultDataStore"))));
                _ = Task.WhenAll(tasks).ContinueWith(done =>
                {
                    if (done.Exception != null) result.TrySetException(done.Exception.InnerExceptions);
                    else result.TrySetResult();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            catch (Exception error) { result.TrySetException(error); }
        }
        if (dispatcher.IsOnUIThread) Clear(); else dispatcher.BeginInvoke(Clear);
        return result.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
    }

    private static Task ClearStore(nint store)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = CompletionBlock(completed, (nint)(delegate* unmanaged<nint, void>)&ProfileCleared);
        try { SendThree(store, Sel("removeDataOfTypes:modifiedSince:completionHandler:"),
            Send(Class("WKWebsiteDataStore"), Sel("allWebsiteDataTypes")), Send(Class("NSDate"), Sel("distantPast")), block); }
        finally { BlockRelease(block); }
        return completed.Task;
    }

    [UnmanagedCallersOnly]
    private static void ProfileCleared(nint block) => ((TaskCompletionSource)GCHandle.FromIntPtr(((SnapshotBlock*)block)->Context).Target!).TrySetResult();

    private static nint CompletionBlock(object result, nint invoke)
    {
        if (snapshotDescriptor == 0)
        {
            stackBlockClass = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteStackBlock");
            var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
            descriptor->Size = (nuint)sizeof(SnapshotBlock);
            descriptor->Copy = (nint)(delegate* unmanaged<nint, nint, void>)&CopySnapshotBlock;
            descriptor->Dispose = (nint)(delegate* unmanaged<nint, void>)&DisposeSnapshotBlock;
            snapshotDescriptor = (nint)descriptor;
        }
        var block = (SnapshotBlock*)NativeMemory.AllocZeroed((nuint)sizeof(SnapshotBlock));
        block->Isa = stackBlockClass; block->Flags = 1 << 25;
        block->Invoke = invoke; block->Descriptor = snapshotDescriptor;
        block->Context = GCHandle.ToIntPtr(GCHandle.Alloc(result));
        var copied = BlockCopy((nint)block);
        NativeMemory.Free(block);
        return copied;
    }

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendThree(nint obj, nint sel, nint first, nint second, nint third);
}
