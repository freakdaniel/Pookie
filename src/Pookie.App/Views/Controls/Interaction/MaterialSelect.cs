using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Pookie.App;

// The trigger and its dropdown share the main window's chat popup compositor.
internal sealed class MaterialSelect : ContentControl
{
    private readonly string[] options;
    private readonly AnimationClock stateMotion = new(TimeSpan.FromMilliseconds(150), Easing.CubicBezier(.2, 0, 0, 1));
    private MaterialSelectMenu? menu;
    private int selectedIndex = -1;
    private ITextLayout? label;
    private (string Text, uint Dpi, TextRunStyle Style, double Width) labelKey;
    private Color stateFrom, stateTarget = TrackButtons.TonalSurface;
    private bool keyboardInteraction, headerHovered;
    private InAppPopup? popup;
    private bool isDropDownOpen;
    internal double MaxDropDownHeight { get; set; } = 320;
    internal bool IsDropDownOpen
    {
        get => isDropDownOpen;
        set { if (isDropDownOpen == value) return; isDropDownOpen = value; UpdatePopup(); }
    }
    internal event Action<int>? SelectionChanged;
    internal double MenuReveal => popup?.Reveal ?? 0;
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
        Focusable = true;
        Content = Icons.View("caret-down", 20, TrackButtons.OnSurface).Right().CenterVertical();
        KeyboardFocusFeedback.Attach(this);
        stateMotion.TickCallback = amount => Background = stateFrom.Lerp(stateTarget, amount);
    }

    protected override Size MeasureContent(Size availableSize)
    { base.MeasureContent(availableSize); return new(Math.Min(220, availableSize.Width), 48); }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);
        var area = new Rect(Bounds.X + Padding.Left, Bounds.Y,
            Math.Max(0, Bounds.Width - Padding.Left - 40), Bounds.Height);
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
    }

    private void UpdatePopup()
    {
        if (IsDropDownOpen)
        {
            if (FindVisualRoot() is not Window) { isDropDownOpen = false; return; }
            if (menu == null)
            {
                menu = new MaterialSelectMenu(options, index =>
                {
                    SelectedIndex = index; IsDropDownOpen = false;
                }, () => IsDropDownOpen = false, keyboard =>
                {
                    keyboardInteraction = keyboard;
                    if (!keyboard) { headerHovered = false; RefreshState(); }
                });
                popup = new InAppPopup();
                popup.InitializePopup(menu, () => Bounds, () => MenuPlacement.Below);
                popup.Dismissing = () => IsDropDownOpen = false;
            }
            menu.Width = ActualWidth; menu.MaxHeight = MaxDropDownHeight;
            menu.Select(SelectedIndex);
            popup!.ShowPopup(this, menu.FocusTarget(SelectedIndex));
        }
        else
        {
            popup?.Close();
            KeyboardFocusFeedback.Set(this, keyboardInteraction);
        }
        RefreshState();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left) return;
        keyboardInteraction = false; headerHovered = IsHeaderHit(e);
        KeyboardFocusFeedback.Set(this, false);
        Focus(); IsDropDownOpen = !IsDropDownOpen; e.Handled = true;
    }

    protected override void OnMouseEnter() { base.OnMouseEnter(); headerHovered = true; RefreshState(); }
    protected override void OnMouseLeave() { base.OnMouseLeave(); headerHovered = false; RefreshState(); }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); headerHovered = IsHeaderHit(e); RefreshState(); }

    private bool IsHeaderHit(MouseEventArgs e)
    { var point = e.GetPosition(this); return point.X >= 0 && point.X < ActualWidth && point.Y >= 0 && point.Y < ActualHeight; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && IsEffectivelyEnabled)
        {
            keyboardInteraction = true; KeyboardFocusFeedback.Set(this, true);
            if (e.Key is Key.Space or Key.Enter) { IsDropDownOpen = !IsDropDownOpen; e.Handled = true; }
            else if (e.Key is Key.Up or Key.Down) { IsDropDownOpen = true; e.Handled = true; }
            else if (e.Key == Key.Escape && IsDropDownOpen) { IsDropDownOpen = false; e.Handled = true; }
        }
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

    protected override void OnDispose() { popup?.Dispose(); stateMotion.Stop(); base.OnDispose(); }
}

internal sealed class MaterialSelectMenu : Panel
{
    internal const double ItemHeight = 48;
    private readonly Button[] buttons;
    private readonly Image[] checks;
    private readonly GlassCard card;
    private readonly Action dismiss;
    private readonly Action<bool> interaction;
    private int focusedIndex;
    internal IReadOnlyList<Button> Options => buttons;
    internal GlassCard Card => card;

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
                new TextBlock().Text(options[index]).FontSize(14).Foreground(TrackButtons.OnSurface)
                    .TextTrimming(TextTrimming.CharacterEllipsis).CenterVertical().Column(0), check.CenterVertical().Column(1));
            var button = TrackButtons.Action(content, () => choose(item), TrackButtons.Text, ItemHeight)
                .Padding(12, 0).CornerRadius(12);
            content.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.MouseDown += _ => interaction(false);
            button.KeyDown += e => { if (e.Key != Key.Escape) interaction(true); };
            buttons[index] = button; rows.Add(button);
        }
        card = new GlassCard(PopupAppearance.AppBackground) { HoverEnabled = false }
            .Background(PopupAppearance.Surface).CornerRadius(PopupAppearance.Radius).Padding(PopupAppearance.Padding, 8)
            .Child(new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).Content(rows));
        Add(card);
    }

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
}
