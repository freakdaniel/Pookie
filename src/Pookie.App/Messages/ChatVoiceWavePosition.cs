namespace Pookie.App.Messages;

// An absolute audio-frame cursor. New capture packets extend the target; they
// never restart interpolation or move already displayed bars to the right.
internal sealed class ChatVoiceWavePosition
{
    private bool initialized;
    private double lastTime, target;
    internal double Position { get; private set; }
    internal bool Moving => Position < target;
    internal void Reset() { initialized = false; lastTime = target = Position = 0; }
    internal void Append(long nextFrame, double now)
    {
        if (!initialized)
        {
            initialized = true; lastTime = now; Position = Math.Max(0,nextFrame-1);
        }
        else Advance(now);
        target = Math.Max(target,nextFrame);
    }
    internal void Advance(double now)
    {
        if (!initialized || now <= lastTime) return;
        Position = Math.Min(target,Position + (now-lastTime)*25);
        lastTime = now;
    }
}
