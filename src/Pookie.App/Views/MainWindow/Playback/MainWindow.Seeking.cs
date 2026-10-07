using Aprillz.MewUI;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly DispatcherTimer seekTimer;
    private bool seekDragging;
    private bool seeking;
    private double? pendingSeek;
    private CancellationTokenSource? seekRequest;

    private void BeginSeekDrag()
    {
        if (!audioReady || current == null) return;
        seekDragging = true;
        seekTimer.Stop();
    }

    private void QueueSeek(double position)
    {
        if (updatingProgress || !audioReady || current == null || !double.IsFinite(position) ||
            playbackLoading.Value && !seeking) return;
        pendingSeek = Math.Clamp(position, 0, progress.Maximum);
        currentTime.Value = FormatTime(pendingSeek.Value);
        RefreshLikedRows(pendingSeek.Value);
        if (!seekDragging)
        {
            seekTimer.Stop();
            seekTimer.Start();
        }
    }

    private void EndSeekDrag()
    {
        if (!seekDragging) return;
        seekDragging = false;
        FlushSeek();
    }

    private void FlushSeek()
    {
        seekTimer.Stop();
        if (seekDragging || seeking || pendingSeek is not { } target || player == null || disposed) return;
        pendingSeek = null;
        seeking = true;
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        seekRequest = request;
        var audio = player;
        Run(async () =>
        {
            try { await audio.SeekAsync(target, request.Token); }
            finally
            {
                if (seekRequest == request)
                {
                    seekRequest = null;
                    seeking = false;
                    // If input changed while audio was opening, keep only its latest value.
                    if (!seekDragging && pendingSeek != null) FlushSeek();
                }
                request.Dispose();
            }
        });
    }

    private void CancelSeek()
    {
        seekTimer.Stop();
        pendingSeek = null;
        seekDragging = false;
        seeking = false;
        var request = seekRequest;
        seekRequest = null;
        request?.Cancel();
    }
}
