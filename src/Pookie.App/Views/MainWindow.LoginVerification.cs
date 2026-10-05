using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private int loginTransitionFrames;
    private string? loginTransitionFailure;
    private long startupPresentedAt;
    private double startupFullyVisibleMs;
    private int startupPreviewStep;

    private void SampleLoginTransition()
    {
        if (signedIn.Value) return;
        if (startupSplash.IsVisible && startupBrandLayer.IsVisible)
        {
            if (startupPresentedAt == 0 && startupLogo.Width == 132 && startupLogo.Height == 124 &&
                startupLogo.Opacity == 1 && startupSpinner.Opacity == 1 &&
                Math.Abs(startupLogo.ActualWidth - 132) < 1 && Math.Abs(startupLogo.ActualHeight - 124) < 1)
            {
                startupPresentedAt = Stopwatch.GetTimestamp();
                CaptureUiPreview("startup-presented");
            }
            if (startupBrand.Opacity < 1 && startupFullyVisibleMs == 0)
            {
                startupFullyVisibleMs = Stopwatch.GetElapsedTime(startupPresentedAt).TotalMilliseconds;
                if (startupPresentedAt == 0 || startupFullyVisibleMs < StartupMinimumVisibleDurationMs)
                    loginTransitionFailure ??= "Startup disappeared before its fully visible minimum duration";
            }
        }
        if (workspace.IsEnabled || workspace.IsHitTestVisible)
            loginTransitionFailure ??= "Unauthenticated workspace accepted input";
        if (startupSplash.IsVisible && workspace.IsVisible)
            loginTransitionFailure ??= "Workspace appeared during unauthenticated startup";
        if (loginScreen.IsVisible && (loginScreen.Opacity is > 0 and < 1 || startupSplash.Opacity is > 0 and < 1))
        {
            loginTransitionFrames++;
            if (startupSplash.IsVisible)
            {
                if (loginScreen.Opacity != 1)
                    loginTransitionFailure ??= "Startup handoff exposed a translucent background";
                if (startupPreviewStep < 4 && 1 - startupSplash.Opacity >= startupPreviewStep / 4.0)
                    CaptureUiPreview($"startup-handoff-{startupPreviewStep++}");
            }
        }
    }

    private async Task WaitForLoginFrameAsync()
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendered() => rendered.TrySetResult();
        Window.FrameRendered += OnRendered;
        try
        {
            Window.InvalidateVisual();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
        }
        finally { Window.FrameRendered -= OnRendered; }
    }

    private async Task VerifyLoginUiAsync()
    {
        try
        {
            await WaitForLoginFrameAsync();
            if (loginTransitionFailure != null) throw new InvalidOperationException(loginTransitionFailure);
            if (loginTransitionFrames < 3) throw new InvalidOperationException("Login entrance did not render intermediate frames");
            if (startupFullyVisibleMs < StartupMinimumVisibleDurationMs)
                throw new InvalidOperationException("Startup did not render a complete one-second hold");
            if (startupSplash.IsVisible || !loginScreen.IsVisible || workspace.IsVisible)
                throw new InvalidOperationException("Unauthenticated startup did not finish on the login screen");
            var client = Window.ClientSize;
            var bounds = loginScreen.Bounds;
            if (Math.Abs(bounds.Width - client.Width) > 1 || Math.Abs(bounds.Height - client.Height) > 1)
                throw new InvalidOperationException("Login screen did not cover the client area");
            if (loginButton.ActualWidth < 300 || loginButton.ActualHeight < 50)
                throw new InvalidOperationException("Login action was not arranged at its intended size");
            VerifyLoginComposition();
            CaptureUiPreview("login");
            Window.WindowSize = WindowSize.Resizable(1000, 680, minWidth: 1000, minHeight: 680);
            await WaitForLoginFrameAsync();
            if (Math.Abs(loginScreen.ActualWidth - Window.ClientSize.Width) > 1 ||
                Math.Abs(loginScreen.ActualHeight - Window.ClientSize.Height) > 1 ||
                loginContent.Bounds.Y < 0 || loginContent.Bounds.Bottom > loginScreen.Bounds.Bottom)
                throw new InvalidOperationException("Login layout escaped the viewport after resize");
            VerifyLoginComposition();
            CaptureUiPreview("login-narrow");

            loginBusy.Value = true;
            await VerifyLoginButtonStateAsync(LoginButtonState.Waiting, "login-waiting");
            await VerifyLoginButtonStateAsync(LoginButtonState.Connecting, "login-connecting");
            loginBusy.Value = false;
            await VerifyLoginButtonStateAsync(LoginButtonState.Idle, null);

            await NavigateAsync(Page.Feed);
            query.Value = "fixture search";
            await SearchAsync();
            await PlayAsync(new SoundCloudTrack { Id = 42, Title = "fixture" });
            if (page.Value != Page.Home || tracks.Count != 0 || current != null)
                throw new InvalidOperationException("Unauthenticated navigation, search or playback was allowed");
            await LoginAsync(); // Demo transport cannot connect; the screen must remain usable for retry.
            if (loginBusy.Value || !loginButton.IsEnabled || !loginScreen.IsVisible || workspace.IsVisible)
                throw new InvalidOperationException("Unsuccessful login did not restore the login action");
            if (loginButtonState != LoginButtonState.Idle || loginSpinner.IsActive)
                throw new InvalidOperationException("Unsuccessful login did not restore the ordinary sign-in state");

            // Exercise the accepted-account handoff without creating or saving a real account.
            signedIn.Value = true;
            await ShowWorkspaceAsync();
            await WaitForLoginFrameAsync();
            if (!workspace.IsVisible || !workspace.IsEnabled || loginScreen.IsVisible)
                throw new InvalidOperationException("Accepted account did not reveal the workspace");
            if (loginSpinner.IsActive)
                throw new InvalidOperationException("Hidden login screen kept its animations active");
            await LogoutAsync();
            await WaitForLoginFrameAsync();
            if (signedIn.Value || workspace.IsVisible || workspace.IsEnabled || !loginScreen.IsVisible || loginBusy.Value)
                throw new InvalidOperationException("Logout did not return to the login screen");
            if (loginButtonState != LoginButtonState.Idle)
                throw new InvalidOperationException("Logout did not reset the login action");
            if (loginTransitionFailure != null) throw new InvalidOperationException(loginTransitionFailure);
            Console.WriteLine($"LOGIN_UI_SMOKE_OK: fully appeared loader held for {startupFullyVisibleMs:F0} ms; {loginTransitionFrames} transition frames; opaque startup handoff, clipped sign-in/waiting/connecting animation, centered form, blocked unauthenticated actions, retry, accepted-account transition and logout");
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            Console.Error.WriteLine($"LOGIN_UI_SMOKE_FAILED: {error.GetType().Name}: {error.Message}");
        }
        finally { Window.Close(); }
    }

    private async Task VerifyLoginButtonStateAsync(LoginButtonState state, string? previewName)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var surroundings = CaptureLoginButtonSurroundings();
        var frames = 0;
        var previewCaptured = false;
        string? overflowFailure = null;
        void OnRendered()
        {
            if (loginLabelHost.ContentOpacity is <= 0 or >= 1) return;
            frames++;
            if (!surroundings.AsSpan().SequenceEqual(CaptureLoginButtonSurroundings()))
                overflowFailure ??= "Animated login label painted outside the button";
            if (!previewCaptured && loginLabelHost.ContentOpacity >= 0.4)
            {
                previewCaptured = true;
                CaptureUiPreview($"login-label-{state.ToString().ToLowerInvariant()}-transition");
            }
        }
        void OnCompleted() => completed.TrySetResult();
        loginLabelHost.TransitionCompleted += OnCompleted;
        Window.FrameRendered += OnRendered;
        try
        {
            SetLoginButtonState(state);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token);
            if (overflowFailure != null) throw new InvalidOperationException(overflowFailure);
            if (frames < 3) throw new InvalidOperationException("Login label did not render intermediate frames");
            await WaitForLoginFrameAsync();
            VerifyLoginComposition();
            var waiting = state != LoginButtonState.Idle;
            if (loginSpinner.IsActive != waiting || loginButton.IsEnabled == waiting)
                throw new InvalidOperationException("Login button activity disagreed with its state");
            if (previewName != null) CaptureUiPreview(previewName);
        }
        finally
        {
            loginLabelHost.TransitionCompleted -= OnCompleted;
            Window.FrameRendered -= OnRendered;
        }
    }

    private byte[] CaptureLoginButtonSurroundings()
    {
        var scale = Window.DpiScale;
        var width = (int)Math.Ceiling(Window.ClientSize.Width * scale);
        var height = (int)Math.Ceiling(Window.ClientSize.Height * scale);
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        using var surface = Window.GraphicsFactory.CreateSurface(Aprillz.MewUI.Rendering.RenderSurfaceDescriptor.CpuPixels(width, height, scale));
        using var context = Window.GraphicsFactory.CreateContext(surface);
        context.BeginFrame(surface);
        Window.Content?.Render(context);
        context.EndFrame();
        if (surface is not Aprillz.MewUI.Rendering.ICpuPixelSurface cpu)
            throw new InvalidOperationException("Button verification surface is not readable");
        var pixels = cpu.GetReadOnlyPixelSpan();
        var left = (int)Math.Ceiling(loginButton.Bounds.X * scale);
        var right = (int)Math.Floor(loginButton.Bounds.Right * scale);
        var top = (int)Math.Floor(loginButton.Bounds.Y * scale) - 1;
        var bottom = (int)Math.Ceiling(loginButton.Bounds.Bottom * scale) + 1;
        var stripHeight = (int)Math.Ceiling(20 * scale);
        var rowBytes = (right - left) * 4;
        var result = new byte[rowBytes * stripHeight * 2];
        for (var row = 0; row < stripHeight; row++)
        {
            pixels.Slice((top - stripHeight + row) * cpu.StrideBytes + left * 4, rowBytes)
                .CopyTo(result.AsSpan(row * rowBytes));
            pixels.Slice((bottom + row) * cpu.StrideBytes + left * 4, rowBytes)
                .CopyTo(result.AsSpan((stripHeight + row) * rowBytes));
        }
        return result;
    }

    private void VerifyLoginComposition()
    {
        var client = Window.ClientSize;
        foreach (var element in new Aprillz.MewUI.Controls.FrameworkElement[] { loginBrand, loginContent, loginButton })
        {
            var bounds = element.Bounds;
            if (bounds.X < 0 || bounds.Y < 0 || bounds.Right > client.Width || bounds.Bottom > client.Height)
                throw new InvalidOperationException("Login branding or action was clipped by the viewport");
        }
        if (Math.Abs(loginScreen.ActualWidth - client.Width) > 1 ||
            Math.Abs(loginScreen.ActualHeight - client.Height) > 1)
            throw new InvalidOperationException("Login backdrop did not fill the viewport");
        if (Math.Abs(loginContent.Bounds.X + loginContent.ActualWidth / 2 - client.Width / 2) > 1 ||
            Math.Abs(loginContent.Bounds.Y + loginContent.ActualHeight / 2 - client.Height / 2) > 1 ||
            Math.Abs(loginBrand.Bounds.X + loginBrand.ActualWidth / 2 - client.Width / 2) > 1)
            throw new InvalidOperationException("Login form or branding was not centered");
        var labelCenter = loginButtonLabel.Bounds.X + loginButtonLabel.ActualWidth / 2;
        var buttonCenter = loginButton.Bounds.X + loginButton.ActualWidth / 2;
        if (Math.Abs(labelCenter - buttonCenter) > 1)
            throw new InvalidOperationException("Login label was not centered inside the button");
    }
}
