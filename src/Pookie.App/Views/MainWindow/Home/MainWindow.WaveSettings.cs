using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Home;
using Pookie.Recommendations;
using WaveMode = Pookie.App.Home.WaveMode;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> waveSettingsOpen = new(false);
    private readonly Dictionary<WaveMode, Button> waveModeButtons = [];
    private readonly Dictionary<WaveMood, Button> waveMoodButtons = [];
    private HomeWaveLayout homeWaveLayout = null!;
    private ShadowDecorator waveSettingsPanel = null!;
    private ScrollViewer waveSettingsScroll = null!;
    private Button waveSettingsClose = null!;
    private FrameworkElement WaveSettingsView()
    {
        var choices = new Grid().Columns("*,*,*").Rows("Auto").Spacing(8);
        var index = 0;
        foreach (var (mode, title, illustration) in new[] {
            (WaveMode.Balance, "Баланс", "tranding"),
            (WaveMode.Favorites, "Любимое", "heart"),
            (WaveMode.Discoveries, "Открытия", "star") })
        {
            var button = TrackButtons.Action(new StackPanel().Vertical().Spacing(8).Children(
                new Image().Source(ImageSource.FromResource<MainWindow>($"Pookie.App.Assets.Stickle.{illustration}.png"))
                    .Width(56).Height(56).StretchMode(Stretch.Uniform).CenterHorizontal(),
                new TextBlock().Text(title).FontSize(13).SemiBold().CenterHorizontal()),
                () => SetWaveMode(mode), TrackButtons.Tonal, 112).CornerRadius(16).Padding(8, 0);
            waveModeButtons[mode] = button; choices.Add(button.Column(index++));
        }
        var moods = new Grid().Columns("*,*").Rows("Auto,Auto,Auto").Spacing(6);
        index = 0;
        foreach (var (mood, label) in new[] { (WaveMood.Auto, "Авто"), (WaveMood.Calm, "Спокойно"),
            (WaveMood.Energetic, "Бодро"), (WaveMood.Melancholic, "Меланхолично"), (WaveMood.Aggressive, "Агрессивно") })
        {
            var button = TrackButtons.Action(new TextBlock().Text(label).FontSize(12).Center(),
                () => SetWaveMood(mood), TrackButtons.Tonal, 32).CornerRadius(10).Padding(8, 0);
            waveMoodButtons[mood] = button;
            moods.Add(index == 0 ? button.Row(0).ColumnSpan(2) : button.Row(1 + (index - 1) / 2).Column((index - 1) % 2));
            index++;
        }
        waveSettingsClose = TrackButtons.Icon(Icons.View("x", 18), () => waveSettingsOpen.Value = false, TrackButtons.Glass, 28);
        waveSettingsScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(
            new StackPanel().Vertical().Spacing(22).Padding(0, 8, 14, 8).Children(
                new StackPanel().Vertical().Spacing(12).Children(new TextBlock().Text("По характеру").FontSize(13).Foreground(Muted), choices),
                new StackPanel().Vertical().Spacing(8).Children(new TextBlock().Text("Настроение сейчас").FontSize(13).Foreground(Muted), moods,
                    new TextBlock().Text("Выбери музыку под настроение. Авто подстраивается по твоим реакциям в этой сессии.")
                        .FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap)),
                new StackPanel().Vertical().Spacing(12).Children(
                    new TextBlock().Text("Исключённые треки").FontSize(13).Foreground(Muted),
                    new TextBlock().Text("Не нравится песня — нажми иконку рядом с сердцем в плеере. Повторное нажатие отменяет исключение.")
                        .FontSize(13).TextWrapping(TextWrapping.Wrap),
                    TrackButtons.Action(new TextBlock().Text("Сбросить исключения").FontSize(13),
                        ResetWaveBlocks, TrackButtons.Tonal).CornerRadius(12)),
                new TextBlock().Text("Волна учитывает твои лайки, прослушивания и пропуски. Изменения применяются к следующим трекам.")
                    .FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap)));
        var card = new Border().CornerRadius(20).Background(Color.FromRgb(31, 31, 33))
            .BorderBrush(Color.FromArgb(15, 255, 255, 255)).BorderThickness(1).Padding(16).Child(
                new Grid().Columns("*").Rows("Auto,*").Spacing(18).Children(
                    new Grid().Columns("*,28").Rows("Auto").Spacing(8).Row(0).Children(
                        new TextBlock().Text("Настроить волну").FontSize(19).Bold().Column(0).CenterVertical(), waveSettingsClose.Column(1)),
                    waveSettingsScroll.Row(1)));
        waveSettingsPanel = new ShadowDecorator().BlurRadius(16).OffsetY(5).ShadowColor(Color.FromArgb(100, 0, 0, 0)).CornerRadius(20).Child(card);
        RefreshWaveSettings();
        return waveSettingsPanel;
    }
    private void RefreshWaveSettings()
    {
        foreach (var (mood, button) in waveMoodButtons)
            ((MaterialTrackButton)button).Palette(mood == waveTaste.Mood ? Color.FromRgb(50, 87, 65) : Color.FromRgb(32, 45, 40),
                Color.FromRgb(194, 244, 202));
        foreach (var (mode, button) in waveModeButtons)
        {
            var (inactive, selected, foreground) = mode switch {
                WaveMode.Favorites => (Color.FromRgb(61, 36, 43), Color.FromRgb(103, 44, 58), Color.FromRgb(255, 195, 205)),
                WaveMode.Discoveries => (Color.FromRgb(34, 45, 65), Color.FromRgb(45, 73, 111), Color.FromRgb(182, 207, 255)),
                _ => (Color.FromRgb(32, 55, 47), Color.FromRgb(50, 87, 65), Color.FromRgb(194, 244, 202)) };
            ((MaterialTrackButton)button).Palette(mode == waveTaste.Mode ? selected : inactive, foreground);
        }
    }
}
