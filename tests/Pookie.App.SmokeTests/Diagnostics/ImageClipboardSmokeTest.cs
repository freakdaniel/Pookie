using System.Diagnostics;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Pookie.App.Platform;
using StbImageSharp;

namespace Pookie.App.Diagnostics;

internal static partial class ImageClipboardSmokeTest
{
    internal static void Run(string? path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This clipboard transport check uses GTK.");
        var source = path == null ? null : ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        var width = source?.Width ?? 3; var height = source?.Height ?? 1;
        var rgba = source?.Data ?? [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255];
        var dib = new byte[40 + rgba.Length];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), -height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        for (var i = 0; i < rgba.Length; i += 4)
        { dib[40 + i] = rgba[i + 2]; dib[41 + i] = rgba[i + 1]; dib[42 + i] = rgba[i]; dib[43 + i] = rgba[i + 3]; }
        var png = ClipboardBitmap.PngFromDib(dib);
        var decoded = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
        if (!decoded.Data.SequenceEqual(rgba)) throw new InvalidOperationException("Clipboard PNG changed the original colours.");
        var selection = "POOKIE_IMAGE_TEST_" + Guid.NewGuid().ToString("N");
        var clipboard = GtkClipboardService.TryCreate(selection) ?? throw new InvalidOperationException("GTK is unavailable.");
        var expected = Path.Combine(Path.GetTempPath(), "pookie-clipboard-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(expected, png);
            if (!ImageClipboard.TrySetLinuxPng(png, selection)) throw new InvalidOperationException("Cannot offer PNG.");
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(ImageClipboardSmokeTest).Assembly.Location);
            foreach (var argument in new[] { "--image-clipboard-read-fixture", selection, expected }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(); var error = child.StandardError.ReadToEndAsync();
            var timer = Stopwatch.StartNew();
            while (!child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(10)) { ImageClipboard.Pump(); Thread.Sleep(2); }
            if (!child.HasExited) { child.Kill(); throw new TimeoutException("Clipboard consumer did not finish."); }
            if (child.ExitCode != 0 || !output.GetAwaiter().GetResult().Contains("PNG_READ_OK"))
                throw new InvalidOperationException("External image consumer failed: " + error.GetAwaiter().GetResult());
            // Replacing the private selection also exercises release of GTK's data callback.
            clipboard.TrySetText("Private selection cleared");
            Console.WriteLine($"IMAGE_CLIPBOARD_OK: {width}x{height} original colours, exact PNG bytes in another process, no lossy MIME alternative; user clipboard untouched");
        }
        finally { clipboard.TrySetText(""); File.Delete(expected); }
    }

    internal static async Task ReadAsync(string selection, string expected)
    {
        _ = GtkClipboardService.TryCreate(selection) ?? throw new InvalidOperationException("GTK is unavailable.");
        var clipboard = gtk_clipboard_get(gdk_atom_intern(selection, 0));
        if (gtk_clipboard_wait_is_target_available(clipboard, gdk_atom_intern("image/jpeg", 0)) != 0)
            throw new InvalidOperationException("Lossy JPEG must not be advertised by image copying.");
        var result = await ChatClipboard.ReadAsync(CancellationToken.None, selection);
        var bytes = await File.ReadAllBytesAsync(expected);
        if (result?.Image == null || !result.Image.SequenceEqual(bytes))
            throw new InvalidOperationException("Image transport changed the PNG payload.");
        Console.WriteLine("PNG_READ_OK");
    }

    [LibraryImport("libgtk-3.so.0")] private static partial nint gtk_clipboard_get(nint selection);
    [LibraryImport("libgtk-3.so.0")] private static partial int gtk_clipboard_wait_is_target_available(nint clipboard, nint target);
    [LibraryImport("libgdk-3.so.0", StringMarshalling = StringMarshalling.Utf8)] private static partial nint gdk_atom_intern(string name, int onlyIfExists);
}
