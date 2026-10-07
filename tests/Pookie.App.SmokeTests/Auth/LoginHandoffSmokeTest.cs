using Pookie.SoundCloud;

namespace Pookie.App.Browser;

internal static class LoginHandoffSmokeTest
{
    public static async Task RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var account = await NativeWebLogin.ConnectAsync(timeout.Token);
            await using var browser = new NativeBrowserSession(account);
            var user = await browser.GetMeAsync(timeout.Token);
            if (user.Id <= 0) throw new InvalidOperationException("SoundCloud не подтвердил вход.");
            Console.WriteLine("LOGIN_HANDOFF_OK: native sign-in completed, session transferred, authenticated profile accepted; credentials redacted");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            Environment.ExitCode = 2;
            Console.WriteLine("LOGIN_HANDOFF_UNCONFIRMED: sign-in or session handoff did not finish within 90 seconds");
        }
    }
}
