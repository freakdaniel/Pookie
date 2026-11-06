using System.Runtime.InteropServices;
using Aprillz.MewUI;

namespace Pookie.App.Browser;

// WKWebView is an NSView, attached to the NSView exposed by MewUI's macOS Window.Handle.
// Cocoa and WebKit run on MewUI's main thread and its existing NSApplication loop.
internal sealed unsafe partial class MacEmbeddedBrowser(EmbeddedBrowserEvents events) : IEmbeddedBrowser
{
    private readonly EmbeddedBrowserEvents events = events;
    private static readonly Dictionary<nint, MacEmbeddedBrowser> delegates = [];
    private static nint delegateClass;
    private static nint snapshotDescriptor, stackBlockClass;
    private readonly List<nint> pages = [];
    private nint parent, handler, content;
    private NativeRect frame;
    private bool visible, disposed;

    public Task InitializeAsync(nint parent, string profile, string script, bool isolated)
    {
        if (Class("WKWebView") == 0) NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");
        this.parent = parent;
        EnsureDelegateClass();
        handler = New(delegateClass); delegates.Add(handler, this);
        var configuration = New(Class("WKWebViewConfiguration"));
        content = New(Class("WKUserContentController"));
        try
        {
            SendTwo(content, Sel("addScriptMessageHandler:name:"), handler, Text("pookie"));
            var userScript = SendScript(Send(Class("WKUserScript"), Sel("alloc")), Sel("initWithSource:injectionTime:forMainFrameOnly:"), Text(script), 1, true);
            try { SendVoid(content, Sel("addUserScript:"), userScript); }
            finally { Release(userScript); }
            SendVoid(configuration, Sel("setUserContentController:"), content);
            // Match the existing macOS browser worker's persistent WK store. Fixtures
            // use a non-persistent store so tests never read or alter a real login.
            SendVoid(configuration, Sel("setWebsiteDataStore:"), Send(Class("WKWebsiteDataStore"), Sel(isolated ? "nonPersistentDataStore" : "defaultDataStore")));
            AddPage(configuration);
        }
        finally { Release(configuration); }
        return Task.CompletedTask;
    }

    private nint AddPage(nint configuration)
    {
        if (pages.Count > 0) SendBool(pages[^1], Sel("setHidden:"), true);
        var view = SendInit(Send(Class("WKWebView"), Sel("alloc")), Sel("initWithFrame:configuration:"), frame, configuration);
        pages.Add(view);
        SendVoid(view, Sel("setNavigationDelegate:"), handler);
        SendVoid(view, Sel("setUIDelegate:"), handler);
        SendVoid(parent, Sel("addSubview:"), view);
        SendBool(view, Sel("setHidden:"), !visible);
        return view;
    }

    public void Navigate(string url)
    {
        var address = SendArg(Class("NSURL"), Sel("URLWithString:"), Text(url));
        SendArg(pages[^1], Sel("loadRequest:"), SendArg(Class("NSURLRequest"), Sel("requestWithURL:"), address));
        events.SourceChanged(url);
    }
    public Task ExecuteAsync(string script)
    {
        SendTwo(pages[0], Sel("evaluateJavaScript:completionHandler:"), Text(script), 0);
        return Task.CompletedTask;
    }
    public void SetBounds(Rect area, double scale)
    {
        var parentBounds = ReadRect(parent, Sel("bounds"));
        var y = GetBool(parent, Sel("isFlipped")) ? area.Y : parentBounds.Height - area.Bottom;
        frame = new(area.X, y, Math.Max(1, area.Width), Math.Max(1, area.Height));
        if (pages.Count > 0) SendRect(pages[^1], Sel("setFrame:"), frame);
    }
    public void SetVisible(bool value)
    {
        visible = value;
        if (pages.Count > 0) SendBool(pages[^1], Sel("setHidden:"), !value);
    }
    public Task<byte[]> CaptureAsync()
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
        var result = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = (SnapshotBlock*)NativeMemory.AllocZeroed((nuint)sizeof(SnapshotBlock));
        block->Isa = stackBlockClass; block->Flags = 1 << 25; // BLOCK_HAS_COPY_DISPOSE
        block->Invoke = (nint)(delegate* unmanaged<nint, nint, nint, void>)&SnapshotCompleted;
        block->Descriptor = snapshotDescriptor; block->Context = GCHandle.ToIntPtr(GCHandle.Alloc(result));
        var copied = BlockCopy((nint)block);
        NativeMemory.Free(block);
        var configuration = New(Class("WKSnapshotConfiguration"));
        try
        {
            SendBool(configuration, Sel("setAfterScreenUpdates:"), false);
            SendTwo(pages[^1], Sel("takeSnapshotWithConfiguration:completionHandler:"), configuration, copied);
        }
        finally { Release(configuration); BlockRelease(copied); }
        return result.Task;
    }

    [StructLayout(LayoutKind.Sequential)] private struct SnapshotBlock { public nint Isa; public int Flags, Reserved; public nint Invoke, Descriptor, Context; }
    [StructLayout(LayoutKind.Sequential)] private struct BlockDescriptor { public nuint Reserved, Size; public nint Copy, Dispose; }
    [UnmanagedCallersOnly] private static void CopySnapshotBlock(nint destination, nint source) { /* The copied block retains the same GCHandle until its single final disposal. */ }
    [UnmanagedCallersOnly] private static void DisposeSnapshotBlock(nint block) => GCHandle.FromIntPtr(((SnapshotBlock*)block)->Context).Free();
    [UnmanagedCallersOnly]
    private static void SnapshotCompleted(nint block, nint image, nint error)
    {
        var result = (TaskCompletionSource<byte[]>)GCHandle.FromIntPtr(((SnapshotBlock*)block)->Context).Target!;
        try
        {
            if (image == 0 || error != 0) throw new InvalidOperationException("WKWebView не смог подготовить кадр анимации входа.");
            var bitmap = SendArg(Class("NSBitmapImageRep"), Sel("imageRepWithData:"), Send(image, Sel("TIFFRepresentation")));
            var data = SendPng(bitmap, Sel("representationUsingType:properties:"), 4, Send(Class("NSDictionary"), Sel("dictionary")));
            var length = (long)Send(data, Sel("length"));
            if (length is <= 0 or > 33554432) throw new InvalidOperationException("WKWebView вернул некорректный кадр входа.");
            var bytes = new byte[(int)length]; Marshal.Copy(Send(data, Sel("bytes")), bytes, 0, bytes.Length);
            result.TrySetResult(bytes);
        }
        catch (Exception failure) { result.TrySetException(failure); }
    }
    private static string Url(nint view) => String(Send(Send(view, Sel("URL")), Sel("absoluteString")));
    private void ClosePage(nint view)
    {
        if (disposed || pages.Count < 2 || pages[^1] != view) return;
        pages.RemoveAt(pages.Count - 1); RemovePage(view);
        SendRect(pages[^1], Sel("setFrame:"), frame); SendBool(pages[^1], Sel("setHidden:"), !visible);
        events.SourceChanged(Url(pages[^1]));
    }
    private static void RemovePage(nint page)
    {
        Send(page, Sel("stopLoading")); SendVoid(page, Sel("setNavigationDelegate:"), 0); SendVoid(page, Sel("setUIDelegate:"), 0);
        Send(page, Sel("removeFromSuperview")); Release(page);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var page in pages.AsEnumerable().Reverse()) RemovePage(page);
        pages.Clear();
        if (content != 0) { SendVoid(content, Sel("removeScriptMessageHandlerForName:"), Text("pookie")); Release(content); content = 0; }
        if (handler != 0) { delegates.Remove(handler); Release(handler); handler = 0; }
    }

    private static void EnsureDelegateClass()
    {
        if (delegateClass != 0) return;
        delegateClass = AllocateClass(Class("NSObject"), "PookieEmbeddedWebDelegate", 0);
        AddMethod(delegateClass, Sel("webView:decidePolicyForNavigationAction:decisionHandler:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&Decide, "v@:@@@?");
        AddMethod(delegateClass, Sel("userContentController:didReceiveScriptMessage:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&Message, "v@:@@");
        AddMethod(delegateClass, Sel("webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:"),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint>)&Popup, "@@:@@@@");
        AddMethod(delegateClass, Sel("webViewDidClose:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&Closed, "v@:@");
        AddMethod(delegateClass, Sel("webViewWebContentProcessDidTerminate:"), (nint)(delegate* unmanaged<nint, nint, nint, void>)&Terminated, "v@:@");
        AddMethod(delegateClass, Sel("webView:didFinishNavigation:"), (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&Loaded, "v@:@@");
        RegisterClass(delegateClass);
    }

    [UnmanagedCallersOnly]
    private static void Decide(nint self, nint cmd, nint view, nint action, nint decision)
    {
        var allow = false;
        if (delegates.TryGetValue(self, out var host))
        {
            try
            {
                var url = String(Send(Send(Send(action, Sel("request")), Sel("URL")), Sel("absoluteString")));
                var target = Send(action, Sel("targetFrame"));
                var main = target == 0 || GetBool(target, Sel("isMainFrame"));
                allow = !host.disposed && host.events.Navigate(url, main);
                if (allow && main) host.events.SourceChanged(url);
            }
            catch (Exception error) { host.events.Failed(error); }
        }
        // Objective-C blocks keep their invoke pointer after isa/flags/reserved.
        var invoke = *(delegate* unmanaged<nint, nint, void>*)(decision + IntPtr.Size + 8);
        invoke(decision, allow ? 1 : 0); // WKNavigationActionPolicyAllow / Cancel
    }

    [UnmanagedCallersOnly]
    private static void Message(nint self, nint cmd, nint controller, nint message)
    {
        if (!delegates.TryGetValue(self, out var host) || host.disposed) return;
        try
        {
            var info = Send(message, Sel("frameInfo"));
            var body = Send(message, Sel("body"));
            if (!GetBool(info, Sel("isMainFrame")) || !GetBoolArg(body, Sel("isKindOfClass:"), Class("NSString"))) return;
            host.events.Message(String(body), Url(Send(message, Sel("webView"))));
        }
        catch (Exception error) { host.events.Failed(error); }
    }
    [UnmanagedCallersOnly]
    private static nint Popup(nint self, nint cmd, nint view, nint configuration, nint action, nint features)
    {
        if (!delegates.TryGetValue(self, out var host) || host.disposed) return 0;
        try { return host.AddPage(configuration); }
        catch (Exception error) { host.events.Failed(error); return 0; }
    }
    [UnmanagedCallersOnly]
    private static void Closed(nint self, nint cmd, nint view)
    {
        if (delegates.TryGetValue(self, out var host))
            try { host.ClosePage(view); } catch (Exception error) { host.events.Failed(error); }
    }
    [UnmanagedCallersOnly]
    private static void Loaded(nint self, nint cmd, nint view, nint navigation)
    {
        if (delegates.TryGetValue(self, out var host) && !host.disposed) host.events.Ready();
    }
    [UnmanagedCallersOnly]
    private static void Terminated(nint self, nint cmd, nint view)
    {
        if (delegates.TryGetValue(self, out var host) && !host.disposed)
            host.events.Failed(new InvalidOperationException("Браузер входа завершился. Повтори вход."));
    }

    private static nint New(nint type) => Send(Send(type, Sel("alloc")), Sel("init"));
    private static nint Text(string value) => SendText(Class("NSString"), Sel("stringWithUTF8String:"), value);
    private static string String(nint value) => value == 0 ? "" : Marshal.PtrToStringUTF8(Send(value, Sel("UTF8String"))) ?? "";
    private static void Release(nint value) { if (value != 0) Send(value, Sel("release")); }
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativeRect(double X, double Y, double Width, double Height);
    private static NativeRect ReadRect(nint receiver, nint selector)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return SendRectResult(receiver, selector);
        SendRectStret(out var rect, receiver, selector); return rect;
    }
    [DllImport(ObjC, EntryPoint = "objc_getClass")] private static extern nint Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern nint Sel([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "objc_allocateClassPair")] private static extern nint AllocateClass(nint parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint extra);
    [DllImport(ObjC, EntryPoint = "objc_registerClassPair")] private static extern void RegisterClass(nint type);
    [DllImport(ObjC, EntryPoint = "class_addMethod")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AddMethod(nint type, nint selector, nint implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string encoding);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint obj, nint sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendArg(nint obj, nint sel, nint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendPng(nint obj, nint sel, nint format, nint properties);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_copy")] private static extern nint BlockCopy(nint block);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_release")] private static extern void BlockRelease(nint block);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(nint obj, nint sel, nint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendTwo(nint obj, nint sel, nint first, nint second);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendText(nint obj, nint sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendScript(nint obj, nint sel, nint text, nint time, [MarshalAs(UnmanagedType.I1)] bool main);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendInit(nint obj, nint sel, NativeRect frame, nint configuration);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendRect(nint obj, nint sel, NativeRect frame);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern NativeRect SendRectResult(nint obj, nint sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend_stret")] private static extern void SendRectStret(out NativeRect frame, nint obj, nint sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(nint obj, nint sel, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool GetBool(nint obj, nint sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool GetBoolArg(nint obj, nint sel, nint value);
}
