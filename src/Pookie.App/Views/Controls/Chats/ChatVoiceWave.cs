using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed class ChatVoiceWave : Control
{
    private byte[] peaks = [];
    private double progress;
    private double displayedProgress, playbackTimestamp;
    private double[] emphasis = [];
    private readonly AnimationClock playback = new(TimeSpan.FromSeconds(1), Easing.Linear) { RepeatCount = -1 };
    private readonly List<(long Frame,float Height)> livePeaks = [];
    private readonly ChatVoiceWavePosition position = new();
    private readonly AnimationClock scroll = new(TimeSpan.FromSeconds(1), Easing.Linear) { RepeatCount = -1 };
    private float referencePeak = .05f;
    private long lastFrame = -1;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    internal double LivePosition => position.Position;
    internal bool IsScrolling => scroll.IsRunning;
    internal int PeakCount => peaks.Length;
    internal bool IsLive { get; private set; }
    internal int LiveFrameCount => livePeaks.Count;
    internal long LastLiveFrame => lastFrame;
    internal double DisplayedProgress => displayedProgress;
    internal bool ProgressAnimating => playback.IsRunning;
    internal Action<double>? Seek;
    internal byte[] Peaks
    {
        get => peaks;
        set { if (!IsLive && ReferenceEquals(peaks,value)) return; IsLive = false; scroll.Stop(); ResetProgress(); peaks = value; InvalidateVisual(); }
    }
    internal double Progress
    {
        get => progress;
        set
        {
            var next = double.IsFinite(value) ? Math.Clamp(value,0,1) : 0;
            if (Math.Abs(progress-next)<.00001) return;
            progress = next;
            if (!IsLive && FindVisualRoot() is Window)
            {
                if (!playback.IsRunning) { playbackTimestamp = Now; playback.Start(); }
            }
            else displayedProgress = progress;
            InvalidateVisual();
        }
    }
    internal ChatVoiceWave()
    {
        Height = 28;
        scroll.TickCallback = _ => { position.Advance(Now); InvalidateVisual(); if (!position.Moving) scroll.Stop(); };
        playback.TickCallback = _ => AdvanceProgress();
    }
    private void ResetProgress() { playback.Stop(); progress = displayedProgress = 0; emphasis = []; }
    private void AdvanceProgress()
    {
        var now = Now; var dt = Math.Clamp(now-playbackTimestamp,0,.05); playbackTimestamp = now;
        displayedProgress += (progress-displayedProgress)*(1-Math.Exp(-dt/.045));
        var settled = Math.Abs(progress-displayedProgress)<.00005;
        if (settled) displayedProgress = progress;
        var blend = 1-Math.Exp(-dt/.065);
        for (var i = 0; i < emphasis.Length; i++)
        {
            var target = Math.Clamp(displayedProgress*emphasis.Length-i,0,1);
            emphasis[i] += (target-emphasis[i])*blend;
            if (Math.Abs(target-emphasis[i])<.002) emphasis[i] = target;
            else settled = false;
        }
        if (settled) playback.Stop();
        InvalidateVisual();
    }
    internal void ResetLive()
    {
        scroll.Stop(); ResetProgress(); livePeaks.Clear(); referencePeak = .05f; lastFrame = -1; position.Reset(); IsLive = false; InvalidateVisual();
    }
    internal void UpdateLive(ChatVoiceMeter meter)
    {
        if (!IsLive) { ResetLive(); IsLive = true; }
        var added = 0;
        for (var i = 0; i < meter.Peaks.Length; i++)
        {
            var frame = meter.FirstFrame + i;
            if (frame <= lastFrame) continue;
            var peak = Math.Clamp(float.IsFinite(meter.Peaks[i]) ? meter.Peaks[i] : 0, 0, 1);
            referencePeak = Math.Max(referencePeak, peak);
            livePeaks.Add((frame,peak / referencePeak)); lastFrame = frame; added++;
        }
        if (livePeaks.Count > ChatVoicePcm.MeterCapacity) livePeaks.RemoveRange(0, livePeaks.Count - ChatVoicePcm.MeterCapacity);
        if (added == 0) return;
        position.Append(lastFrame+1,Now);
        if (position.Moving && !scroll.IsRunning && FindVisualRoot() is Window) scroll.Start();
        InvalidateVisual();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    { base.OnMouseDown(e); if (!IsLive && e.Button == MouseButton.Left && Seek != null) { e.Handled = true; Progress = Math.Clamp(e.GetPosition(this).X/Math.Max(1,ActualWidth),0,1); Seek(Progress); } }
    private Brush FadeBrush(Color color)
    {
        var edge = Math.Min(18,Bounds.Width/4);
        var portion = edge/Math.Max(1,Bounds.Width);
        Color Alpha(double value) => Color.FromArgb((byte)Math.Round(color.A*value),color.R,color.G,color.B);
        return new LinearGradientBrush(new Point(Bounds.X,Bounds.Y),new Point(Bounds.Right,Bounds.Y),
            [new(0,Alpha(0)),new(portion*.25,Alpha(.15625)),new(portion*.5,Alpha(.5)),new(portion*.75,Alpha(.84375)),new(portion,color),
             new(1-portion,color),new(1-portion*.75,Alpha(.84375)),new(1-portion*.5,Alpha(.5)),new(1-portion*.25,Alpha(.15625)),new(1,Alpha(0))]);
    }
    protected override void OnRender(IGraphicsContext context)
    {
        var active = FadeBrush(Color.FromRgb(128,196,249));
        if (IsLive)
        {
            position.Advance(Now);
            foreach (var bar in livePeaks)
            {
                var x = Bounds.Right-3 + (bar.Frame+1-position.Position)*6;
                var height = Math.Max(3,bar.Height*(Bounds.Height-3));
                var left = Math.Max(Bounds.X,x); var right = Math.Min(Bounds.Right,x+3);
                if (right > left) context.FillRoundedRectangle(new Rect(left,Bounds.Y+(Bounds.Height-height)/2,right-left,height),1.5,1.5,active);
            }
            return;
        }
        var idleColor = Color.FromArgb(150,186,206,223);
        var activeColor = Color.FromRgb(128,196,249);
        var maximum = peaks.Length == 0 ? 1d : Math.Max(1d,peaks.Max());
        var count = Math.Max(1,(int)(Bounds.Width/4));
        if (emphasis.Length != count) emphasis = Enumerable.Range(0,count).Select(i=>Math.Clamp(displayedProgress*count-i,0,1)).ToArray();
        var brushes = new Dictionary<int,Brush>();
        for (var i = 0; i < count; i++)
        {
            var sample = peaks.Length == 0 ? 0 : peaks[Math.Min(peaks.Length-1,i*peaks.Length/count)];
            var height = Math.Max(3,sample/maximum*(Bounds.Height-3));
            var level = (int)Math.Round(emphasis[i]*63);
            if (!brushes.TryGetValue(level,out var brush)) brushes[level] = brush = FadeBrush(idleColor.Lerp(activeColor,level/63d));
            context.FillRoundedRectangle(new Rect(Bounds.X+i*4,Bounds.Y+(Bounds.Height-height)/2,2,height),1,1,brush);
        }
    }
    protected override void RenderSubtree(IGraphicsContext context)
    { context.Save(); try { context.SetClip(Bounds); base.RenderSubtree(context); } finally { context.Restore(); } }
    protected override void OnDispose() { scroll.Stop(); playback.Stop(); base.OnDispose(); }
}
