using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class FloatingMenuHover : IDisposable
{
    private readonly FrameworkElement anchor, card;
    private readonly Action show, close;
    private readonly DispatcherTimer enter = new(TimeSpan.FromMilliseconds(220));
    private readonly DispatcherTimer leave = new(TimeSpan.FromMilliseconds(280));

    internal FloatingMenuHover(FrameworkElement anchor, FrameworkElement card, Action show, Action close)
    {
        this.anchor = anchor; this.card = card; this.show = show; this.close = close;
        anchor.MouseEnter += Enter; anchor.MouseLeave += Leave;
        card.MouseEnter += EnterCard; card.MouseLeave += Leave;
        enter.Tick += () => { enter.Stop(); show(); };
        leave.Tick += () => { leave.Stop(); if (!anchor.IsMouseOver && !card.IsMouseOver) close(); };
    }
    private void Enter() { leave.Stop(); enter.Start(); }
    private void EnterCard() { enter.Stop(); leave.Stop(); }
    private void Leave() { enter.Stop(); leave.Start(); }
    internal void Stop() { enter.Stop(); leave.Stop(); }
    public void Dispose()
    {
        enter.Dispose(); leave.Dispose();
        anchor.MouseEnter -= Enter; anchor.MouseLeave -= Leave;
        card.MouseEnter -= EnterCard; card.MouseLeave -= Leave;
    }
}
