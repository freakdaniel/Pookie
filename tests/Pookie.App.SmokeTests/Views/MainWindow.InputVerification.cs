using System.Diagnostics;
using System.Runtime.InteropServices;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Pookie.App.Diagnostics;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifySearchInputAsync()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (searchInput == null) throw new InvalidOperationException("Native search input is unavailable");
        var selection = "POOKIE_TEST_" + Guid.NewGuid().ToString("N");
        var clipboard = GtkClipboardService.TryCreate(selection)!;
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(start.FileName) == "dotnet") start.ArgumentList.Add(typeof(MainWindow).Assembly.Location);
        start.ArgumentList.Add("--clipboard-fixture"); start.ArgumentList.Add(selection);
        using var owner = Process.Start(start)!;
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for search verification");
        try
        {
            const string russianTyping = "Привет World Ёж";
            var russianAvailable = (russianTyping + "я").All(character => TryFindInputKey(display, character, out _, out _));
            if (await owner.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) != "CLIPBOARD_READY")
                throw new InvalidOperationException("External clipboard fixture failed to start");
            if (!clipboard.TryGetText(out var received) || received != ClipboardFixture.Text)
                throw new InvalidOperationException("Cross-process Unicode clipboard transport failed");
            topSearchInput.ClipboardService = clipboard;
            likedFilter.ClipboardService = clipboard;
            foreach (var field in new[] { topSearchInput, likedFilter })
            {
                if (field == topSearchInput) OpenTopSearch();
                else { CloseTopSearch(true); ShowLibraryTracks(); }
                await Task.Delay(300, lifetime.Token);
                field.Focus();
                SetInputFixtureText(field, "prefix to replace");
                field.SelectAll();
                SendCharacter(display, 'v', 4);
                await WaitForInputAsync(field, ClipboardFixture.Text);
                if (field == topSearchInput && query.Value != ClipboardFixture.Text ||
                    field == likedFilter && likedFilterText != ClipboardFixture.Text)
                    throw new InvalidOperationException($"Pasted text bypassed search binding/filter: top={field == topSearchInput}, query='{query.Value}', filter='{likedFilterText}'");
                SendCharacter(display, 'z', 4);
                await WaitForInputAsync(field, "prefix to replace");

                // Send a batch, rather than waiting between keys. This detects
                // lost/duplicated characters and ordering against ASCII keys.
                SetInputFixtureText(field, "");
                var typed = russianAvailable ? russianTyping : "Hello World";
                foreach (var character in typed) SendCharacter(display, character);
                await WaitForInputAsync(field, typed);
                SendCharacter(display, 'a', 4);
                await Task.Delay(50, lifetime.Token);
                SendCharacter(display, russianAvailable ? 'я' : 'a');
                await WaitForInputAsync(field, russianAvailable ? "я" : "a");

                // Normal committed IME text must remain Unicode, and never be
                // doubled by the fallback after a commit already arrived.
                SetInputFixtureText(field, "");
                ((ITextInputClient)field).HandleTextInput(new("日本語 العربية 🎵"));
                if (field.Text != "日本語 العربية 🎵") throw new InvalidOperationException("Unicode commit was corrupted");
                SetInputFixtureText(field, "");
            }
            Console.WriteLine($"UI_SEARCH_INPUT_OK: external UTF-8 clipboard, Ctrl+V/Ctrl+Z in both fields, batched {(russianAvailable ? "Russian/English" : "English (Russian keyboard layout unavailable)")} typing with Shift, selection replacement and Unicode commits");
        }
        finally
        {
            topSearchInput.ClipboardService = nativeClipboard;
            likedFilter.ClipboardService = nativeClipboard;
            CloseTopSearch(true);
            Window.FocusManager.ClearFocus();
            await NavigateAsync(Page.Home);
            XCloseDisplay(display);
            await owner.StandardInput.WriteLineAsync("stop");
            try { await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { owner.Kill(); }
        }
    }

    private void SetInputFixtureText(TextBox field, string text)
    {
        // Assigning Text directly detaches a MewUI binding. Seed the bound
        // field through its observable, just as opening/clearing search does.
        if (field == topSearchInput) query.Value = text;
        else field.Text = text;
    }

    private async Task WaitForInputAsync(TextBox field, string expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (field.Text == expected) return;
            await Task.Delay(25, lifetime.Token);
        }
        throw new InvalidOperationException($"Search input mismatch: expected '{expected}', got '{field.Text}'");
    }

    private void SendCharacter(nint display, char character, uint extraModifiers = 0)
    {
        if (!TryFindInputKey(display, character, out var code, out var state))
            throw new InvalidOperationException($"Test keyboard layout has no '{character}' key");
        var key = new LinuxSearchInput.XKeyEvent
        {
            Type = 2, Display = display, Window = Window.Handle, Root = XDefaultRootWindow(display),
            State = state | extraModifiers, KeyCode = code, SameScreen = 1
        };
        var buffer = Marshal.AllocHGlobal(24 * IntPtr.Size); // XEvent union, not only XKeyEvent.
        try
        {
            Marshal.StructureToPtr(key, buffer, false);
            if (XSendEvent(display, Window.Handle, 0, 1, buffer) == 0) throw new InvalidOperationException("XSendEvent failed");
            key.Type = 3;
            Marshal.StructureToPtr(key, buffer, false);
            XSendEvent(display, Window.Handle, 0, 2, buffer);
            XFlush(display);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool TryFindInputKey(nint display, char character, out uint keyCode, out uint keyState)
    {
        XDisplayKeycodes(display, out var first, out var last);
        for (var group = 0; group < 4; group++)
        for (uint shift = 0; shift <= 1; shift++)
        for (var code = first; code <= last; code++)
        {
            var state = ((uint)group << 13) | shift;
            if (XkbLookupKeySym(display, (byte)code, state, out _, out var symbol) == 0 ||
                gdk_keyval_to_unicode((uint)symbol) != character) continue;
            keyCode = (uint)code; keyState = state;
            return true;
        }
        keyCode = keyState = 0;
        return false;
    }

    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern void XDisplayKeycodes(nint display, out int first, out int last);
    [DllImport("libX11.so.6")] private static extern int XkbLookupKeySym(nint display, byte code, uint state, out uint consumed, out nuint symbol);
    [DllImport("libX11.so.6")] private static extern nint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern int XSendEvent(nint display, nint window, int propagate, nint eventMask, nint eventData);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libgdk-3.so.0")] private static extern uint gdk_keyval_to_unicode(uint symbol);
}
