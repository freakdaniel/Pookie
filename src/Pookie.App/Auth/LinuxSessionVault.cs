using System.Runtime.InteropServices;
using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App.Auth;

// Store in the desktop's Secret Service (GNOME Keyring/KWallet), never in the legacy config.
internal sealed class LinuxSessionVault : ISessionVault
{
    private readonly nint library;
    private readonly nint schema;
    private readonly Lookup lookup;
    private readonly Store store;
    private readonly Clear clear;
    private readonly Release freePassword;
    private readonly Release freeError;
    private readonly Release unrefSchema;
    private bool disposed;

    public LinuxSessionVault()
    {
        if (!OperatingSystem.IsLinux() || !NativeLibrary.TryLoad("libsecret-1.so.0", out library)) throw new InvalidOperationException("Сохранение сессии требует Linux Secret Service (GNOME Keyring или KWallet).");
        lookup = Export<Lookup>("secret_password_lookup_sync");
        store = Export<Store>("secret_password_store_sync");
        clear = Export<Clear>("secret_password_clear_sync");
        freePassword = Export<Release>("secret_password_free");
        freeError = Export<Release>("g_error_free");
        unrefSchema = Export<Release>("secret_schema_unref");
        schema = Export<NewSchema>("secret_schema_new")("io.pookie.web-session", 0, "application", 0, 0);
        if (schema == 0) throw new InvalidOperationException("Не удалось создать схему хранилища сессий.");
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private void Check(nint error)
    {
        if (error == 0) return;
        freeError(error);
        throw new InvalidOperationException("Хранилище паролей недоступно или заблокировано. Сессия останется только в памяти.");
    }
    public WebSession? Load()
    {
        var value = lookup(schema, 0, out var error, "application", "pookie", 0);
        Check(error);
        if (value == 0) return null;
        try
        {
            var session = JsonSerializer.Deserialize(Marshal.PtrToStringUTF8(value)!, SoundCloudJson.Default.WebSession);
            return session != null && session.IsValid() ? session : null;
        }
        finally { freePassword(value); }
    }
    public void Save(WebSession session)
    {
        var success = store(schema, "default", "Pookie SoundCloud web session", JsonSerializer.Serialize(session, SoundCloudJson.Default.WebSession), 0, out var error, "application", "pookie", 0);
        Check(error);
        if (success == 0) throw new InvalidOperationException("Сессию не удалось сохранить в хранилище паролей.");
    }
    public void Delete() { clear(schema, 0, out var error, "application", "pookie", 0); Check(error); }
    public void Dispose() { if (disposed) return; disposed = true; unrefSchema(schema); NativeLibrary.Free(library); }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint NewSchema([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, [MarshalAs(UnmanagedType.LPUTF8Str)] string attribute, int type, nint end);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Lookup(nint schema, nint cancellable, out nint error, [MarshalAs(UnmanagedType.LPUTF8Str)] string attribute, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, nint end);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Store(nint schema, [MarshalAs(UnmanagedType.LPUTF8Str)] string collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label, [MarshalAs(UnmanagedType.LPUTF8Str)] string password, nint cancellable, out nint error, [MarshalAs(UnmanagedType.LPUTF8Str)] string attribute, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, nint end);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Clear(nint schema, nint cancellable, out nint error, [MarshalAs(UnmanagedType.LPUTF8Str)] string attribute, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, nint end);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Release(nint pointer);
}
