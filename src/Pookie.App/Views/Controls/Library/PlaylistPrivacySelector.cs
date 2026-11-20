using Pookie.Localization;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed class PlaylistPrivacySelector(Action<bool> select)
    : SegmentedSelector("globe", UiText.Get("privacy.public"), "lock-simple", UiText.Get("privacy.private"), select, liquidGlass: true)
{
    internal Button Public => First;
    internal Button Private => Second;
}
