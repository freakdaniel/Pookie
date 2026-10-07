using Pookie.App.Diagnostics;
using Aprillz.MewUI;
using Pookie.Logging;

namespace Pookie.App.Hosting;

internal static class AppHost
{
    internal static async Task RunAsync(AppRunOptions options, Action<MainWindow>? configureWindow = null)
    {
        StartupLog.Event("app.host-start");
#if POOKIE_LINUX
        X11Platform.Register(); MewVGX11Backend.Register();
#elif POOKIE_WINDOWS
        Win32Platform.Register(); Direct2DBackend.Register();
#elif POOKIE_MACOS
        MacOSPlatform.Register(); MewVGMacOSBackend.Register();
#else
        if (OperatingSystem.IsLinux()) { X11Platform.Register(); MewVGX11Backend.Register(); }
        else if (OperatingSystem.IsWindows()) { Win32Platform.Register(); Direct2DBackend.Register(); }
        else if (OperatingSystem.IsMacOS()) { MacOSPlatform.Register(); MewVGMacOSBackend.Register(); }
        else throw new PlatformNotSupportedException("Unsupported desktop platform.");
#endif

        Application.DispatcherUnhandledException += eventArgs =>
        {
            AppLog.Failure("Pookie.UI", "Ошибка потока интерфейса", eventArgs.Exception);
            Environment.ExitCode = 1;
            Application.Shutdown();
            eventArgs.Handled = true;
        };
        using var fontStream = typeof(AppHost).Assembly.GetManifestResourceStream("Pookie.App.Assets.Fonts.GoogleSans.ttf")
            ?? throw new InvalidOperationException("Встроенный шрифт Google Sans не найден.");
        using var appFont = FontResources.Register(fontStream, ".ttf", "Google Sans");
        using var brandStream = typeof(AppHost).Assembly.GetManifestResourceStream("Pookie.App.Assets.Fonts.Bungee-Regular.ttf")
            ?? throw new InvalidOperationException("Встроенный шрифт Bungee не найден.");
        using var brandFont = FontResources.Register(brandStream, ".ttf", "Bungee");
        ThemeManager.DefaultDarkSeed = ThemeSeed.DefaultDark with
        {
            WindowBackground = Color.FromRgb(34, 34, 34),
            WindowText = Color.FromRgb(236, 236, 236),
            ControlBackground = Color.FromRgb(24, 24, 24),
            ButtonFace = Color.FromRgb(43, 43, 43),
            ButtonDisabledBackground = Color.FromRgb(35, 35, 35)
        };
        ThemeManager.DefaultAccentColor = Color.FromRgb(180, 180, 180);
        ThemeManager.DefaultMetrics = ThemeMetrics.Default with { ControlCornerRadius = 10, FontFamily = appFont.FontFamily };
        using var native = StartupLog.Run("app.main-window-create", () => new MainWindow(options, brandFont.FontFamily));
        configureWindow?.Invoke(native);
        try { Application.Create().UseTheme(ThemeVariant.Dark).UseAccent(Color.FromRgb(180, 180, 180)).Run(native.Window); }
        finally
        {
            try { await native.FinishShutdownAsync().ConfigureAwait(false); }
            finally
            {
                AppLog.For("Pookie").Information("Приложение закрыто");
                AppLog.Shutdown();
                native.CleanupIsolatedData();
            }
        }
    }
}
