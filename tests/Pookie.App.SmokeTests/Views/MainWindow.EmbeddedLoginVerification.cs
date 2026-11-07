using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;
using Pookie.App.Browser;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    internal void StartEmbeddedLoginVerification(string root, string profile, Task providerSeen, Action continueProvider,
        Task authorizationRequested, Action continueAuthorization, Task formRequested, Action continueForm,
        Action navigateToProvider, Task? previewReleased = null) =>
        Run(async () =>
        {
            var openingFrames = 0; var capturedOpening = false; var capturedClosing = false;
            var closingFrames = 0; var revealFrames = 0;
            var lostClosingSurface = false; var pixelFadeFrames = 0; Exception? motionFailure = null;
            var expandingFrames = 0; var openingBrandFrames = 0; string? expansionFailure = null;
            var capturedExpansion = false; var capturedFade = false;
            var spinnerPhases = new HashSet<double>();
            void SampleMotion()
            {
                if (embeddedLoginDialog.IsOpen && embeddedLoginSurface is { } loadingSurface)
                {
                    var spinner = loadingSurface.Children.OfType<LoadingSpinner>().Single();
                    var logo = loadingSurface.Children.OfType<Aprillz.MewUI.Controls.Image>().Single();
                    if (embeddedLoginDialog.Pose is > .1 and < .9)
                    {
                        openingBrandFrames++;
                        if (loadingSurface.IntroProgress != 0 || spinner.IsActive || spinner.Opacity != 0 || logo.Opacity != 1)
                            expansionFailure ??= $"opening: intro={loadingSurface.IntroProgress}, active={spinner.IsActive}, opacity={spinner.Opacity}, logo={logo.Opacity}";
                    }
                    if (loadingSurface.IntroProgress is > 0 and < 1)
                    {
                        expandingFrames++; spinnerPhases.Add(spinner.Phase);
                        if (embeddedLoginDialog.Pose != 1 || loadingSurface.IntroProgress > .1 &&
                            (logo.Bounds.Center.Y >= loadingSurface.Bounds.Center.Y || spinner.Bounds.Center.Y <= loadingSurface.Bounds.Center.Y))
                            expansionFailure ??= $"expanding: pose={embeddedLoginDialog.Pose}, intro={loadingSurface.IntroProgress}, logo={logo.Bounds}, spinner={spinner.Bounds}, surface={loadingSurface.Bounds}";
                        if (!capturedExpansion && loadingSurface.IntroProgress is > .3 and < .8)
                        { capturedExpansion = true; CaptureUiPreview("embedded-brand-expanding"); }
                    }
                }
                if (embeddedLoginSurface?.PageReveal is > 0 and < 1)
                {
                    revealFrames++;
                    if (embeddedLoginSurface.PageReveal is > .1 and < .9 && embeddedLogin?.HasSnapshot == true)
                    {
                        try { VerifyEmbeddedFadePixels(); pixelFadeFrames++; }
                        catch (Exception error) { motionFailure ??= error; }
                        if (!capturedFade && embeddedLoginSurface.PageReveal is > .3 and < .7)
                        { capturedFade = true; CaptureUiPreview("embedded-page-reveal"); }
                    }
                }
                if (!embeddedLoginDialog.IsOpen && embeddedLoginDialog.Pose is > 0 and < 1)
                {
                    closingFrames++;
                    if (!embeddedLoginDialog.HasSnapshots || embeddedLoginContent.Content == null) lostClosingSurface = true;
                }
                if (embeddedLoginDialog.Pose is <= 0 or >= 1) return;
                if (embeddedLoginDialog.IsOpen) openingFrames++;
                if (embeddedLoginDialog.Pose is < .3 or > .8) return;
                if (embeddedLoginDialog.IsOpen && !capturedOpening) { capturedOpening = true; CaptureUiPreview("embedded-opening"); }
                if (!embeddedLoginDialog.IsOpen && !capturedClosing) { capturedClosing = true; CaptureUiPreview("embedded-closing"); }
            }
            Window.FrameRendered += SampleMotion;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(previewReleased == null ? 35 : 90));
                Window.Activate();
                while (startupSplash.IsVisible || !loginScreen.IsVisible) await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
                var signingIn = AuthorizeEmbeddedAsync(timeout.Token, root + "/authorize", profile);
                await authorizationRequested.WaitAsync(timeout.Token);
                while (embeddedLoginDialog.Running || embeddedLoginSurface?.IntroProgress != 1) await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
                if (!embeddedLoginDialog.IsOpen || embeddedLoginSurface is not { Loading: true, PageReveal: 0, NativeVisible: false })
                    throw new InvalidOperationException("The loading backdrop did not appear before the delayed authorization document.");
                CaptureUiPreview("embedded-loading");
                continueAuthorization();
                await formRequested.WaitAsync(timeout.Token);
                await WaitForLoginFrameAsync(); await WaitForLoginFrameAsync();
                if (embeddedLoginSurface is not { PageReveal: 0, NativeVisible: false } || embeddedLogin!.HasSnapshot)
                    throw new InvalidOperationException("The SPA shell was mistaken for a painted sign-in form.");
                continueForm();
                while (!embeddedLoginDialog.IsOpen || embeddedLoginDialog.Running || embeddedLoginSurface?.NativeVisible != true)
                {
                    await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
                }
                if (previewReleased != null) { Console.WriteLine("EMBEDDED_PREVIEW_READY"); await previewReleased.WaitAsync(timeout.Token); }
                if (!embeddedLoginDialog.IsOpen || !loginContent.IsVisible || workspace.IsVisible || embeddedLogin == null)
                    throw new InvalidOperationException("Login browser was not shown inside the main login screen.");
                await Task.Delay(250, timeout.Token);
                VerifyEmbeddedNativePixels();
                if (openingFrames < 3 || embeddedLoginDialog.Pose != 1)
                    throw new InvalidOperationException($"Login did not animate through the existing modal spring: frames={openingFrames}, snapshot={embeddedLogin.HasSnapshot}, pose={embeddedLoginDialog.Pose}.");
                if (revealFrames < 3 || embeddedLogin.HasSnapshot && pixelFadeFrames < 2 || embeddedLoginSurface!.Loading)
                    throw new InvalidOperationException("The ready authorization page skipped its spinner-to-browser crossfade.");
                if (!embeddedLogin.HasSnapshot)
                {
                    await embeddedLogin.CapturePresentedPageForVerificationAsync(Window, timeout.Token);
                    Console.WriteLine("EMBEDDED_PREVIEW_FALLBACK_OK: native page shown despite unavailable offscreen preview; onscreen page captured successfully");
                }
                if (motionFailure != null) throw motionFailure;
                if (openingBrandFrames < 3 || expandingFrames < 3 || spinnerPhases.Count < 3 || expansionFailure != null)
                    throw new InvalidOperationException($"The centered logo/vertical spinner sequence failed: brandFrames={openingBrandFrames}, expandingFrames={expandingFrames}, phases={spinnerPhases.Count}, failure={expansionFailure}.");
                var close = embeddedLoginDialog.Children.OfType<MaterialTrackButton>().Single();
                if (!close.HasOverlayBlur || KeyboardFocusFeedback.IsVisible(close))
                    throw new InvalidOperationException("A pointer-opened login close button lacks its glass backdrop or acquired a keyboard highlight.");
                if (Environment.GetEnvironmentVariable("POOKIE_UI_PREVIEW") is { Length: > 0 } preview)
                    File.WriteAllBytes(Path.Combine(preview, "embedded-native.png"), embeddedLogin.SnapshotPng.ToArray());
                CaptureUiPreview("embedded-settled");
                navigateToProvider();
                await providerSeen.WaitAsync(timeout.Token);
                Window.WindowSize = WindowSize.Resizable(1050, 720, minWidth: 1000, minHeight: 680);
                // Native resize, ClientSizeChanged and layout arrive on separate
                // turns; do not inspect the old viewport after an arbitrary two frames.
                do { await WaitForLoginFrameAsync().WaitAsync(timeout.Token); }
                while (Window.ClientSize != new Size(1050, 720) ||
                    embeddedLoginContent.Height > Window.ClientSize.Height - 128 ||
                    embeddedLogin.ActualHeight > embeddedLoginContent.Height);
                var browserBounds = embeddedLogin.TranslateRect(new(0, 0, embeddedLogin.ActualWidth, embeddedLogin.ActualHeight), Window);
                var backdropBounds = embeddedLoginSurface.TranslateRect(new(0, 0, embeddedLoginSurface.ActualWidth, embeddedLoginSurface.ActualHeight), Window);
                if (Math.Abs(backdropBounds.Width - browserBounds.Width) > .01 || Math.Abs(backdropBounds.Height - browserBounds.Height) > .01 ||
                    Math.Abs(backdropBounds.X - browserBounds.X) > .01 || Math.Abs(backdropBounds.Y - browserBounds.Y) > .01)
                    throw new InvalidOperationException("The opaque backdrop does not match the browser viewport.");
                if (browserBounds.Width < 600 || browserBounds.Height < 350 || browserBounds.Y < 16 || browserBounds.Bottom > Window.ClientSize.Height - 16 ||
                    embeddedLoginDialog.SurfaceBounds.Width >= Window.ClientSize.Width - 16)
                    throw new InvalidOperationException($"Embedded browser escaped its login viewport after resize: browser={browserBounds}, surface={embeddedLoginDialog.SurfaceBounds}, client={Window.ClientSize}.");
                await Task.Delay(250, timeout.Token);
                VerifyEmbeddedNativePixels();
                continueProvider();
                var result = (WebSession)await signingIn;
                if (result.AccessToken != "embedded-token" || result.DataDomeClientId != "embedded-cookie" || embeddedLoginDialog.IsOpen || !loginContent.IsVisible)
                    throw new InvalidOperationException("Embedded OAuth lost its callback or did not restore the login screen.");
                if (closingFrames < 3 || lostClosingSurface || embeddedLoginDialog.IsVisible)
                    throw new InvalidOperationException("The browser surface was cleared before its modal exit animation completed.");
                using (BrowserProfile.Open(path: profile)) { /* The browser lease must be released before the API worker starts. */ }
                Console.WriteLine("EMBEDDED_OAUTH_OK: real in-window browser, provider CSP, resize, callback, cookie and profile handoff");
                VerifyEmbeddedPerspectiveSampling();
                Console.WriteLine($"EMBEDDED_TRANSITIONS_OK: centered logo during opening; upward expansion and animated spinner below; SPA controls and page reveal; opaque exit snapshot; snapshot fade pixel frames={pixelFadeFrames}");
                foreach (var loaded in new[] { false, true, false })
                {
                    using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    login = cancel;
                    var pending = RunLoginAttemptAsync(async () => await AuthorizeEmbeddedAsync(cancel.Token,
                        root + (loaded ? "/loaded" : "/wait"), profile));
                    while (!embeddedLoginDialog.IsOpen || (loaded && embeddedLoginSurface?.NativeVisible != true))
                    {
                        if (pending.IsCompleted) throw new InvalidOperationException("Login ended before its cancellation fixture was ready.");
                        await WaitForLoginFrameAsync().WaitAsync(timeout.Token);
                    }
                    if (!loaded && embeddedLoginSurface is not { PageReveal: 0, NativeVisible: false })
                        throw new InvalidOperationException("A pending document exposed the native view or dismissed its loading spinner early.");
                    if (loaded) { await Task.Delay(250, timeout.Token); VerifyEmbeddedNativePixels(); }
                    embeddedLoginDialog.DismissRequested!();
                    try { await pending; throw new InvalidOperationException("Embedded login ignored cancellation."); }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                    {
                        if (loginBusy.Value || !loginButton.IsEnabled || loginButtonState != LoginButtonState.Idle || loginSpinner.IsActive)
                            throw new InvalidOperationException("Cancelling an entry point left the login button waiting or disabled.");
                    }
                    login = null;
                }
                Console.WriteLine("EMBEDDED_CANCEL_OK: immediate, loaded and repeated cancellation restore an enabled idle login button");
                try
                {
                    await RunLoginAttemptAsync(async () => await AuthorizeEmbeddedAsync(timeout.Token, root + "/wait", profile,
                        preparationTimeout: TimeSpan.FromMilliseconds(250)));
                    throw new InvalidOperationException("A stalled login preparation never timed out.");
                }
                catch (InvalidOperationException error) when (error.Message.Contains("не загрузилась вовремя", StringComparison.Ordinal))
                {
                    if (loginBusy.Value || !loginButton.IsEnabled || embeddedLoginDialog.IsOpen || !loginError.IsVisible ||
                        !loginError.Text.Contains("не загрузилась вовремя", StringComparison.Ordinal))
                        throw new InvalidOperationException("Preparation timeout did not restore a retryable login with a visible error.");
                    using (BrowserProfile.Open(path: profile)) { }
                }
                Console.WriteLine("EMBEDDED_LOGIN_READINESS_OK: link-only controls without animation frames, bounded stalled-page wait, visible error and reusable profile");
                if (motionFailure != null) throw motionFailure;
                var popupLogin = AuthorizeEmbeddedAsync(timeout.Token, root + "/web", profile);
                await embeddedLogin!.OpenFixturePopupForVerificationAsync(timeout.Token);
                var session = (WebSession)await popupLogin;
                if (session.ClientId != "embeddedid" || session.AccessToken != "embedded-token")
                    throw new InvalidOperationException("Embedded popup did not return its website session.");
                Console.WriteLine("EMBEDDED_POPUP_OK: popup shares profile and opener, closes back into the same login panel; session returned");
            }
            catch (Exception error) { VerificationFailure = error; Environment.ExitCode = 1; Console.Error.WriteLine(error); }
            finally { Window.FrameRendered -= SampleMotion; continueAuthorization(); continueForm(); navigateToProvider(); continueProvider(); Window.Close(); }
        });

    private void VerifyEmbeddedPerspectiveSampling()
    {
        var graphics = Window.GraphicsFactory;
        using var scope = graphics.AcquireBackgroundRenderScope();
        var bounds = new Rect(100, 60, 400, 360);
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2 })
        {
            var width = (int)Math.Ceiling(400 * dpi); var height = (int)Math.Ceiling(360 * dpi);
            var pixels = new byte[width * height * 4];
            for (var offset = 0; offset < pixels.Length; offset += 4)
            { pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 120; pixels[offset + 3] = 255; }
            var source = ImageSource.FromBgraPixels(width, height, pixels, hasAlpha: false);
            using var image = ((IImageSource)source).CreateImage(graphics);
            foreach (var pose in new[] { 0d, .15, .45, .8, .97 })
            {
                using var target = graphics.CreateSurface(RenderSurfaceDescriptor.CpuPixels((int)(600 * dpi), (int)(500 * dpi), dpi));
                using var context = graphics.CreateContext(target);
                context.BeginFrame(target); context.Clear(Color.Black); context.BeginOpacity(.65);
                ModalDialog.DrawPerspective(context, image, bounds, bounds.Center, pose, new(0, 0, 600, 500));
                context.EndOpacity(); context.EndFrame();
                var cpu = (ICpuPixelSurface)target;
                var data = cpu.GetReadOnlyPixelSpan();
                var scale = .8 + .2 * pose; var angle = -Math.PI / 9 * (1 - pose);
                double Y(double y) => bounds.Center.Y + scale * (y - bounds.Center.Y) * Math.Cos(angle) /
                    (1 - scale * (y - bounds.Center.Y) * Math.Sin(angle) / 500);
                for (var y = (int)Math.Ceiling(Y(bounds.Y) * dpi) + 4; y < (int)Math.Floor(Y(bounds.Bottom) * dpi) - 4; y++)
                {
                    var value = data[y * cpu.StrideBytes + (int)(bounds.Center.X * dpi) * 4 + 1];
                    if (Math.Abs(value - 78) > 2)
                        throw new InvalidOperationException($"Perspective strips contain a gap or overlapping fade: dpi={dpi}, pose={pose}, row={y}, value={value}.");
                }
            }
        }
        Console.WriteLine("EMBEDDED_PERSPECTIVE_OK: every interior scanline remains opaque and evenly faded at five poses and four DPI scales");
    }

    private void VerifyEmbeddedFadePixels()
    {
        var surface = embeddedLoginSurface!;
        if (surface.NativeVisible) throw new InvalidOperationException("The native browser bypassed the snapshot fade.");
        var dpi = Window.DpiScale;
        var graphics = Window.GraphicsFactory;
        using var scope = graphics.AcquireBackgroundRenderScope();
        using var target = graphics.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(
            (int)Math.Ceiling(Window.ClientSize.Width * dpi), (int)Math.Ceiling(Window.ClientSize.Height * dpi), dpi));
        using var context = graphics.CreateContext(target);
        context.BeginFrame(target); context.Clear(Color.Black); surface.Render(context); context.EndFrame();
        var cpu = (ICpuPixelSurface)target;
        var offset = (int)((surface.Bounds.Bottom - 40) * dpi) * cpu.StrideBytes + (int)((surface.Bounds.Right - 40) * dpi) * 4;
        var pixel = cpu.GetReadOnlyPixelSpan().Slice(offset, 4);
        var expectedBlue = 19 + (187 - 19) * surface.PageReveal;
        if (Math.Abs(pixel[0] - expectedBlue) > 3 || pixel[3] != 255)
            throw new InvalidOperationException($"Browser pixels skipped the crossfade: reveal={surface.PageReveal}, blue={pixel[0]}, expected={expectedBlue}.");
    }

    private void VerifyEmbeddedNativePixels()
    {
        if (!OperatingSystem.IsLinux()) return;
        VerifyEmbeddedNativeGeometry();
        var display = XOpenDisplay(0);
        if (display == 0) throw new InvalidOperationException("No X11 display for embedded browser visibility verification.");
        try
        {
            var bounds = embeddedLogin!.TranslateRect(new(0, 0, embeddedLogin.ActualWidth, embeddedLogin.ActualHeight), Window);
            var point = new Point(bounds.Right - 40, bounds.Bottom - 40);
            var pixel = ReadNativeChatVideoPixel(display, point);
            if (pixel != 0x3355bb)
                throw new InvalidOperationException($"The live browser is covered or misplaced in the actual X11 window: pixel=#{pixel:x6}, expected=#3355bb, bounds={bounds}.");
            Console.WriteLine("EMBEDDED_NATIVE_PIXELS_OK: actual application window displays the live page before closing");
        }
        finally { XCloseDisplay(display); }
    }
}
