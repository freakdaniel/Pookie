using Pookie.Localization;
using Aprillz.MewUI;

namespace Pookie.App;

internal enum ShortcutAction
{
    Play, Previous, Next, VolumeUp, VolumeDown, Mute, Queue, SeekBack, SeekForward,
    Expand, Lyrics, Like, Shuffle, Repeat, Search, Find, Settings, Back, Forward, RepaintOverlay
}

internal sealed record ShortcutBinding(ShortcutAction Action, string TitleKey, string GroupKey, string Default = "", bool Alternate = false)
{
    public string Title => UiText.Get(TitleKey);
    public string Id => Alternate ? Action + ".alternate" : Action.ToString();

    public bool Supports(bool expanded) => GroupKey switch
    {
        Expanded => expanded,
        Navigation or Search => !expanded,
        _ => true
    };

    private const string Player = "player.title", Expanded = "player.expanded.heading",
        Navigation = "shortcuts.navigation", Search = "header.search";
    private static readonly string Primary = OperatingSystem.IsMacOS() ? "Meta+" : "Ctrl+";
    internal static readonly KeyGesture RepaintGesture = new(Key.D, ModifierKeys.Shift |
        (OperatingSystem.IsMacOS() ? ModifierKeys.Meta : ModifierKeys.Control));

    public static readonly ShortcutBinding[] All =
    [
        new(ShortcutAction.Back, "shortcuts.historyBack", Navigation, "Mouse4"),
        new(ShortcutAction.Back, "shortcuts.historyBackAlternate", Navigation, "Alt+Left", true),
        new(ShortcutAction.Forward, "shortcuts.historyForward", Navigation, "Mouse5"),
        new(ShortcutAction.Forward, "shortcuts.historyForwardAlternate", Navigation, "Alt+Right", true),
        new(ShortcutAction.Settings, "settings.open", Navigation, Primary + ","),
        new(ShortcutAction.Search, "shortcuts.openSearch", Search, Primary + "K"),
        new(ShortcutAction.Find, "shortcuts.findInPage", Search, Primary + "F"),
        new(ShortcutAction.Play, "playback.playPause", Player, "Space"),
        new(ShortcutAction.Previous, "playback.previousTrack", Player, "Ctrl+Left"),
        new(ShortcutAction.Next, "playback.nextTrack", Player, "Ctrl+Right"),
        new(ShortcutAction.VolumeUp, "shortcuts.volumeUp", Player, "Ctrl+Up"),
        new(ShortcutAction.VolumeDown, "shortcuts.volumeDown", Player, "Ctrl+Down"),
        new(ShortcutAction.Mute, "shortcuts.toggleMute", Player, "M"),
        new(ShortcutAction.Queue, "shortcuts.toggleQueue", Player, "Q"),
        new(ShortcutAction.Expand, "shortcuts.toggleExpandedPlayer", Player),
        new(ShortcutAction.Lyrics, "shortcuts.toggleLyrics", Player),
        new(ShortcutAction.Like, "shortcuts.likeCurrentTrack", Player),
        new(ShortcutAction.Shuffle, "shortcuts.toggleShuffle", Player),
        new(ShortcutAction.Repeat, "shortcuts.cycleRepeat", Player),
        new(ShortcutAction.SeekBack, "shortcuts.seekBack", Expanded, "Left"),
        new(ShortcutAction.SeekForward, "shortcuts.seekForward", Expanded, "Right"),
        new(ShortcutAction.RepaintOverlay, "settings.diagnostics.repaint", "settings.diagnostics.title", new KeySequence(RepaintGesture).ToString())
    ];

    public static string Display(string binding)
    {
        if (binding.Length == 0) return UiText.Get("shortcuts.assign");
        if (!KeyGesture.TryParse(binding, out var gesture)) return binding;
        var display = gesture.ToDisplayString();
        var label = gesture.Key switch
        {
            Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓", Key.Space => UiText.Get("key.space"),
            _ => null
        };
        return label == null ? display : display.Replace(gesture.Key.ToString(), label, StringComparison.Ordinal);
    }
}
