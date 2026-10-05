# Application integration checks

This executable contains native UI checks, local browser fixtures, and storage/audio
integration scenarios. It is separate from the production `Pookie` executable

Run from the repository root:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --login-ui-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --storage-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --audio-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --login-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --browser-worker-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --browser-persistence-smoke-test
```

UI and local browser scenarios require a desktop session and the same native
dependencies as the application. Run UI scenarios sequentially: keyboard focus
and window events belong to the shared desktop. UI checks use temporary app data,
synthetic accounts, local audio, and isolated clipboard selections

Set `POOKIE_UI_PREVIEW=/tmp/pookie-preview` to save PPM renders during UI checks.
The `--smoke-test` flag opens the local preview and closes it after five seconds

The following scenarios use the real SoundCloud service and must be run explicitly:

```bash
dotnet run --project tests/Pookie.App.SmokeTests -- --web-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --login-handoff-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --session-smoke-test
dotnet run --project tests/Pookie.App.SmokeTests -- --protected-audio-smoke-test <track-url>
```

The session scenario sends an idempotent like request for an already liked track;
the handoff scenario opens the real sign-in flow. These are not run by `dotnet test`

## Source boundaries

`Views/` contains the verification partials for `MainWindow`; `Auth/` contains local
WebView servers and assertions; `Diagnostics/` contains scenario dispatch, audio/storage
checks, and the clipboard child process

The test executable compiles the application's original sources as linked files,
excluding the production entry point. This lets integration checks inspect private
UI state without adding public test APIs or duplicating application logic. The
dependencies and embedded resources are shared through `Pookie.App.Shared.props`.
Only this project supplies the optional lifecycle hook implementations; the C#
compiler removes unimplemented hooks from the production assembly

The production app keeps its standalone `--demo` preview mode. It contains no
verification methods, test fixture servers, or diagnostic scenario runner
