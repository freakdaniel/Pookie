using Aprillz.MewUI;
using Pookie.App;
using Pookie.App.Diagnostics;
using Pookie.App.Auth;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task RunAsync(string[] args)
    {
        if (args.Length >= 4 && args[0] == "--web-requests" && args[2] == "--login-profile")
        {
            var fixture = args.Length == 6 && args[4] == "--login-fixture" ? args[5] : null;
            Environment.ExitCode = NativeBrowserSession.RunChild(args[1], args[3], fixture);
            return;
        }
        if (args.Length >= 4 && args[0] == "--web-login" && args[2] == "--login-profile")
        {
            var fixture = args.Length == 6 && args[4] == "--login-fixture" ? args[5] : null;
            Environment.ExitCode = NativeWebLogin.RunChild(args[1], args[3], fixture);
            return;
        }

        if (await DiagnosticsRunner.TryRunAsync(args)) return;

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
            Console.Error.WriteLine($"UI_ERROR: {eventArgs.Exception.GetType().Name}");
            if (args.Contains("--login-ui-smoke-test")) Console.Error.WriteLine(eventArgs.Exception);
            Environment.ExitCode = 1;
            Application.Shutdown();
            eventArgs.Handled = true;
        };
        using var fontStream = typeof(Program).Assembly.GetManifestResourceStream("Pookie.App.Assets.Fonts.GoogleSans.ttf")
            ?? throw new InvalidOperationException("Встроенный шрифт Google Sans не найден.");
        using var appFont = FontResources.Register(fontStream, ".ttf", "Google Sans");
        using var brandStream = typeof(Program).Assembly.GetManifestResourceStream("Pookie.App.Assets.Fonts.Bungee-Regular.ttf")
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
        using var native = new MainWindow(args, brandFont.FontFamily);
        Application.Create().UseTheme(ThemeVariant.Dark).UseAccent(Color.FromRgb(180, 180, 180)).Run(native.Window);
        await native.FinishShutdownAsync().ConfigureAwait(false);
    }
}
