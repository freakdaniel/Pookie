using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.Audio;
using Pookie.App.Diagnostics;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    internal void VerifyWidevineConsentUi(WidevineConsent consent, Func<int> calls, TaskCompletionSource<string> ready, Func<bool> preparationBusy) => Run(async () =>
    {
        var perf = Environment.GetEnvironmentVariable("POOKIE_WIDEVINE_PERF") == "1";
        var openingIntervals = new List<double>();
        var hoverIntervals = new List<double>();
        var spinnerIntervals = new List<double>();
        var beforeCardIntervals = new List<double>();
        var afterCardIntervals = new List<double>();
        var busySpinnerPhases = new HashSet<double>();
        var lastSpinnerFrame = 0L;
        var hovering = false;
        var lastPaint = 0L;
        var previousStage = "";
        Rect? lateLicenseBounds = null;
        string? transitionFailure = null;
        var capturedTransition = false;
        var initialLoaderSeen = false;
        var continuePressed = false;
        void ObserveTransition()
        {
            var stage = hovering ? "hover" : widevineDialog.IsOpen && widevineDialog.Running ? "opening" : "";
            var now = Stopwatch.GetTimestamp();
            if (startupSpinner.Running && startupBrand.Opacity == 1)
            {
                if (perf && lastSpinnerFrame != 0)
                {
                    var gap = Stopwatch.GetElapsedTime(lastSpinnerFrame, now).TotalMilliseconds;
                    spinnerIntervals.Add(gap);
                    if (!continuePressed && (!widevineDialog.IsVisible || widevineDialog.Running)) beforeCardIntervals.Add(gap);
                    if (continuePressed && (!widevineDialog.IsVisible || !widevineDialog.IsOpen) && !widevineStartupFailure.IsVisible) afterCardIntervals.Add(gap);
                }
                lastSpinnerFrame = now;
                if (preparationBusy()) busySpinnerPhases.Add(startupSpinner.Phase);
            }
            else lastSpinnerFrame = 0;
            if (perf && stage.Length != 0)
            {
                var intervals = hovering ? hoverIntervals : openingIntervals;
                if (lastPaint != 0 && previousStage == stage) intervals.Add(Stopwatch.GetElapsedTime(lastPaint, now).TotalMilliseconds);
            }
            previousStage = stage; lastPaint = now;
            if (widevineDialog.IsVisible && (!startupBrandLayer.IsVisible || startupBrand.Opacity != 1 || !startupSpinner.IsActive))
                transitionFailure ??= "Loader was hidden or faded while the matte consent card was visible.";
            if (!continuePressed && !widevineDialog.IsVisible && startupBrandLayer.IsVisible)
            {
                if (!initialLoaderSeen && startupLogo.Opacity == 1 && startupBrand.Opacity == 1 && startupSpinner.Running)
                {
                    initialLoaderSeen = true;
                    CaptureUiPreview("widevine-startup-initial");
                }
            }
            if (widevineBrand.Opacity != 1) transitionFailure ??= "Widevine logo animated independently of the card.";
            if (widevineDialog.IsOpen && widevineDialog.Running && widevineDialog.Pose > .97 && widevineDialog.HasSnapshots)
            {
                lateLicenseBounds = widevineLicense.Bounds;
                if (!capturedTransition) { capturedTransition = true; CaptureUiPreview("widevine-opening-final-frame"); }
            }
        }
        Window.FrameRendered += ObserveTransition;
        try
        {
            Window.Activate();
            Window.WindowSize = WindowSize.Resizable(1000, 680, minWidth: 1000, minHeight: 680);
            await WaitWidevineAsync(() => widevineStartupFailure.IsVisible && widevineStartupRetry.ActualWidth > 0);
            if (widevineDialog.IsVisible || widevineLicense.Text.Length != 0 || calls() != 0 || consent.IsAccepted(WidevineConsentUiSmokeTest.LicenseText))
                throw new InvalidOperationException("Failed license fetch showed stale terms or bypassed consent.");
            RouteWaveformClick(SettingsCenter(widevineStartupRetry));
            await WaitWidevineAsync(() => widevineDialog.IsOpen && !widevineDialog.Running);
            if (transitionFailure != null) throw new InvalidOperationException(transitionFailure);
            if (!widevineDialog.UsesCachedTransition || widevineDialog.SnapshotCaptures is < 1 or > 2)
                throw new InvalidOperationException($"Standard card animation did not reuse its capture: {widevineDialog.SnapshotCaptures} captures.");
            if (lateLicenseBounds is { } before && before != widevineLicense.Bounds)
                throw new InvalidOperationException("License layout moved during the final animation handoff.");
            if (widevineBrand.Bounds.Bottom > widevineHeading.Bounds.Y || widevineLicenseScroll.Bounds.X < widevineHeading.Bounds.Right)
                throw new InvalidOperationException("Branded two-column layout or its reserved reveal slot was lost.");
            if (widevineExit.Bounds.Size != widevineContinue.Bounds.Size || widevineExit.CornerRadius != widevineContinue.CornerRadius)
                throw new InvalidOperationException("Dialog action backgrounds have different sizes.");
            if (widevineContinueLabel.Foreground != Color.White || ((MaterialTrackButton)widevineContinue).GlassAccentSurface is not { } accent || accent.B <= accent.R || accent.A == 255)
                throw new InvalidOperationException("Primary action lost its blue glass tint or white label.");
            var surface = widevineDialog.SurfaceBounds;
            if (surface.X < 15 || surface.Right > Window.ClientSize.Width - 15 || surface.Y < 15 || surface.Bottom > Window.ClientSize.Height - 15)
                throw new InvalidOperationException("Consent card exceeds the minimum-size viewport.");
            if (consent.IsAccepted(WidevineConsentUiSmokeTest.LicenseText) || calls() != 0 || !widevineContinue.IsEnabled) throw new InvalidOperationException("Consent was assumed or continue requires an extra action.");
            if (Math.Abs(widevineContinue.Bounds.Bottom - (surface.Bottom - 40)) > 1)
                throw new InvalidOperationException("License footer retained extra space for module progress.");
            await WaitWidevineAsync(() => widevineDialog.HasGlassBackdrop);
            if (!initialLoaderSeen || !startupBrandLayer.IsVisible || startupBrand.Opacity != 1 || !startupSpinner.IsActive)
                throw new InvalidOperationException("Initial loader was not preserved behind the matte consent card.");
            void VerifyLicenseColumn()
            {
                var license = widevineLicenseScroll.Bounds;
                if (Math.Abs(license.Width - WidevineLicenseWidth) > 1 || Math.Abs(widevineExit.Bounds.X - license.X) > 1 ||
                    Math.Abs(widevineContinue.Bounds.Right - license.Right) > 1)
                    throw new InvalidOperationException("Fixed-width license column and action edges no longer align.");
                if (Math.Abs(license.Y - widevineBrand.Bounds.Y) > 1)
                    throw new InvalidOperationException("License retained unnecessary top spacing.");
            }
            VerifyLicenseColumn();
            var introduction = (FrameworkElement)widevineBrand.Parent!;
            var narrowWidth = introduction.ActualWidth;
            Window.WindowSize = WindowSize.Resizable(1280, 840, minWidth: 1000, minHeight: 680);
            await WaitWidevineAsync(() => introduction.ActualWidth > narrowWidth + 20 && widevineDialog.HasGlassBackdrop);
            VerifyLicenseColumn();
            Window.WindowSize = WindowSize.Resizable(1000, 680, minWidth: 1000, minHeight: 680);
            await WaitWidevineAsync(() => Math.Abs(introduction.ActualWidth - narrowWidth) < 1 && widevineDialog.HasGlassBackdrop);
            VerifyLicenseColumn();
            if (perf)
            {
                var backdropVersion = widevineDialog.BackdropVersion;
                var rimBuilds = ((MaterialTrackButton)widevineContinue).AccentRasterizations;
                foreach (var button in new[] { widevineExit, widevineContinue, widevineExit, widevineContinue })
                {
                    hovering = true; previousStage = ""; lastPaint = 0;
                    MoveActionPointer(SettingsCenter(button));
                    await Task.Delay(320, lifetime.Token);
                    hovering = false;
                }
                MoveActionPointer(new Point(8, 8));
                if (widevineDialog.BackdropVersion != backdropVersion || ((MaterialTrackButton)widevineContinue).AccentRasterizations != rimBuilds)
                    throw new InvalidOperationException("Hover rebuilt the static backdrop or blue glass rim.");
            }
            RouteSettingsKey(Key.Escape);
            RouteWaveformClick(new Point(8, 8));
            await Task.Delay(150);
            if (!widevineDialog.IsOpen || calls() != 0 || loginScreen.IsVisible || workspace.IsEnabled) throw new InvalidOperationException("Mandatory consent could be bypassed.");
            CaptureUiPreview("widevine-consent-1000");
            var opened = new List<Uri>();
            widevineLicenseLinks.OpenLink = opened.Add;
            static string Words(string value) => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (Words(widevineLicense.Text) != Words(WidevineConsentUiSmokeTest.LicenseText) || widevineLicenseLinks.Inlines.Length != 0)
                throw new InvalidOperationException("Link decoration changed the displayed vendor license.");
            var link = widevineLicenseLinks.Links.Single();
            if (link.Uri.AbsoluteUri != "https://www.widevine.com/") throw new InvalidOperationException("Vendor URL was not normalized to HTTPS.");
            widevineLicenseScroll.SetScrollOffsets(0, SmoothScroll.Maximum(widevineLicenseScroll));
            await WaitForLoginFrameAsync();
            var linkBounds = widevineLicenseLinks.BoundsFor(link).Last();
            var linkPoint = new Point(linkBounds.X + Math.Min(20, linkBounds.Width / 2), linkBounds.Y + linkBounds.Height / 2);
            MoveActionPointer(linkPoint);
            await WaitWidevineAsync(() => widevineLicense.Cursor == CursorType.Hand);
            RouteWaveformClick(linkPoint);
            if (opened.Count != 1 || opened[0] != link.Uri || consent.IsAccepted(WidevineConsentUiSmokeTest.LicenseText) || calls() != 0 || !widevineDialog.IsOpen)
                throw new InvalidOperationException("License link did not activate independently of acceptance.");
            CaptureUiPreview("widevine-license-link");
            continuePressed = true;
            RouteWaveformClick(SettingsCenter(widevineContinue));
            await WaitWidevineAsync(() => calls() == 1 && widevineStartupFailure.IsVisible && widevineStartupRetry.ActualWidth > 0);
            if (!consent.IsAccepted(WidevineConsentUiSmokeTest.LicenseText) || widevineDialog.IsVisible || loginScreen.IsVisible || !startupBrandLayer.IsVisible || startupSpinner.IsActive)
                throw new InvalidOperationException("Failed download reopened the consent card or lost acceptance.");
            CaptureUiPreview("widevine-download-retry");
            Window.WindowSize = WindowSize.Resizable(1280, 840, minWidth: 1000, minHeight: 680);
            await Task.Delay(200);
            RouteWaveformClick(SettingsCenter(widevineStartupRetry));
            await WaitWidevineAsync(() => calls() == 2);
            await WaitWidevineAsync(() => !preparationBusy());
            if (busySpinnerPhases.Count < 3)
                throw new InvalidOperationException("Synchronous component initialization blocked the spinner.");
            if (widevineDialog.IsVisible || !startupBrandLayer.IsVisible || startupBrand.Opacity != 1 || !startupSpinner.Running || loginScreen.IsVisible)
                throw new InvalidOperationException("Pending installation did not return to the standalone loader.");
            if (transitionFailure != null) throw new InvalidOperationException(transitionFailure);
            CaptureUiPreview("widevine-downloading-1280");
            ready.TrySetResult("fixture-installed-library");
            await WaitWidevineAsync(() => loginScreen.IsVisible && !widevineDialog.IsOpen && !startupSpinner.IsActive);
            if (!new WidevineConsent(Path.GetDirectoryName(consent.FilePath)!).IsAccepted(WidevineConsentUiSmokeTest.LicenseText)) throw new InvalidOperationException("Acceptance did not survive reopening the store.");
            if (perf)
            {
                static double P95(List<double> values) => values.Count == 0 ? 0 : values.Order().ElementAt(Math.Min(values.Count - 1, (int)(values.Count * .95)));
                Console.WriteLine($"WIDEVINE_PERF opening: frames={openingIntervals.Count}, interval_p95={P95(openingIntervals):F1}ms");
                Console.WriteLine($"WIDEVINE_PERF hover: frames={hoverIntervals.Count}, interval_p95={P95(hoverIntervals):F1}ms");
                Console.WriteLine($"WIDEVINE_PERF spinner: frames={spinnerIntervals.Count}, interval_p95={P95(spinnerIntervals):F1}ms, max={spinnerIntervals.DefaultIfEmpty().Max():F1}ms, worker_busy_phases={busySpinnerPhases.Count}");
                Console.WriteLine($"WIDEVINE_PERF spinner boundaries: before_max={beforeCardIntervals.DefaultIfEmpty().Max():F1}ms, after_max={afterCardIntervals.DefaultIfEmpty().Max():F1}ms");
            }
            Console.WriteLine($"WIDEVINE_CONSENT_UI_OK: continuous loader behind dense matte glass, fixed right column/responsive left, aligned actions and license top edge, standard cached animation, static brand, license links, retry on the loader and responsive spinner during synchronous preparation");
        }
        catch (Exception error) { VerificationFailure = error; Console.Error.WriteLine(error); }
        finally { Window.FrameRendered -= ObserveTransition; Window.Close(); }
    });

    private async Task WaitWidevineAsync(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Window.PerformLayout();
            if (ready()) return;
            await Task.Delay(50, lifetime.Token);
        }
        throw new TimeoutException("Widevine consent UI did not reach the expected state.");
    }
}
