using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Pookie.App.Storage;
using Pookie.SoundCloud;

namespace Pookie.App.Auth;

// DPAPI binds this encrypted file to the signed-in Windows user, not the machine.
internal sealed class WindowsSessionVault(AppDataPaths paths) : ISessionVault
{
    internal const int MaxSize = 65536;
    private readonly string file = Path.Combine(paths.Config, "soundcloud-session.bin");
    private bool disposed;

    public WebSession? Load()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            if (!File.Exists(file)) return null;
            using var input = File.OpenRead(file);
            if (input.Length is < 1 or > MaxSize) throw new InvalidOperationException("Некорректный размер сохранённой сессии.");
            var encrypted = new byte[(int)input.Length];
            input.ReadExactly(encrypted);
            var plaintext = Transform(encrypted, encrypt: false);
            try
            {
                var session = JsonSerializer.Deserialize(plaintext, SoundCloudJson.Default.WebSession);
                return session?.IsValid() == true ? session : null;
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Не удалось прочитать сохранённую сессию SoundCloud.", error); }
    }

    public void Save(WebSession session)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!session.IsValid()) throw new ArgumentException("Некорректная сессия SoundCloud.", nameof(session));
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(session, SoundCloudJson.Default.WebSession);
        byte[] encrypted;
        try { encrypted = Transform(plaintext, encrypt: true); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            try
            {
                File.WriteAllBytes(temporary, encrypted);
                File.Move(temporary, file, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Не удалось сохранить сессию SoundCloud.", error); }
    }

    public void Delete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try { File.Delete(file); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Не удалось удалить сохранённую сессию SoundCloud.", error); }
    }

    public void Dispose() => disposed = true;

    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI доступен только на Windows.");
        var input = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        DataBlob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var succeeded = encrypt
                ? CryptProtectData(ref input, 0, 0, 0, 0, 1, out output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!succeeded) throw new InvalidOperationException("Хранилище сессии Windows недоступно или данные повреждены.",
                new Win32Exception(Marshal.GetLastWin32Error()));
            if (output.Size is < 1 or > MaxSize) throw new InvalidOperationException("Некорректный размер сессии Windows.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            // Clear both unmanaged buffers before releasing any plaintext session data.
            for (var index = 0; index < input.Size; index++) Marshal.WriteByte(input.Data, index, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0)
            {
                for (var index = 0; index < output.Size; index++) Marshal.WriteByte(output.Data, index, 0);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptProtectData(
        ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(
        ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
