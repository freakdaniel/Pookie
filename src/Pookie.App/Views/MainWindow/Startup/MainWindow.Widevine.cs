using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.Audio;
using Pookie.Logging;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private const int WidevineActionWidth = 220, WidevineActionGap = 12;
    private const int WidevineLicenseWidth = WidevineActionWidth * 2 + WidevineActionGap;
    private ModalDialog widevineDialog = null!;
    private DescriptionTextBlock widevineLicense = null!;
    private DescriptionLinks widevineLicenseLinks = null!;
    private TextBlock widevineConsentError = null!, widevineStartupError = null!;
    private StackPanel widevineStartupFailure = null!;
    private Button widevineStartupRetry = null!;
    private TaskCompletionSource? widevineRetryAction;
    private ScrollViewer widevineLicenseScroll = null!;
    private Button widevineContinue = null!, widevineExit = null!;
    private WidevineBrand widevineBrand = null!;
    private TextBlock widevineHeading = null!;
    private TextBlock widevineContinueLabel = null!;
    private TaskCompletionSource? widevineAction;
    private bool widevineBusy;

    private FrameworkElement WidevineSetupView()
    {
        if (widevineSetup == null) return new Grid().IsVisible(false);
        widevineContinueLabel = new TextBlock().Text("Принять и продолжить").FontSize(14).SemiBold().Foreground(Color.White);
        widevineContinue = TrackButtons.Action(widevineContinueLabel,
            () => { if (!widevineBusy) widevineAction?.TrySetResult(); },
            TrackButtons.Glass, 44).Width(WidevineActionWidth);
        ((MaterialTrackButton)widevineContinue).GlassAccent(TrackButtons.BlueGlassAccent, Color.White);
        ((MaterialTrackButton)widevineContinue).UseGlassAccentStateLayer();
        widevineExit = TrackButtons.Action(new TextBlock().Text("Выйти").FontSize(14).SemiBold().Foreground(Color.White),
            () => Window.Close(), TrackButtons.Glass, 44).Width(WidevineActionWidth);
        ((MaterialTrackButton)widevineExit).UseLightGlassFinish();
        widevineConsentError = new TextBlock().FontSize(13).Foreground(Color.FromRgb(255, 177, 193)).TextWrapping(TextWrapping.Wrap).IsVisible(false);
        widevineLicense = new DescriptionTextBlock { Text = "", FontSize = 14, Foreground = TrackButtons.OnSurface };
        widevineLicenseLinks = new DescriptionLinks(widevineLicense, uri => OpenExternalUrl(uri.AbsoluteUri, () =>
        {
            widevineConsentError.Text = "Не удалось открыть ссылку в системном браузере.";
            widevineConsentError.IsVisible = true;
        }), showIcons: false);
        widevineLicenseScroll = new ScrollViewer().Height(250).Padding(18).Background(Color.FromArgb(100, 10, 10, 14)).BorderThickness(0)
            .Content(widevineLicense);
        widevineBrand = new WidevineBrand().Left();
        widevineHeading = new TextBlock().Text("Защищённое\nвоспроизведение").FontSize(32).Foreground(Color.White).TextWrapping(TextWrapping.Wrap);
        var introduction = new StackPanel().Vertical().Spacing(24).Children(
            widevineBrand,
            widevineHeading,
            new TextBlock().Text("Для воспроизведения контента защищенного авторским правом необходим DRM модуль. Ознакомьтесь и примите условия поставщика чтобы продолжить")
                .FontSize(15).Foreground(Muted).TextWrapping(TextWrapping.Wrap), widevineConsentError);
        var buttons = new StackPanel().Horizontal().Spacing(WidevineActionGap).Children(widevineExit, widevineContinue);
        var licenseColumn = new StackPanel().Vertical().Spacing(28).Children(widevineLicenseScroll, buttons);
        // Preserve the license/action width; only the introduction flexes on resize.
        var body = new Grid().Columns($"*,{WidevineLicenseWidth}").Rows("Auto").Spacing(48).Children(
            introduction.Column(0), licenseColumn.Column(1));
        var card = new GlassCard { HoverEnabled = false }.CornerRadius(28).Padding(40).MaxWidth(1040).Child(body);
        widevineDialog = new ModalDialog(card, widevineContinue, staticBackdrop: true, cacheTransition: true,
            backdropTint: Color.FromArgb(252, 24, 24, 28))
        {
            DismissRequested = () => { }
        };
        return widevineDialog;
    }

    private FrameworkElement WidevineStartupFailureView()
    {
        if (widevineSetup == null) return new Grid().IsVisible(false);
        widevineStartupError = new TextBlock().FontSize(14).Foreground(Muted).TextWrapping(TextWrapping.Wrap).TextAlignment(TextAlignment.Center);
        widevineStartupRetry = TrackButtons.Action(new TextBlock().Text("Повторить загрузку").FontSize(14).SemiBold(),
            () => widevineRetryAction?.TrySetResult(), TrackButtons.Glass, 44).Width(220).CenterHorizontal();
        widevineStartupFailure = new StackPanel().Vertical().Spacing(16).MaxWidth(480).CenterHorizontal().IsVisible(false)
            .Children(widevineStartupError, widevineStartupRetry);
        return widevineStartupFailure;
    }

    private async Task ShowWidevineSetupAsync()
    {
        await startupMinimumDisplay.WaitAsync(lifetime.Token);
        // Keep the loader in place. Dense matte glass obscures it without a
        // second fade-out/fade-in sequence or repeated backdrop captures.
        widevineDialog.Show();
    }

    private async Task ResumeWidevineStartupAsync()
    {
        if (widevineDialog.IsVisible)
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnClosed() => closed.TrySetResult();
            widevineDialog.Closed += OnClosed;
            try
            {
                widevineDialog.Close();
                await closed.Task.WaitAsync(lifetime.Token);
            }
            finally { widevineDialog.Closed -= OnClosed; }
            // Retain a readable loading frame after the card closes, without
            // animating the logo itself a second time.
            startupMinimumDisplay = HoldVisibleStartupAsync();
        }
    }

    private void RefreshWidevineAction()
    {
        widevineContinue.IsEnabled = !widevineBusy;
    }

    private async Task PrepareWidevineAtStartupAsync()
    {
        var setup = widevineSetup!;
        var token = lifetime.Token;
        while (true)
        {
            try { await setup.ResolveAsync(token); break; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                await RetryWidevineStartupAsync(error, "Не удалось получить актуальные условия Widevine. Проверь подключение к интернету и повтори попытку.");
            }
        }
        // Reflow vendor line wraps without changing words or paragraph boundaries.
        widevineLicense.Text = string.Join("\n\n", setup.LicenseText.Replace("\r\n", "\n").Trim().Split("\n\n")
            .Select(paragraph => paragraph.Replace('\n', ' ')));
        widevineDialog.InvalidateTransitionSnapshot();
        while (!setup.IsAccepted)
        {
            widevineAction = new(TaskCreationOptions.RunContinuationsAsynchronously);
            widevineBusy = false;
            RefreshWidevineAction();
            await ShowWidevineSetupAsync();
            await widevineAction.Task.WaitAsync(token);
            widevineBusy = true;
            RefreshWidevineAction();
            widevineConsentError.IsVisible = false;
            try { await setup.AcceptAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                AppLog.Failure("Pookie.UI", "Не удалось сохранить принятие условий Widevine", error);
                widevineConsentError.Text = "Не удалось сохранить принятие условий. Проверь доступ к папке данных Pookie и повтори попытку.";
                widevineConsentError.IsVisible = true;
            }
        }
        await ResumeWidevineStartupAsync();
        while (true)
        {
            try
            {
                await setup.PrepareAsync(token);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                await RetryWidevineStartupAsync(error, "Не удалось загрузить или проверить Widevine. Проверь подключение к интернету и повтори попытку.");
            }
        }
    }

    private async Task RetryWidevineStartupAsync(Exception error, string message)
    {
        AppLog.Failure("Pookie.UI", "Не удалось подготовить Widevine при запуске", error);
        widevineRetryAction = new(TaskCreationOptions.RunContinuationsAsynchronously);
        widevineStartupError.Text = error is DrmPlaybackException ? error.Message : message;
        startupSpinner.IsActive = false;
        widevineStartupFailure.IsVisible = true;
        widevineStartupRetry.Focus();
        await widevineRetryAction.Task.WaitAsync(lifetime.Token);
        widevineStartupFailure.IsVisible = false;
        startupSpinner.IsActive = true;
    }
}
