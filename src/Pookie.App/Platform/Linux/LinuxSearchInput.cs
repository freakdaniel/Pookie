using System.Runtime.InteropServices;
using System.Text;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace Pookie.App.Platform;

// Preserve normal XIM/IBus composition. Only supply a character if the backend
// forwarded the key to the app but its legacy XLookupString lost Unicode text.
internal sealed class LinuxSearchInput : IDisposable
{
    private readonly Window window;
    private readonly TextBox[] fields;
    private PendingKey? pending;
    private bool disposed;

    public LinuxSearchInput(Window window, params TextBox[] fields)
    {
        this.window = window;
        this.fields = fields;
        window.NativeMessage += OnNativeMessage;
        window.PreviewKeyDown += OnKeyDown;
        foreach (var field in fields) field.TextInput += OnTextInput;
    }

    private void OnNativeMessage(NativeMessageEventArgs e)
    {
        // Finish the previous event before processing another key, including
        // ASCII, backspace or mouse input; dispatcher-only delivery reorders it.
        CompletePending();
        if (e is not X11NativeMessageEventArgs { EventType: 2 } x11 ||
            window.FocusManager.FocusedElement is not TextBox field || !fields.Contains(field) ||
            field.IsReadOnly || ((ITextCompositionClient)field).IsComposing) return;
        var key = Marshal.PtrToStructure<XKeyEvent>(x11.EventPointer);
        if ((key.State & (4 | 8 | 64)) != 0) return; // Ctrl, Alt and Super shortcuts; allow AltGr.
        if (XkbLookupKeySym(key.Display, (byte)key.KeyCode, key.State, out _, out var symbol) == 0) return;
        var character = gdk_keyval_to_unicode((uint)symbol);
        if (character < 128 || !Rune.IsValid((int)character)) return;
        pending = new(field, new Rune((int)character).ToString());
        var captured = pending;
        Application.Current!.Dispatcher!.BeginInvoke(() =>
        {
            if (!disposed && ReferenceEquals(pending, captured)) CompletePending();
        });
    }

    private void OnKeyDown(KeyEventArgs e)
    {
        if (pending != null) pending.ForwardedKey = e;
    }

    private void OnTextInput(TextInputEventArgs e)
    {
        if (pending == null) return;
        // XLookupString emits Latin-1, which the backend decodes as UTF-8.
        // Discard that invalid byte rather than inserting a replacement glyph.
        if (e.Text == "\uFFFD") { e.Handled = true; return; }
        pending.Delivered = true;
    }

    private void CompletePending()
    {
        var key = pending;
        pending = null;
        if (key == null || key.Delivered || key.ForwardedKey is not { Handled: false } ||
            !ReferenceEquals(window.FocusManager.FocusedElement, key.Field) || key.Field.IsReadOnly ||
            ((ITextCompositionClient)key.Field).IsComposing) return;
        ((ITextInputClient)key.Field).HandleTextInput(new(key.Text));
    }

    public void Dispose()
    {
        disposed = true;
        pending = null;
        window.NativeMessage -= OnNativeMessage;
        window.PreviewKeyDown -= OnKeyDown;
        foreach (var field in fields) field.TextInput -= OnTextInput;
    }

    private sealed class PendingKey(TextBox field, string text)
    {
        public TextBox Field { get; } = field;
        public string Text { get; } = text;
        public KeyEventArgs? ForwardedKey;
        public bool Delivered;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XKeyEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public nint Display, Window, Root, Subwindow;
        public nuint Time;
        public int X, Y, RootX, RootY;
        public uint State, KeyCode;
        public int SameScreen;
    }

    [DllImport("libX11.so.6")] private static extern int XkbLookupKeySym(nint display, byte keyCode, uint state, out uint consumedModifiers, out nuint symbol);
    [DllImport("libgdk-3.so.0")] private static extern uint gdk_keyval_to_unicode(uint symbol);
}
