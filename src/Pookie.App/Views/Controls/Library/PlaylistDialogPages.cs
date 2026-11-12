using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class PlaylistDialogPages : DialogPageDeck
{
    internal const double HeaderHeight = 72;
    internal bool Creating => SelectedIndex == 1;
    internal double ListHeight { get => PageHeight(0) + HeaderHeight; set => SetPageHeight(0, value - HeaderHeight); }
    internal double FormHeight { get => PageHeight(1) + HeaderHeight; set => SetPageHeight(1, value - HeaderHeight); }
    internal Rect ListBounds => PageBounds(0);
    internal Rect FormBounds => PageBounds(1);
    internal PlaylistDialogPages(FrameworkElement list, FrameworkElement form) : base(list, form)
    { ListHeight = PlaylistDialogContent.PreferredBodyHeight; }
    internal void Show(bool create, bool animate = true) => base.Show(create ? 1 : 0, animate);
}
