namespace Pookie.App;

internal sealed partial class MainWindow
{
    // Optional host extensions; unimplemented hooks are erased from the production assembly.
    partial void OnWindowReady();
    partial void OnInitialized();
    partial void OnStartupTransitionCompleted();
    partial void OnDisposed();
}
