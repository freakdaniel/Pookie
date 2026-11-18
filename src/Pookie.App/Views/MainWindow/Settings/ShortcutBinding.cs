using Aprillz.MewUI;

namespace Pookie.App;

internal enum ShortcutAction
{
    Play, Previous, Next, VolumeUp, VolumeDown, Mute, Queue, SeekBack, SeekForward,
    Expand, Lyrics, Like, Shuffle, Repeat, Search, Find, Settings, Back, Forward, RepaintOverlay
}

internal sealed record ShortcutBinding(ShortcutAction Action, string Title, string Group, string Default = "", bool Alternate = false)
{
    public string Id => Alternate ? Action + ".alternate" : Action.ToString();

    public bool Supports(bool expanded) => Group switch
    {
        Expanded => expanded,
        Navigation or Search => !expanded,
        _ => true
    };

    private const string Player = "Плеер", Expanded = "Развёрнутый плеер", Navigation = "Навигация", Search = "Поиск";
    private static readonly string Primary = OperatingSystem.IsMacOS() ? "Meta+" : "Ctrl+";
    internal static readonly KeyGesture RepaintGesture = new(Key.D, ModifierKeys.Shift |
        (OperatingSystem.IsMacOS() ? ModifierKeys.Meta : ModifierKeys.Control));

    public static readonly ShortcutBinding[] All =
    [
        new(ShortcutAction.Back, "Назад по истории", Navigation, "Mouse4"),
        new(ShortcutAction.Back, "Назад по истории · дополнительное", Navigation, "Alt+Left", true),
        new(ShortcutAction.Forward, "Вперёд по истории", Navigation, "Mouse5"),
        new(ShortcutAction.Forward, "Вперёд по истории · дополнительное", Navigation, "Alt+Right", true),
        new(ShortcutAction.Settings, "Открыть настройки", Navigation, Primary + ","),
        new(ShortcutAction.Search, "Открыть поиск SoundCloud", Search, Primary + "K"),
        new(ShortcutAction.Find, "Поиск в чатах, лайках и коллекции", Search, Primary + "F"),
        new(ShortcutAction.Play, "Воспроизведение / пауза", Player, "Space"),
        new(ShortcutAction.Previous, "Предыдущий трек", Player, "Ctrl+Left"),
        new(ShortcutAction.Next, "Следующий трек", Player, "Ctrl+Right"),
        new(ShortcutAction.VolumeUp, "Увеличить громкость на 5%", Player, "Ctrl+Up"),
        new(ShortcutAction.VolumeDown, "Уменьшить громкость на 5%", Player, "Ctrl+Down"),
        new(ShortcutAction.Mute, "Выключить / вернуть звук", Player, "M"),
        new(ShortcutAction.Queue, "Открыть / закрыть очередь", Player, "Q"),
        new(ShortcutAction.Expand, "Развернуть / свернуть плеер", Player),
        new(ShortcutAction.Lyrics, "Показать / скрыть текст песни", Player),
        new(ShortcutAction.Like, "Поставить / убрать лайк у текущего трека", Player),
        new(ShortcutAction.Shuffle, "Переключить перемешивание", Player),
        new(ShortcutAction.Repeat, "Сменить режим повтора", Player),
        new(ShortcutAction.SeekBack, "Назад на 10 секунд", Expanded, "Left"),
        new(ShortcutAction.SeekForward, "Вперёд на 10 секунд", Expanded, "Right"),
        new(ShortcutAction.RepaintOverlay, "Подсветка областей перерисовки", "Диагностика", new KeySequence(RepaintGesture).ToString())
    ];

    public static string Display(string binding)
    {
        if (binding.Length == 0) return "Назначить";
        if (!KeyGesture.TryParse(binding, out var gesture)) return binding;
        var display = gesture.ToDisplayString();
        var label = gesture.Key switch
        {
            Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓", Key.Space => "Пробел",
            _ => null
        };
        return label == null ? display : display.Replace(gesture.Key.ToString(), label, StringComparison.Ordinal);
    }
}
