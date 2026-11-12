using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private Button shareTimeAction = null!, shareTimeApply = null!, shareTimeReset = null!;
    private TextBlock shareTimeLabel = null!;
    private Grid shareTimeGrid = null!;
    private ShareTimeFields shareTimeFields = null!;
    private FrameworkElement shareHoursPart = null!, shareMinutesPart = null!, shareHoursColon = null!, shareMinutesColon = null!;
    private Border shareTimePrefix = null!;
    private TextBox shareHours = null!, shareMinutes = null!, shareSeconds = null!;
    private InAppPopup? shareTimePopup;
    private int? shareStartSeconds;
    private bool settingShareTime;
    private readonly AnimationClock shareTimeMotion = new(TimeSpan.FromMilliseconds(260), Easing.CubicBezier(.2, 0, 0, 1));

    private Button ShareTimeButton()
    {
        shareTimeLabel = new TextBlock().FontSize(13).SemiBold().CenterVertical();
        shareTimePrefix = new Border().Width(0).Opacity(0).ClipToBounds().Child(shareTimeLabel);
        shareTimeAction = TrackButtons.Icon(new StackPanel().Horizontal().Center().Children(
            shareTimePrefix, Icons.View("clock", 20)), ToggleShareTime, TrackButtons.Glass, 48).Hint("Таймкод");
        ((MaterialTrackButton)shareTimeAction).UseLightGlassFinish();
        return shareTimeAction;
    }

    private void UpdateShareTimeLabel(bool animate = true)
    {
        shareTimeMotion.Stop();
        var selected = shareStartSeconds.HasValue;
        var targetWidth = 0d;
        if (selected)
        {
            var text = FormatTime(shareStartSeconds!.Value);
            if (shareTimeLabel.Text != text) shareTimePrefix.Opacity = 0;
            shareTimeLabel.Text = text; shareTimeLabel.Width = double.NaN;
            shareTimeLabel.Measure(new Size(double.PositiveInfinity, 48));
            shareTimeLabel.Width = shareTimeLabel.DesiredSize.Width;
            targetWidth = shareTimeLabel.DesiredSize.Width + 8;
        }
        shareTimeAction.Hint(selected ? "Таймкод " + shareTimeLabel.Text + " · Изменить" : "Таймкод");
        var fromWidth = shareTimePrefix.Width; var fromOpacity = shareTimePrefix.Opacity;
        void Present(double amount)
        {
            var width = fromWidth + (targetWidth - fromWidth) * amount;
            shareTimePrefix.Width = width;
            shareTimePrefix.Opacity = fromOpacity + ((selected ? 1 : 0) - fromOpacity) * amount;
            shareTimeAction.Width = 48 + width;
        }
        if (!animate || !shareDialog.IsOpen) Present(1);
        else { shareTimeMotion.TickCallback = Present; shareTimeMotion.Start(); }
    }

    private void ToggleShareTime()
    {
        if (shareTimePopup?.IsOpen == true) { shareTimePopup.Close(); return; }
        if (!shareTimeAction.IsEnabled || shareTarget?.Track is not { } track) return;
        if (shareTimePopup == null) CreateShareTimePopup();
        var hours = track.DurationSeconds >= 3600;
        shareHours.IsVisible = shareHoursPart.IsVisible = shareHoursColon.IsVisible = hours;
        shareMinutes.IsVisible = shareMinutesPart.IsVisible = shareMinutesColon.IsVisible = true;
        shareTimeGrid.Columns(hours ? "72,16,72,16,72" : "0,0,72,16,72");
        var seconds = shareStartSeconds ?? 0;
        settingShareTime = true;
        shareHours.Text = (seconds / 3600).ToString("00", CultureInfo.InvariantCulture);
        shareMinutes.Text = (seconds / 60 % 60).ToString("00", CultureInfo.InvariantCulture);
        shareSeconds.Text = (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
        settingShareTime = false;
        shareTimeFields.SetInvalid(false, false);
        shareTimeReset.IsEnabled = shareStartSeconds.HasValue;
        ValidateShareTime();
        var first = hours ? shareHours : shareMinutes;
        shareTimePopup!.ShowPopup(shareTimeAction, first);
        first.SelectAll();
    }

    private void CreateShareTimePopup()
    {
        TextBox Field(int length) => new TextBox().MaxLength(length).FontSize(24)
            .Width(72).Height(56).Padding(10, 8).CornerRadius(12).Background(TrackButtons.TonalSurface)
            .Foreground(TrackButtons.OnSurface).BorderThickness(0);
        shareHours = Field(6); shareMinutes = Field(2); shareSeconds = Field(2);
        FrameworkElement Part(TextBox field, string label) => new StackPanel().Vertical().Spacing(6).Children(
            field, new TextBlock().Text(label).FontSize(11).Foreground(Muted).CenterHorizontal());
        TextBlock Colon() => new TextBlock().Text(":").FontSize(24).Height(56).VerticalTextAlignment(TextAlignment.Center).CenterHorizontal();
        shareHoursPart = Part(shareHours, "Часы"); shareMinutesPart = Part(shareMinutes, "Минуты");
        shareHoursColon = Colon(); shareMinutesColon = Colon();
        shareTimeGrid = new Grid().Columns("72,16,72,16,72").Children(shareHoursPart, shareHoursColon.Column(1),
            shareMinutesPart.Column(2), shareMinutesColon.Column(3), Part(shareSeconds, "Секунды").Column(4));
        shareTimeFields = new ShareTimeFields(shareTimeGrid, shareHours, shareMinutes, shareSeconds).CenterHorizontal();
        shareTimeReset = TrackButtons.Action(new TextBlock().Text("Сбросить").FontSize(13).Foreground(TrackButtons.OnSurface), () => ApplyShareTime(null), TrackButtons.Text);
        shareTimeApply = TrackButtons.Action(new TextBlock().Text("Готово").FontSize(13).SemiBold().Foreground(TrackButtons.OnPrimary), CommitShareTime, TrackButtons.Filled);
        var card = new GlassCard { HoverEnabled = false }.MaxWidth(300).Background(PopupAppearance.Surface).BorderThickness(1)
            .BorderBrush(TrackButtons.TonalSurface).CornerRadius(20).Padding(20).Child(
                new StackPanel().Vertical().Spacing(16).Children(new TextBlock().Text("Таймкод").FontSize(16).SemiBold().CenterHorizontal(),
                    shareTimeFields,
                    new StackPanel().Horizontal().Spacing(8).CenterHorizontal().Children(shareTimeReset, shareTimeApply)));
        shareTimePopup = new InAppPopup();
        shareTimePopup.InitializePopup(card, () => shareTimeAction.Bounds, () => MenuPlacement.Below, alignCenter: true);
        foreach (var field in new[] { shareHours, shareMinutes, shareSeconds })
        { field.OnTextChanged(_ => { if (!settingShareTime) ValidateShareTime(); }); field.GotFocus += field.SelectAll; }
    }

    private bool TryShareTime(out int seconds)
    {
        seconds = 0;
        if (shareTarget?.Track is not { } track || !double.IsFinite(track.DurationSeconds) || track.DurationSeconds <= 0) return false;
        var parts = new[] { shareHours.IsVisible ? shareHours.Text : "0", shareMinutes.IsVisible ? shareMinutes.Text : "0", shareSeconds.Text };
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 ||
                index > 0 && value > 59 || seconds > (int.MaxValue - value) / 60) return false;
            seconds = seconds * 60 + value;
        }
        return seconds <= track.DurationSeconds;
    }

    private void ValidateShareTime()
    {
        var valid = TryShareTime(out _);
        shareTimeApply.IsEnabled = valid;
        shareTimeFields.SetInvalid(!valid);
    }

    private void CommitShareTime() { if (TryShareTime(out var seconds)) ApplyShareTime(seconds); }

    private void ApplyShareTime(int? seconds)
    {
        shareStartSeconds = seconds; shareTimePopup?.Close();
        UpdateShareTimeLabel(); RefreshShareLink();
    }

    private bool HandleShareTimeKey(KeyEventArgs e)
    {
        if (shareTimePopup?.IsOpen != true) return false;
        if (e.Key == Key.Escape) { shareTimePopup.Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { CommitShareTime(); e.Handled = true; }
        else if (e.Key == Key.Tab)
        {
            UIElement[] stops = [shareHours, shareMinutes, shareSeconds, shareTimeReset, shareTimeApply];
            stops = stops.Where(element => element.IsVisible && element.IsEffectivelyEnabled).ToArray();
            var index = Array.IndexOf(stops, Window.FocusManager.FocusedElement);
            var direction = e.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            stops[(index + direction + stops.Length) % stops.Length].Focus(); e.Handled = true;
        }
        return true;
    }
}
