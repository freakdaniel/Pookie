using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep hover details visible across brief pointer excursions and throughout a drag.
internal sealed class HoverReveal : IDisposable
{
    private readonly FrameworkElement owner;
    private readonly Func<bool> hold;
    private readonly Action<bool> reveal;
    private readonly DispatcherTimer timer = new(TimeSpan.FromMilliseconds(280));

    public HoverReveal(FrameworkElement owner, Action<bool> reveal, Func<bool>? hold = null)
    {
        this.owner = owner; this.reveal = reveal; this.hold = hold ?? (() => owner.IsMouseCaptured);
        owner.MouseEnter += Show;
        owner.MouseLeave += HideLater;
        owner.MouseDown += OnMouseDown;
        owner.MouseUp += OnMouseUp;
        timer.Tick += Hide;
    }

    private void Show() { timer.Stop(); reveal(true); }
    private void HideLater() { timer.Stop(); timer.Start(); }
    private void OnMouseDown(MouseEventArgs args) { if (args.Button == MouseButton.Left) Show(); }
    private void OnMouseUp(MouseEventArgs args) { if (!owner.IsMouseOver) HideLater(); }
    private void Hide()
    {
        if (hold()) return;
        timer.Stop();
        if (!owner.IsMouseOver) reveal(false);
    }

    public void Dispose()
    {
        timer.Dispose();
        owner.MouseEnter -= Show; owner.MouseLeave -= HideLater;
        owner.MouseDown -= OnMouseDown; owner.MouseUp -= OnMouseUp;
    }
}
