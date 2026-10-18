using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> chatVideoExpanded = new(false);
    private readonly AnimationClock chatVideoExpansionClock = new(TimeSpan.FromMilliseconds(300), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly AnimationClock chatVideoSpeedClock = new(TimeSpan.FromMilliseconds(150), Easing.CubicBezier(.2, 0, 0, 1));
    private ChatVideoLayout chatVideoLayout = null!;
    private Slider chatVideoVolume = null!, chatVideoSpeedSlider = null!;
    private TextBlock chatVideoSpeedValue = null!;
    private bool chatVideoSyncSpeed;
    private TextBlock chatVideoRemaining = null!;
    private Button chatVideoGear = null!;
    private ShadowDecorator chatVideoSpeedMenu = null!;
    private sealed record ChatVideoRateChoice(Button Button, TextBlock Rate, TextBlock Label, TextBlock Check);
    private readonly Dictionary<double, ChatVideoRateChoice> chatVideoRateChoices = [];
    private double chatVideoLastVolume = 100;
    private double chatVideoExpansion, chatVideoExpansionFrom, chatVideoSpeedReveal, chatVideoSpeedFrom, chatVideoRate = 1;
    private bool chatVideoSpeedOpen;
    private DateTimeOffset chatVideoInteraction;

    private void SetChatVideoExpanded(bool expanded, bool immediate = false)
    {
        chatVideoExpanded.Value = expanded; chatVideoExpansionFrom = chatVideoExpansion;
        chatVideoExpansionClock.Stop();
        void Apply(double value)
        {
            chatVideoExpansion = value; chatVideoLayout.Expansion = value; chatVideoSurface.Expansion = value;
            chatVideoHeader.Opacity = 1 - value; chatVideoHeader.IsHitTestVisible = value < .5;
            chatVideoSurface.InvalidateVisual();
        }
        if (immediate) Apply(expanded ? 1 : 0);
        else
        {
            chatVideoExpansionClock.TickCallback = t => Apply(chatVideoExpansionFrom + ((chatVideoExpanded.Value ? 1 : 0) - chatVideoExpansionFrom) * t);
            chatVideoExpansionClock.Start();
        }
        RevealChatVideoControls();
    }
    private void SetChatVideoSpeedMenu(bool open)
    {
        if (!chatVideoOpen.Value && open) return;
        chatVideoSpeedOpen = open; chatVideoSpeedFrom = chatVideoSpeedReveal; chatVideoSpeedClock.Stop();
        chatVideoSpeedMenu.IsHitTestVisible = open;
        chatVideoSpeedClock.TickCallback = t =>
        {
            chatVideoSpeedReveal = chatVideoSpeedFrom + ((chatVideoSpeedOpen ? 1 : 0) - chatVideoSpeedFrom) * t;
            chatVideoSpeedMenu.Opacity = chatVideoSpeedReveal;
        };
        chatVideoSpeedClock.Start(); RevealChatVideoControls();
    }
    private void SetChatVideoRate(double rate, bool closeMenu = true)
    {
        rate = Math.Round(Math.Clamp(rate, .25, 2) * 20) / 20;
        if (chatVideoSession is { } session && !session.Rate(rate)) return;
        chatVideoRate = rate; chatVideoSpeedValue.Text = rate.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + "×";
        chatVideoSyncSpeed = true;
        try { chatVideoSpeedSlider.Value = rate; }
        finally { chatVideoSyncSpeed = false; }
        foreach (var pair in chatVideoRateChoices)
        {
            var selected = Math.Abs(pair.Key - rate) < .001;
            pair.Value.Rate.Foreground = pair.Value.Label.Foreground = selected ? ChatAccent : TrackButtons.OnSurface;
            pair.Value.Check.Opacity = selected ? 1 : 0;
        }
        if (closeMenu) SetChatVideoSpeedMenu(false);
    }
    private void SetChatVideoMuted(bool muted)
    {
        if (!muted && chatVideoVolume.Value == 0) chatVideoVolume.Value = chatVideoLastVolume;
        chatVideoMuted = muted; chatVideoSession?.Mute(muted);
        chatVideoMuteIcon.Source = Icons.Source(muted ? "speaker-slash" : "speaker-high", Color.White);
    }
    private void BuildChatVideoControls()
    {
        chatVideoPlayIcon = Icons.View("pause-solid", 24, Color.White);
        chatVideoMuteIcon = Icons.View("speaker-high", 18, Color.White);
        chatVideoTime = new TextBlock().Text("0:00").FontSize(11).Foreground(TrackButtons.OnSurface).CenterVertical();
        chatVideoRemaining = new TextBlock().Text("−0:00").FontSize(11).Foreground(TrackButtons.OnSurface).Right().CenterVertical();
        chatVideoVolume = ThinSlider().Minimum(0).Maximum(100).Value(100).Width(72).CenterVertical();
        chatVideoVolume.ValueChanged += value => { if (value > 0) chatVideoLastVolume = value; chatVideoSession?.Volume((int)Math.Round(value)); SetChatVideoMuted(value == 0); RevealChatVideoControls(); };
        chatVideoSeek = ThinSlider().Minimum(0).Maximum(1).CenterVertical();
        chatVideoSeek.ValueChanged += value => { if (!chatVideoUpdating) { chatVideoSession?.Seek(value); RevealChatVideoControls(); } };
        chatVideoGear = PlayerButton(Icons.View("gear", 18, Color.White),
            () => SetChatVideoSpeedMenu(!chatVideoSpeedOpen)).Width(32).Height(32);
        var top = new Grid().Columns("*,40,*").Rows("40").Margin(-7, 0, -7, 0).Children(
            new StackPanel().Horizontal().Spacing(4).Left().CenterVertical().Children(
                PlayerButton(chatVideoMuteIcon, () => { SetChatVideoMuted(!chatVideoMuted); RevealChatVideoControls(); }).Width(32).Height(32), chatVideoVolume),
            PlayerButton(chatVideoPlayIcon, () => { if (chatVideoSession is { } session) Run(session.ToggleAsync); RevealChatVideoControls(); }).Width(40).Height(40).Column(1),
            new StackPanel().Horizontal().Spacing(4).Right().CenterVertical().Column(2).Children(
                PlayerButton(Icons.View("arrows-out", 22, Color.White), () => SetChatVideoExpanded(!chatVideoExpanded.Value), chatVideoExpanded).Width(32).Height(32), chatVideoGear));
        var timeline = new Grid().Columns("42,*,48").Rows("20").Spacing(6).Children(chatVideoTime, chatVideoSeek.Column(1), chatVideoRemaining.Column(2));
        chatVideoControls = new Border().Background(Color.FromArgb(235, 8, 8, 8)).BorderThickness(0).CornerRadius(12).Padding(12, 6)
            .Child(new Grid().Columns("*").Rows("40,20").Spacing(2).Children(top, timeline.Row(1)));
        chatVideoControls.Transitions = [Transition.Create(UIElement.OpacityProperty, 180, Easing.CubicBezier(.2, 0, 0, 1))];
        chatVideoControls.MouseEnter += RevealChatVideoControls;
        chatVideoControls.MouseMove += _ => RevealChatVideoControls();
        chatVideoSpeedValue = new TextBlock().Text("1×").FontSize(12).SemiBold().CenterVertical();
        chatVideoSpeedSlider = ThinSlider().Minimum(.25).Maximum(2).Value(1).CenterVertical();
        chatVideoSpeedSlider.ValueChanged += value => { if (!chatVideoSyncSpeed) SetChatVideoRate(value, closeMenu: false); };
        var speedHeader = new Grid().Columns("38,*").Rows("32").Spacing(6).Margin(8, 0).Children(chatVideoSpeedValue, chatVideoSpeedSlider.Column(1));
        var choices = new StackPanel().Vertical().Spacing(2).Children(speedHeader,
            new Border().Height(1).Background(Color.FromRgb(63, 63, 66)).BorderThickness(0).Margin(0, 4));
        foreach (var (rate, title) in new[] { (.5, "Медленно"), (1d, "По умолчанию"), (1.25, "Ускоренно"),
            (1.5, "Быстро"), (1.75, "Очень быстро"), (2d, "Сверхбыстро") })
        {
            var selected = rate == chatVideoRate;
            var rateText = new TextBlock().Text(rate.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + "×").FontSize(10).SemiBold().CenterVertical();
            var label = new TextBlock().Text(title).FontSize(13).CenterVertical();
            rateText.Foreground = label.Foreground = selected ? ChatAccent : TrackButtons.OnSurface;
            var check = new TextBlock().Text("✓").FontSize(14).Foreground(ChatAccent).CenterVertical(); check.Opacity = selected ? 1 : 0;
            var content = new Grid().Columns("34,*,18").Rows("34").Spacing(6).Children(rateText, label.Column(1), check.Column(2));
            var choice = TrackButtons.Action(content, () => SetChatVideoRate(rate), TrackButtons.Text, 34).CornerRadius(6).Padding(8, 0);
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            chatVideoRateChoices[rate] = new(choice, rateText, label, check); choices.Add(choice);
        }
        chatVideoSpeedMenu = new ShadowDecorator().BlurRadius(12).OffsetY(3).ShadowColor(Color.FromArgb(90, 0, 0, 0)).CornerRadius(12)
            .Child(new Border().Background(Color.FromRgb(43, 43, 43)).CornerRadius(12).BorderThickness(0).Padding(6).Width(232).Child(choices));
        chatVideoSpeedMenu.Opacity = 0; chatVideoSpeedMenu.IsHitTestVisible = false;
        hoverReveals.Add(new HoverReveal(chatVideoGear, SetChatVideoSpeedMenu, () => chatVideoSpeedMenu.IsMouseOver || chatVideoSpeedSlider.IsMouseCaptured));
        hoverReveals.Add(new HoverReveal(chatVideoSpeedMenu, SetChatVideoSpeedMenu, () => chatVideoGear.IsMouseOver || chatVideoSpeedSlider.IsMouseCaptured));
    }
    private void RefreshChatVideoControlVisibility()
    {
        if (chatVideoExpanded.Value && DateTimeOffset.UtcNow - chatVideoInteraction > TimeSpan.FromMilliseconds(1400) &&
            !chatVideoControls.IsMouseOver && !chatVideoPrevious.IsMouseOver && !chatVideoNext.IsMouseOver && !chatVideoSpeedOpen && !chatVideoSeek.IsMouseCaptured && !chatVideoVolume.IsMouseCaptured)
            chatVideoControls.Opacity = chatVideoPrevious.Opacity = chatVideoNext.Opacity = 0;
    }
    private void ResetChatVideoControls(bool keepExpansion = false)
    {
        if (!keepExpansion) chatVideoExpansionClock.Stop();
        chatVideoSpeedClock.Stop();
        chatVideoSpeedOpen = false; chatVideoSpeedReveal = 0;
        chatVideoSpeedMenu.Opacity = 0; chatVideoSpeedMenu.IsHitTestVisible = false;
    }
}
