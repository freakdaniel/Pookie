using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

// Shared by message bodies and descriptions. Selection belongs to the displayed
// text layout; there is no editor, input client, insertion caret or blink timer.
internal abstract class SelectableTextBlock : TextBlock
{
    private int anchor, end;
    private bool selecting;
    internal int SelectionLength => Math.Abs(end - anchor);
    internal string? SelectedText => SelectionLength == 0 ? null : Text[Math.Min(anchor, end)..Math.Max(anchor, end)];
    internal IClipboardService? ClipboardService { get; set; }
    internal Action<string>? CopyText;
    protected abstract ITextLayout SelectionLayout(double width);
    protected virtual bool SelectionPressed(MouseEventArgs args) => false;

    protected SelectableTextBlock()
    {
        Focusable = true;
        MouseDown += args =>
        {
            if (args.Handled || args.Button != MouseButton.Left) return;
            if (SelectionPressed(args)) { args.Handled = true; return; }
            Focus(); selecting = true;
            var hit = SelectionLayout(Bounds.Width).HitTestPoint(args.GetPosition(this));
            var offset = Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, Text.Length);
            if (!args.Modifiers.HasFlag(ModifierKeys.Shift)) anchor = offset;
            end = offset;
            if (FindVisualRoot() is Window window) window.CaptureMouse(this);
            args.Handled = true; InvalidateVisual();
        };
        MouseMove += args =>
        {
            if (!selecting || !args.LeftButton) return;
            var hit = SelectionLayout(Bounds.Width).HitTestPoint(args.GetPosition(this));
            end = Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, Text.Length);
            args.Handled = true; InvalidateVisual();
        };
        MouseUp += args =>
        {
            if (!selecting || args.Button != MouseButton.Left) return;
            StopSelection(); args.Handled = true;
        };
        KeyDown += args =>
        {
            if (args.Modifiers.HasFlag(ModifierKeys.Control) && args.Key == Key.A)
            { SelectAll(); args.Handled = true; }
            else if (args.Modifiers.HasFlag(ModifierKeys.Control) && args.Key == Key.C && SelectionLength > 0)
            { Copy(); args.Handled = true; }
            else if (args.Key is Key.Left or Key.Right or Key.Home or Key.End)
            {
                if (args.Key is Key.Home or Key.End) end = args.Key == Key.Home ? 0 : Text.Length;
                else
                {
                    var hit = SelectionLayout(Bounds.Width).GetNextLogicalCaret(new(end, 0),
                        args.Key == Key.Left ? LogicalDirection.Backward : LogicalDirection.Forward, CaretMode.TextElement);
                    end = Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, Text.Length);
                }
                if (!args.Modifiers.HasFlag(ModifierKeys.Shift)) anchor = end;
                args.Handled = true; InvalidateVisual();
            }
        };
        Commands.Register(StandardCommands.SelectAll, SelectAll);
        Commands.Register(StandardCommands.Copy, Copy, () => SelectionLength > 0);
    }

    internal void SelectAll() { anchor = 0; end = Text.Length; InvalidateVisual(); }
    internal void Copy()
    {
        if (SelectedText is not { } text) return;
        if (CopyText != null) CopyText(text);
        else (ClipboardService ?? Application.Current.PlatformServices.Clipboard)?.TrySetText(text);
    }
    protected void ClearSelection() { StopSelection(); anchor = end = 0; InvalidateVisual(); }
    private void StopSelection()
    {
        selecting = false;
        if (IsMouseCaptured && FindVisualRoot() is Window window) window.ReleaseMouseCapture();
    }
    protected void DrawSelection(IGraphicsContext context)
    {
        if (SelectionLength == 0) return;
        var rectangles = new List<Rect>();
        SelectionLayout(Bounds.Width).GetRangeBounds(Math.Min(anchor, end), SelectionLength, rectangles);
        context.Save();
        try
        {
            context.SetClip(Bounds);
            foreach (var rect in rectangles)
                context.FillRectangle(new(Bounds.X + rect.X, Bounds.Y + rect.Y, rect.Width, rect.Height), Color.FromArgb(100, 100, 175, 230));
        }
        finally { context.Restore(); }
    }
    protected override void OnTextChanged() { ClearSelection(); base.OnTextChanged(); }
    protected override void OnDispose() { StopSelection(); base.OnDispose(); }
}
