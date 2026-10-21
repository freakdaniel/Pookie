using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum SettingsSection { General, Interface, Playback, Wave, Chats, Data, About }
    private readonly ObservableValue<SettingsSection> settingsSection = new(SettingsSection.General);
    private readonly ObservableValue<string> settingsMessage = new("");
    private readonly List<Action> settingsRefreshers = [];
    private readonly Dictionary<string, ToggleSwitch> settingsToggles = [];
    private readonly Dictionary<string, MaterialSelect> settingsChoices = [];
    private readonly Dictionary<SettingsSection, Button> settingsTabs = [];
    private readonly List<(Button Button, Func<bool> Enabled)> settingsActions = [];
    private ScrollViewer settingsScroll = null!;
    private StackPanel settingsBlockedList = null!;
    private CacheUsageView settingsCacheUsage = null!;
    private bool refreshingSettings, settingsBusy;
    private long settingsDataGeneration;

    private FrameworkElement SettingsPageView()
    {
        var tabs = new StackPanel().Horizontal().Spacing(22);
        var body = new Grid().Columns("*").Rows("Auto");
        foreach (var (section, title, content) in new (SettingsSection, string, Func<FrameworkElement>)[] {
            (SettingsSection.General, "Общие", GeneralSettingsView), (SettingsSection.Interface, "Интерфейс", InterfaceSettingsView),
            (SettingsSection.Playback, "Воспроизведение", PlaybackSettingsView), (SettingsSection.Wave, "Волна", WavePreferencesView),
            (SettingsSection.Chats, "Чаты", ChatSettingsView), (SettingsSection.Data, "Данные", DataSettingsView),
            (SettingsSection.About, "О приложении", AboutSettingsView) })
        {
            var tab = SectionTab(title, settingsSection, section, () => Run(() => ShowSettingsAsync(section)));
            settingsTabs[section] = tab; tabs.Add(tab);
            body.Add(content().BindIsVisible(settingsSection, value => value == section));
        }
        settingsScroll = new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0)
            .Content(body.Padding(0, 0, 16, 24));
        return new DockPanel().LastChildFill().Spacing(24).Children(
            new StackPanel().Vertical().Spacing(22).DockTop().Children(
                new TextBlock().Text("Настройки").FontSize(30).Bold(), tabs,
                new TextBlock().BindText(settingsMessage).FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap)
                    .BindIsVisible(settingsMessage, value => value.Length > 0)), settingsScroll);
    }

    private Task ShowSettingsAsync(SettingsSection section) => NavigateRouteAsync(new(Page.Settings, SettingsSection: section), _ =>
    {
        CloseShellMenus(true); queueOpen.Value = false;
        settingsSection.Value = section;
        settingsScroll.SetScrollOffsets(0, 0);
        RefreshSettingsControls(); RefreshSettingsData();
        return Task.CompletedTask;
    });

    private void RefreshSettingsControls()
    {
        if (refreshingSettings) return;
        refreshingSettings = true;
        try
        {
            foreach (var refresh in settingsRefreshers) refresh();
            foreach (var (button, enabled) in settingsActions) button.IsEnabled = !settingsBusy && enabled();
        }
        finally { refreshingSettings = false; }
    }

    private static StackPanel SettingsGroups(params FrameworkElement[] groups) => new StackPanel().Vertical().Spacing(30).Children(groups);

    private static FrameworkElement SettingsGroup(string title, params FrameworkElement[] rows)
    {
        // Notes or dynamic content may follow the final setting row.
        if (rows.OfType<Border>().LastOrDefault() is { } last)
        { last.NonUniformBorderThickness = new Thickness(0); last.BorderThickness = 0; }
        return new StackPanel().Vertical().Spacing(title.Length > 0 ? 4 : 0).Children(
            new TextBlock().Text(title).FontSize(21).SemiBold().Margin(0, 0, 0, 6).IsVisible(title.Length > 0),
            new StackPanel().Vertical().Children(rows));
    }

    private static FrameworkElement SettingsRow(string title, string description, FrameworkElement control)
    {
        var text = new StackPanel().Vertical().Spacing(5).CenterVertical().Children(
            new TextBlock().Text(title).FontSize(15).TextWrapping(TextWrapping.Wrap));
        if (description.Length > 0) text.Add(new TextBlock().Text(description).FontSize(13).Foreground(Muted).TextWrapping(TextWrapping.Wrap));
        return new Border().BorderBrush(Edge).BorderThickness(new Thickness(0, 0, 0, 1)).Padding(0, 17)
            .Child(new Grid().Columns("*,Auto").Rows("Auto").Spacing(24).Children(text.Column(0), control.CenterVertical().Column(1)));
    }

    private FrameworkElement SettingsToggle(string key, string title, string description, Func<bool> read, Action<bool> write)
    {
        var control = MaterialSwitch.Create(read(), value => { if (!refreshingSettings) write(value); }).ToolTip(title);
        settingsToggles[key] = control;
        settingsRefreshers.Add(() => control.IsChecked = read());
        return SettingsRow(title, description, control);
    }

    private FrameworkElement SettingsChoice<T>(string key, string title, string description, Func<T> read, Action<T> write,
        params (string Title, T Value)[] options)
    {
        var control = new MaterialSelect(options.Select(option => option.Title).ToArray()).Width(220).ToolTip(title);
        void Refresh() => control.SelectedIndex = Array.FindIndex(options, option => EqualityComparer<T>.Default.Equals(option.Value, read()));
        Refresh();
        control.SelectionChanged += _ =>
        {
            if (!refreshingSettings && control.SelectedIndex >= 0 && control.SelectedIndex < options.Length)
                write(options[control.SelectedIndex].Value);
        };
        settingsChoices[key] = control; settingsRefreshers.Add(Refresh);
        return SettingsRow(title, description, control);
    }

    private FrameworkElement SettingsAction(string title, string description, string action, Action click, Func<bool>? enabled = null, bool externalLink = false)
    {
        FrameworkElement content = new TextBlock().Text(action).FontSize(14).SemiBold();
        if (externalLink) content = new StackPanel().Horizontal().Spacing(8).Children(
            content.CenterVertical(), Icons.View("arrow-square-out", 18, TrackButtons.OnSurface).CenterVertical());
        var button = TrackButtons.Action(content, click, externalLink ? TrackButtons.Text : TrackButtons.Tonal);
        settingsActions.Add((button, enabled ?? (() => true)));
        return SettingsRow(title, description, button);
    }

    private FrameworkElement SettingsValue(string title, string description, Func<string> read)
    {
        var value = new TextBlock().Text(read()).FontSize(13).Foreground(Muted).MaxWidth(260).TextWrapping(TextWrapping.Wrap);
        settingsRefreshers.Add(() => value.Text = read());
        return SettingsRow(title, description, value);
    }
}
