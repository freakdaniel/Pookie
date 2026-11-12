using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class PlaylistPrivacySelector(Action<bool> select)
    : SegmentedSelector("globe", "Публичный", "lock-simple", "Приватный", select, liquidGlass: true)
{
    internal Button Public => First;
    internal Button Private => Second;
}
