using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void OpenExternalUrl(string url, Action failed) => Run(async () =>
    {
        try { await ExternalBrowser.OpenAsync(url, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            if (!disposed) failed();
            Pookie.Logging.AppLog.Failure("Pookie.Links", "Не удалось открыть ссылку в системном браузере", error);
        }
    });
}
