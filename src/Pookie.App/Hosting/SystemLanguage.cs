using System.Globalization;
using System.Runtime.InteropServices;
using Pookie.Localization;

namespace Pookie.App.Hosting;

internal static partial class SystemLanguage
{
    // Capture the OS preference before installing any application override.
    private static readonly Lazy<CultureInfo> SystemPreference = new(Detect);
    private static readonly bool NeedsRegionalFallback = CultureInfo.CurrentCulture.Equals(CultureInfo.InvariantCulture);

    internal static void Initialize(string? overrideLanguage = null)
    {
        var system = SystemPreference.Value;
        var language = string.IsNullOrEmpty(overrideLanguage) ? system : UiText.ResolveLanguage([overrideLanguage, system.Name]);
        UiText.SetLanguage(language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = language;
        if (NeedsRegionalFallback)
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture(language.Name);
    }

    private static CultureInfo Detect()
    {
        var preferred = new List<string?>();
        if (OperatingSystem.IsWindows()) AddWindowsLanguages(preferred);
        else if (OperatingSystem.IsMacOS()) AddMacLanguages(preferred);
        else
        {
            var locale = new[] { "LC_ALL", "LC_MESSAGES", "LANG" }.Select(Environment.GetEnvironmentVariable)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (locale is not ("C" or "POSIX") && Environment.GetEnvironmentVariable("LANGUAGE") is { } languages)
                preferred.AddRange(languages.Split(':'));
            preferred.Add(locale);
        }

        preferred.Add(CultureInfo.CurrentUICulture.Name);
        return UiText.ResolveLanguage(preferred);
    }

    private static unsafe void AddWindowsLanguages(List<string?> result)
    {
        const uint names = 8; // MUI_LANGUAGE_NAME, double-NUL-terminated list.
        uint length = 0;
        if (GetUserPreferredUILanguages(names, out _, null, ref length) == 0 || length <= 1) return;
        var buffer = new char[length];
        fixed (char* data = buffer)
        {
            if (GetUserPreferredUILanguages(names, out _, data, ref length) != 0)
                result.AddRange(new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    private static unsafe void AddMacLanguages(List<string?> result)
    {
        var languages = CFLocaleCopyPreferredLanguages();
        if (languages == 0) return;
        try
        {
            var count = CFArrayGetCount(languages);
            byte* buffer = stackalloc byte[256];
            for (nint index = 0; index < count; index++)
            {
                var name = CFArrayGetValueAtIndex(languages, index);
                if (CFStringGetCString(name, buffer, 256, 0x08000100) != 0)
                    result.Add(Marshal.PtrToStringUTF8((nint)buffer));
            }
        }
        finally { CFRelease(languages); }
    }

    private const string Core = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [LibraryImport("kernel32.dll")] private static unsafe partial int GetUserPreferredUILanguages(uint flags, out uint count, char* buffer, ref uint length);
    [LibraryImport(Core)] private static partial nint CFLocaleCopyPreferredLanguages();
    [LibraryImport(Core)] private static partial nint CFArrayGetCount(nint array);
    [LibraryImport(Core)] private static partial nint CFArrayGetValueAtIndex(nint array, nint index);
    [LibraryImport(Core)] private static unsafe partial byte CFStringGetCString(nint value, byte* buffer, nint capacity, uint encoding);
    [LibraryImport(Core)] private static partial void CFRelease(nint value);
}
