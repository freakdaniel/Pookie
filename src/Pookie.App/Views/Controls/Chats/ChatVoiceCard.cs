using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed class ChatVoiceCard : ContentControl
{
    internal readonly string Key;
    private readonly Button play;
    private readonly FrameworkElement playGlyph = Icons.View("play-solid",20,Color.FromArgb(225,255,255,255)).Center();
    private readonly FrameworkElement pauseGlyph = Icons.View("pause-solid",20,Color.FromArgb(225,255,255,255)).Center().IsVisible(false);
    private readonly TextBlock time;
    private readonly ChatVoiceWave wave;
    private readonly double duration;
    internal TextBlock DurationLabel => time;
    internal void ReserveFooter(double width) { if (Math.Abs(time.Margin.Right-width)<.1) return; time.Margin = new Thickness(0,0,width,0); }
    internal ChatVoiceCard(string key, ChatVoiceMetadata metadata, Action activate, Action<double> seek)
    {
        Key = key; duration = metadata.Duration; Padding = new Thickness(0); BorderThickness = 0; Background = Color.Transparent;
        play = TrackButtons.Icon(new Grid().Columns("*").Rows("*").Children(playGlyph,pauseGlyph), activate, TrackButtons.Glass, 44).Hint("Прослушать / пауза");
        ((MaterialTrackButton)play).UseCardGlassFallback();
        time = new TextBlock().Text(Clock(duration)).FontSize(11).Foreground(Color.FromArgb(178,255,255,255));
        wave = new ChatVoiceWave { Peaks = metadata.Peaks, Seek = seek };
        Content = new Grid().Columns("44,*").Rows("Auto").Spacing(10).Children(play.Column(0).CenterVertical(), new StackPanel().Vertical().Spacing(3).Children(wave, time).Column(1));
        Width = 280;
    }
    internal void Update(double position, bool playing)
    {
        wave.Progress = duration <= 0 ? 0 : position / duration;
        time.Text = position > 0 ? Clock(position) + " / " + Clock(duration) : Clock(duration);
        playGlyph.IsVisible = !playing; pauseGlyph.IsVisible = playing;
    }
    private static string Clock(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
}
