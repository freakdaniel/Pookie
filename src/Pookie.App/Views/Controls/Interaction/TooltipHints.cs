using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// App-owned hints use the window overlay rather than native popup windows.
// One registration per owner; rebinding a virtualized row only updates its text.
internal static class TooltipHints
{
    private sealed class HintData { internal string? Text; }
    private static readonly ConditionalWeakTable<FrameworkElement, HintData> hints = new();
    internal static string? TextFor(FrameworkElement owner) => hints.TryGetValue(owner, out var hint) ? hint.Text : null;
    private static MaterialTooltipLayer? Layer(FrameworkElement owner) =>
        (owner.FindVisualRoot() as Window)?.OverlayLayer.GetService<MaterialTooltipLayer>();

    internal static T Hint<T>(this T owner, string? text) where T : FrameworkElement
    {
        var hint = hints.GetValue(owner, element =>
        {
            element.MouseEnter += () => Layer(element)?.Request(element, keyboard: false);
            element.MouseLeave += () => Layer(element)?.Leave(element);
            element.GotFocus += () => Layer(element)?.Request(element, keyboard: true);
            element.LostFocus += () => Layer(element)?.Leave(element, lostFocus: true);
            element.MouseDown += _ => Layer(element)?.Dismiss(element);
            element.MouseUp += _ => Layer(element)?.Dismiss(element);
            return new HintData();
        });
        text = string.IsNullOrWhiteSpace(text) ? null : text;
        if (hint.Text == text) return owner;
        hint.Text = text; Layer(owner)?.Refresh(owner);
        return owner;
    }
}
