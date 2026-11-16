using Aprillz.MewUI;

namespace Pookie.App;

internal sealed class DebouncedAction : IDisposable
{
    private readonly DispatcherTimer timer = new(TimeSpan.FromMilliseconds(250));
    private readonly Action apply;
    internal bool Pending => timer.IsEnabled;
    internal DebouncedAction(Action apply)
    {
        this.apply = apply;
        timer.Tick += Flush;
    }
    internal void Schedule(bool immediately = false)
    { timer.Stop(); if (immediately) apply(); else timer.Start(); }
    internal void Flush() { timer.Stop(); apply(); }
    public void Dispose() => timer.Dispose();
}
