using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Pookie.SoundCloud;

namespace Pookie.App.Auth;

// Generic-password items live in the user's login Keychain, scoped like the
// Linux Secret Service entry. Neither credentials nor passwords enter argv.
internal sealed partial class MacSessionVault : ISessionVault
{
    private readonly string service;
    private bool disposed;
    internal MacSessionVault(string scope)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Keychain доступен только на macOS.");
        service = "io.github.freakdaniel.Pookie." + scope;
    }

    public WebSession? Load()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var query = Query();
        query.Set(Keychain.Constant("kSecReturnData"), Keychain.True);
        query.Set(Keychain.Constant("kSecMatchLimit"), Keychain.Constant("kSecMatchLimitOne"));
        var status = Keychain.CopyMatching(query.Handle, out var value);
        if (status == Keychain.NotFound) return null;
        Check(status);
        try
        {
            if (value == 0 || Keychain.GetTypeId(value) != Keychain.DataTypeId())
                throw new InvalidOperationException("Keychain вернул некорректную сессию.");
            var length = Keychain.DataLength(value);
            if (length is < 1 or > WindowsSessionVault.MaxSize) throw new InvalidOperationException("Некорректный размер сессии Keychain.");
            var bytes = new byte[(int)length];
            try
            {
                Marshal.Copy(Keychain.DataBytes(value), bytes, 0, bytes.Length);
                var session = JsonSerializer.Deserialize(bytes, SoundCloudJson.Default.WebSession);
                return session?.IsValid() == true ? session : null;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (JsonException error) { throw new InvalidOperationException("Некорректная сессия Keychain.", error); }
        finally { if (value != 0) Keychain.Release(value); }
    }

    public void Save(WebSession session)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!session.IsValid()) throw new ArgumentException("Некорректная сессия SoundCloud.", nameof(session));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session, SoundCloudJson.Default.WebSession);
        try
        {
            using var query = Query();
            using var attributes = new Keychain.Dictionary();
            attributes.Data(Keychain.Constant("kSecValueData"), bytes);
            var status = Keychain.Update(query.Handle, attributes.Handle);
            if (status == Keychain.NotFound)
            {
                query.Data(Keychain.Constant("kSecValueData"), bytes);
                status = Keychain.Add(query.Handle, 0);
                // Another instance can save the same scoped entry concurrently.
                if (status == Keychain.Duplicate)
                {
                    using var retry = Query();
                    status = Keychain.Update(retry.Handle, attributes.Handle);
                }
            }
            Check(status);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Delete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var query = Query();
        var status = Keychain.Delete(query.Handle);
        if (status != Keychain.NotFound) Check(status);
    }
    public void Dispose() => disposed = true;
    private Keychain.Dictionary Query()
    {
        var query = new Keychain.Dictionary();
        query.Set(Keychain.Constant("kSecClass"), Keychain.Constant("kSecClassGenericPassword"));
        query.Text(Keychain.Constant("kSecAttrService"), service);
        query.Text(Keychain.Constant("kSecAttrAccount"), "web-session");
        return query;
    }
    private static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException($"Keychain недоступен или заблокирован (OSStatus {status}). Сессия останется только в памяти.");
    }

    private static partial class Keychain
    {
        internal const int NotFound = -25300, Duplicate = -25299;
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string Core = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        // Framework handles and immutable exported constants have process lifetime.
        private static readonly nint security = NativeLibrary.Load(Security), core = NativeLibrary.Load(Core);
        internal static nint Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(security, name));
        internal static nint True => Marshal.ReadIntPtr(NativeLibrary.GetExport(core, "kCFBooleanTrue"));
        internal sealed class Dictionary : IDisposable
        {
            internal nint Handle { get; } = CreateDictionary(0, 0, NativeLibrary.GetExport(core, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(core, "kCFTypeDictionaryValueCallBacks"));
            internal void Set(nint key, nint value) => SetValue(Handle, key, value);
            internal void Text(nint key, string text)
            {
                var value = CreateString(0, text, 0x08000100);
                try { Set(key, value); } finally { Release(value); }
            }
            internal void Data(nint key, byte[] data)
            {
                var value = CreateData(0, data, data.Length);
                try { Set(key, value); } finally { Release(value); }
            }
            public void Dispose() => Release(Handle);
        }
        [LibraryImport(Security, EntryPoint = "SecItemCopyMatching")] internal static partial int CopyMatching(nint query, out nint result);
        [LibraryImport(Security, EntryPoint = "SecItemAdd")] internal static partial int Add(nint attributes, nint result);
        [LibraryImport(Security, EntryPoint = "SecItemUpdate")] internal static partial int Update(nint query, nint attributes);
        [LibraryImport(Security, EntryPoint = "SecItemDelete")] internal static partial int Delete(nint query);
        [LibraryImport(Core, EntryPoint = "CFDictionaryCreateMutable")] private static partial nint CreateDictionary(nint allocator, nint capacity, nint keys, nint values);
        [LibraryImport(Core, EntryPoint = "CFDictionarySetValue")] private static partial void SetValue(nint dictionary, nint key, nint value);
        [LibraryImport(Core, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)] private static partial nint CreateString(nint allocator, string value, uint encoding);
        [LibraryImport(Core, EntryPoint = "CFDataCreate")] private static partial nint CreateData(nint allocator, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] bytes, nint count);
        [LibraryImport(Core, EntryPoint = "CFDataGetLength")] internal static partial nint DataLength(nint data);
        [LibraryImport(Core, EntryPoint = "CFDataGetBytePtr")] internal static partial nint DataBytes(nint data);
        [LibraryImport(Core, EntryPoint = "CFGetTypeID")] internal static partial nuint GetTypeId(nint value);
        [LibraryImport(Core, EntryPoint = "CFDataGetTypeID")] internal static partial nuint DataTypeId();
        [LibraryImport(Core, EntryPoint = "CFRelease")] internal static partial void Release(nint value);
    }
}
