using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

// Keep the framework's popup placement, focus ownership and outside-click policy.
// Only the trigger, option rows and reveal are owned by the app.
internal sealed class MaterialSelect : DropDownBase
{
    private readonly string[] options;
    private readonly AnimationClock stateMotion = new(TimeSpan.FromMilliseconds(150), Easing.CubicBezier(.2, 0, 0, 1));
    private MaterialSelectMenu? menu;
    private int selectedIndex = -1;
    private ITextLayout? label;
    private (string Text, uint Dpi, TextRunStyle Style, double Width) labelKey;
    private Color stateFrom, stateTarget = TrackButtons.TonalSurface;
    private bool keyboardInteraction, headerHovered;
    private Window? pendingPopupWindow;
    private int popupGeneration;
    internal event Action<int>? SelectionChanged;
    internal double MenuReveal => menu?.Reveal ?? 0;
    internal MaterialSelectMenu? Menu => menu;
    internal string SelectedText => selectedIndex >= 0 ? options[selectedIndex] : "";
    internal int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < -1 || value >= options.Length) throw new ArgumentOutOfRangeException(nameof(value));
            if (selectedIndex == value) return;
            selectedIndex = value; menu?.Select(value); InvalidateVisual(); SelectionChanged?.Invoke(value);
        }
    }

    internal MaterialSelect(string[] options)
    {
        this.options = options.ToArray();
        Background = stateTarget; Foreground = TrackButtons.OnSurface;
        BorderThickness = 0; BorderBrush = Color.Transparent; CornerRadius = 12;
        Height = 48; FontSize = 16; Padding = new Thickness(16, 0, 12, 0);
        MaxDropDownHeight = 320;
        KeyboardFocusFeedback.Attach(this);
        stateMotion.TickCallback = amount => Background = stateFrom.Lerp(stateTarget, amount);
    }

    protected override double ArrowAreaWidth => 40;
    protected override Size MeasureHeader(Size availableSize) => new(Math.Min(220, availableSize.Width), 48);

    protected override void RenderHeaderContent(IGraphicsContext context, Rect headerRect, Rect innerHeaderRect)
    {
        var area = new Rect(innerHeaderRect.X + Padding.Left, innerHeaderRect.Y,
            Math.Max(0, innerHeaderRect.Width - Padding.Left - ArrowAreaWidth), innerHeaderRect.Height);
        var style = GetTextRunStyle();
        var key = (SelectedText, GetDpi(), style, area.Width);
        if (label == null || labelKey != key)
        {
            labelKey = key;
            label = GetGraphicsFactory().TextEngine.CreateLayout(new TextLayoutRequest
            {
                Text = SelectedText.AsMemory(), Dpi = key.Item2, DefaultStyle = style,
                Paragraph = new() { MaxWidth = area.Width, Wrapping = TextWrapping.NoWrap, Trimming = TextTrimming.CharacterEllipsis }
            });
        }
        context.Text.Draw(label, new Point(area.X, area.Y + (area.Height - label.MeasuredSize.Height) / 2),
            new TextDrawOptions(Foreground, Owner: this));
        ArrowForeground = Foreground;
    }

    protected override UIElement CreatePopupContent() => menu = new MaterialSelectMenu(options, index =>
    {
        SelectedIndex = index; IsDropDownOpen = false;
    }, () => IsDropDownOpen = false, keyboard =>
    {
        keyboardInteraction = keyboard;
        if (!keyboard) { headerHovered = false; RefreshState(); }
    });

    protected override void SyncPopupContent(UIElement popup) => menu?.Select(SelectedIndex);
    protected override UIElement GetPopupFocusTarget(UIElement popup) => menu!.FocusTarget(SelectedIndex);

    protected override void OnIsDropDownOpenChanged(bool oldValue, bool newValue)
    {
        ++popupGeneration;
        CancelPendingPopup();
        if (newValue) menu?.ResetReveal();
        if (newValue && OperatingSystem.IsWindows() && FindVisualRoot() is Window owner)
        {
            // Focus can scroll the trigger's ancestors. Their extent is refined
            // at the frame boundary; creating the native popup before that pass
            // lets its scroll-dismiss policy immediately close the new menu.
            pendingPopupWindow = owner;
            owner.FrameRendered += OpenAfterLayout;
            owner.InvalidateVisual();
            RefreshState();
            return;
        }
        base.OnIsDropDownOpenChanged(oldValue, newValue);
        if (newValue) menu?.Open(); else menu?.Stop();
        if (!newValue) KeyboardFocusFeedback.Set(this, keyboardInteraction);
        RefreshState();
    }

    private void CancelPendingPopup()
    {
        if (pendingPopupWindow is not { } owner) return;
        owner.FrameRendered -= OpenAfterLayout; pendingPopupWindow = null;
    }

    private void OpenAfterLayout()
    {
        var owner = pendingPopupWindow!; var generation = popupGeneration;
        CancelPendingPopup();
        Application.Current.Dispatcher!.BeginInvoke(() =>
        {
            if (generation != popupGeneration || !IsDropDownOpen || FindVisualRoot() != owner) return;
            owner.PerformLayout();
            base.OnIsDropDownOpenChanged(false, true);
            menu?.Open(); RefreshState();
        });
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        keyboardInteraction = false;
        headerHovered = IsHeaderHit(e);
        KeyboardFocusFeedback.Set(this, false);
        base.OnMouseDown(e);
    }

    protected override void OnMouseEnter() { base.OnMouseEnter(); headerHovered = true; RefreshState(); }
    protected override void OnMouseLeave() { base.OnMouseLeave(); headerHovered = false; RefreshState(); }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); headerHovered = IsHeaderHit(e); RefreshState(); }

    private bool IsHeaderHit(MouseEventArgs e)
    { var point = e.GetPosition(this); return point.X >= 0 && point.X < ActualWidth && point.Y >= 0 && point.Y < ActualHeight; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        keyboardInteraction = true;
        KeyboardFocusFeedback.Set(this, true);
        if (!e.Handled && IsEffectivelyEnabled && (e.Key is Key.Up or Key.Down) && !IsDropDownOpen)
        { IsDropDownOpen = true; e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);
        if (property == KeyboardFocusFeedback.VisibleProperty || property == IsEnabledProperty)
            RefreshState();
    }

    private void RefreshState()
    {
        var next = TrackButtons.StateColor(TrackButtons.TonalSurface, TrackButtons.OnSurface,
            !IsEffectivelyEnabled ? 0 : IsDropDownOpen || GetValue(KeyboardFocusFeedback.VisibleProperty) ? .12 : headerHovered ? .08 : 0);
        if (next == stateTarget) return;
        stateMotion.Stop(); stateFrom = Background; stateTarget = next;
        if (FindVisualRoot() is Window) stateMotion.Start(); else Background = next;
    }

    protected override void OnDispose() { ++popupGeneration; CancelPendingPopup(); stateMotion.Stop(); base.OnDispose(); }
}

internal sealed class MaterialSelectMenu : Panel
{
    internal const double ItemHeight = 48;
    private readonly AnimationClock opening = new(TimeSpan.FromMilliseconds(220), Easing.CubicBezier(.2, 0, 0, 1));
    private readonly Button[] buttons;
    private readonly Image[] checks;
    private readonly Border card;
    private readonly Action dismiss;
    private readonly Action<bool> interaction;
    private int focusedIndex;
    internal double Reveal { get; private set; }
    internal IReadOnlyList<Button> Options => buttons;
    internal Border Card => card;

    internal MaterialSelectMenu(string[] options, Action<int> choose, Action dismiss, Action<bool> interaction)
    {
        this.dismiss = dismiss;
        this.interaction = interaction;
        buttons = new Button[options.Length]; checks = new Image[options.Length];
        var rows = new StackPanel().Vertical();
        for (var index = 0; index < options.Length; index++)
        {
            var item = index;
            var check = checks[index] = Icons.View("check", 20, TrackButtons.OnSurface);
            var content = new Grid().Columns("*,20").Rows("*").Spacing(12).Children(
                new TextBlock().Text(options[index]).FontSize(16).Foreground(TrackButtons.OnSurface)
                    .TextTrimming(TextTrimming.CharacterEllipsis).CenterVertical().Column(0), check.CenterVertical().Column(1));
            var button = TrackButtons.Action(content, () => choose(item), TrackButtons.Text, ItemHeight)
                .Padding(12, 0).CornerRadius(4);
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.MouseDown += _ => interaction(false);
            button.KeyDown += e => { if (e.Key != Key.Escape) interaction(true); };
            buttons[index] = button; rows.Add(button);
        }
        card = new Border().Background(FloatingMenuLayer.Surface).BorderThickness(0).CornerRadius(4).Padding(4, 8)
            .Child(new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(rows));
        Add(card); ResetReveal();
        opening.TickCallback = value => { Reveal = value; Opacity = value; InvalidateVisual(); };
        opening.CompletedCallback = () => { Reveal = Opacity = 1; InvalidateVisual(); };
    }

    internal void ResetReveal() { opening.Stop(); Reveal = Opacity = 0; }
    internal void Open() { opening.Start(); InvalidateVisual(); }
    internal void Stop() => opening.Stop();
    internal UIElement FocusTarget(int selected)
    { focusedIndex = Math.Clamp(selected, 0, buttons.Length - 1); return buttons[focusedIndex]; }

    internal void Select(int selected)
    {
        for (var index = 0; index < buttons.Length; index++)
        {
            checks[index].Opacity = index == selected ? 1 : 0;
            ((MaterialTrackButton)buttons[index]).Palette(index == selected ? TrackButtons.TonalSurface : Color.Transparent, TrackButtons.OnSurface);
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        card.Measure(new Size(availableSize.Width, Math.Min(availableSize.Height, buttons.Length * ItemHeight + 16)));
        return new Size(availableSize.Width, Math.Min(buttons.Length * ItemHeight + 16, availableSize.Height));
    }
    protected override Size ArrangeOverride(Size finalSize)
    { card.Arrange(new Rect(Bounds.X, Bounds.Y, finalSize.Width, finalSize.Height)); return finalSize; }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        // Fixed layout prevents rows jumping while the top-anchored menu unfolds.
        context.Save();
        context.IntersectClip(new Rect(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height * (.85 + .15 * Reveal)));
        context.Translate(0, -6 * (1 - Reveal));
        base.RenderSubtree(context); context.Restore();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key != Key.Escape) interaction(true);
        if (!e.Handled)
        {
            if (e.Key == Key.Escape) { dismiss(); e.Handled = true; }
            else if (e.Key is Key.Up or Key.Down or Key.Home or Key.End)
            {
                focusedIndex = e.Key switch { Key.Home => 0, Key.End => buttons.Length - 1,
                    Key.Up => Math.Max(0, focusedIndex - 1), _ => Math.Min(buttons.Length - 1, focusedIndex + 1) };
                buttons[focusedIndex].Focus(); e.Handled = true;
            }
        }
        base.OnKeyDown(e);
    }
    protected override void OnDispose() { opening.Stop(); base.OnDispose(); }
}
