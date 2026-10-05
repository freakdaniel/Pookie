using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private enum LoginButtonState { Idle, Waiting, Connecting }
    private readonly string brandFontFamily;
    private readonly ObservableValue<bool> loginBusy = new(false);
    private readonly ObservableValue<Color> loginButtonColor = new(LoginOrange);
    private Grid workspace = null!;
    private Border loginScreen = null!;
    private StackPanel loginContent = null!;
    private Button loginButton = null!;
    private FrameworkElement loginBrand = null!;
    private Grid loginBackdrop = null!;
    private TransitionContentControl loginLabelHost = null!;
    private TextBlock loginButtonLabel = null!;
    private ProgressRing loginSpinner = null!;
    private PathShape loginButtonGlyph = null!;
    private LoginButtonState loginButtonState;
    private static readonly Color LoginBackground = Color.FromRgb(24, 24, 24);
    private static readonly Color LoginOrange = Color.FromRgb(255, 85, 0);
    private static readonly Color LoginPaper = Color.FromRgb(239, 239, 239);

    private bool CanUseWorkspace => signedIn.Value || (demo && !loginUiSmoke);

    private FrameworkElement LoginScreen()
    {
        loginButtonGlyph = new PathShape().Width(18).Height(18).Stroke(LoginPaper, 2)
            .CenterHorizontal().CenterVertical();
        loginButtonGlyph.Transitions = [Transition.Create(UIElement.OpacityProperty, 180, Easing.CubicBezier(0.2, 0, 0, 1))];
        loginSpinner = new ProgressRing().Width(22).Height(22).IsActive(false).CenterHorizontal().CenterVertical();
        loginSpinner.Opacity = 0;
        loginSpinner.Transitions = [Transition.Create(UIElement.OpacityProperty, 180, Easing.CubicBezier(0.2, 0, 0, 1))];
        loginLabelHost = new TransitionContentControl
        {
            Transition = ContentTransition.CreateSlide(SlideDirection.Up, durationMs: 220)
        };
        var labelViewport = new Border().Height(22).CenterVertical().ClipToBounds().Child(loginLabelHost);
        loginButton = new Button().Width(352).Height(58).Padding(16, 10).CornerRadius(18)
            .Bind(Control.BackgroundProperty, loginButtonColor).Foreground(LoginPaper).BorderThickness(0)
            .BindIsEnabled(loginBusy, busy => !busy)
            .Content(new Grid().Columns("36,*,36").Rows("*").Spacing(6).Children(
                Icons.SoundCloudMark(36).CenterVertical().Column(0),
                labelViewport.Column(1),
                new Grid().Columns("*").Rows("*").Column(2).Children(loginButtonGlyph, loginSpinner)))
            .OnMouseEnter(() => { if (!loginBusy.Value) loginButtonColor.Value = Color.FromRgb(255, 105, 30); })
            .OnMouseLeave(() => { if (!loginBusy.Value) loginButtonColor.Value = LoginOrange; })
            .OnClick(() => Run(LoginAsync));
        loginButton.Transitions = [Transition.Create(Control.BackgroundProperty, 200, Easing.CubicBezier(0.2, 0, 0, 1))];
        SetLoginButtonState(LoginButtonState.Idle);
        loginBusy.Changed += RefreshLoginButtonActivity;

        loginBrand = new StackPanel().Horizontal().Spacing(16).CenterHorizontal().Children(
            Icons.LogoMark(58, 56).Fill(LoginPaper).CenterVertical(),
            new TextBlock().Text("Pookie").FontFamily(brandFontFamily).FontSize(44).Foreground(LoginPaper).CenterVertical());
        loginContent = new StackPanel().Vertical().Spacing(36).Width(400).CenterHorizontal().CenterVertical().Children(
            loginBrand,
            new StackPanel().Vertical().Spacing(14).Children(
                new TextBlock().Text("Добро пожаловать").FontSize(34).Bold().Foreground(LoginPaper)
                    .TextAlignment(TextAlignment.Center).TextWrapping(TextWrapping.Wrap),
                new TextBlock().Text("Для использования Pookie необходима учётная запись SoundCloud. Выполните вход с помощью кнопки ниже.")
                    .FontSize(15).Foreground(Color.FromRgb(195, 195, 195))
                    .TextAlignment(TextAlignment.Center).TextWrapping(TextWrapping.Wrap)),
            loginButton.CenterHorizontal());
        loginBackdrop = new Grid().Columns("*").Rows("*").IsHitTestVisible(false).Children(
            new LoginBackdropShade().IsHitTestVisible(false));
        var composition = new Grid().Columns("*").Rows("*").Children(loginBackdrop, loginContent);
        loginScreen = new Border().Background(LoginBackground).ClipToBounds().Child(composition);
        loginScreen.IsVisible = false;
        loginScreen.IsHitTestVisible = false;
        loginScreen.Opacity = 0;
        return loginScreen;
    }

    private void SetLoginButtonState(LoginButtonState state)
    {
        if (loginButtonLabel != null && loginButtonState == state) return;
        loginButtonState = state;
        var label = state switch
        {
            LoginButtonState.Waiting => "Ожидание входа…",
            LoginButtonState.Connecting => "Вхожу…",
            _ => "Войти в SoundCloud"
        };
        loginButtonLabel = new TextBlock().Text(label).FontSize(15).SemiBold().Foreground(LoginPaper)
            .CenterHorizontal().CenterVertical();
        loginLabelHost.Content = loginButtonLabel;
        loginButtonGlyph.Data("M 2 16 L 16 2 M 4 2 L 16 2 L 16 14");
        loginButtonColor.Value = state != LoginButtonState.Idle ? Color.FromRgb(230, 76, 0) : LoginOrange;
        RefreshLoginButtonActivity();
    }

    private void RefreshLoginButtonActivity()
    {
        var waiting = loginButtonState != LoginButtonState.Idle;
        loginSpinner.Opacity = waiting ? 1 : 0;
        loginSpinner.IsActive = waiting && loginBusy.Value;
        loginButtonGlyph.Opacity = waiting ? 0 : 1;
    }

    private async Task ShowLoginScreenAsync()
    {
        if (disposed) return;
        workspace.IsEnabled = false;
        workspace.IsHitTestVisible = false;
        if (loginScreen.IsVisible && loginScreen.Opacity >= 1 && !startupSplash.IsVisible && !workspace.IsVisible) return;
        var fromStartup = startupSplash.IsVisible;
        if (fromStartup) await startupMinimumDisplay;
        SetLoginButtonState(LoginButtonState.Idle);
        loginScreen.IsVisible = true;
        loginScreen.IsHitTestVisible = false;
        loginContent.Opacity = loginBrand.Opacity = 0;
        // Fade only the upper screen; the lower background must stay opaque throughout the handoff.
        loginScreen.Opacity = fromStartup ? 1 : 0;
        loginBackdrop.Opacity = 1;
        if (fromStartup)
        {
            await AnimateStartupAsync(StartupBrandFadeDurationMs, Easing.CubicBezier(0.2, 0, 0, 1),
                progress => startupBrand.Opacity = 1 - progress);
            startupBrandLayer.IsVisible = false;
            startupSpinner.IsActive = false;
        }
        await AnimateStartupAsync(720, Easing.CubicBezier(0.16, 1, 0.3, 1), progress =>
        {
            if (!fromStartup) loginScreen.Opacity = progress;
            loginBrand.Opacity = Math.Clamp(progress * 1.5, 0, 1);
            var contentProgress = Math.Clamp((progress - 0.18) / 0.82, 0, 1);
            loginContent.Opacity = contentProgress;
            loginContent.Margin = new Thickness(0, 30 * (1 - contentProgress), 0, 0);
            if (fromStartup) startupSplash.Opacity = 1 - progress;
        });
        startupSplash.IsVisible = false;
        workspace.IsVisible = false;
        workspace.Opacity = 1;
        loginScreen.IsHitTestVisible = true;
        loginButton.Focus();
    }

    private async Task ShowWorkspaceAsync()
    {
        if (disposed || !CanUseWorkspace) return;
        workspace.IsVisible = true;
        workspace.Opacity = 1;
        startupHeader.Opacity = startupContent.Opacity = 1;
        startupHeader.IsHitTestVisible = startupContent.IsHitTestVisible = true;
        startupBackdrop.Background = HeaderSurface;
        loginScreen.IsHitTestVisible = false;
        await AnimateStartupAsync(420, Easing.CubicBezier(0.2, 0, 0, 1), progress =>
        {
            loginScreen.Opacity = 1 - progress;
        });
        loginScreen.IsVisible = false;
        loginSpinner.IsActive = false;
        workspace.IsEnabled = true;
        workspace.IsHitTestVisible = true;
    }

    private async Task LoginAsync()
    {
        if (disposed || loginBusy.Value || signedIn.Value) return;
        loginBusy.Value = true;
        SetLoginButtonState(LoginButtonState.Waiting);
        try { await ConnectSoundCloudAsync(); }
        finally
        {
            loginBusy.Value = false;
            if (!disposed && loginScreen.IsVisible)
            {
                if (!signedIn.Value) SetLoginButtonState(LoginButtonState.Idle);
                loginButton.Focus();
            }
        }
    }
}
