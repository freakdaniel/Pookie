namespace Pookie;

internal static class PookieVersion
{
    internal static string Display { get; } = typeof(PookieVersion).Assembly.GetName().Version!.ToString(3);
}
