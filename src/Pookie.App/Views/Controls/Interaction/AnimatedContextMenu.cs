using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

// Keep the public menu model on the owner for command metadata and recycling.
// App-owned rows provide the same animated state layers as the chat menu;
// The chat overlay owns painting, placement and animated dismissal.
internal sealed class AnimatedContextMenu : InAppPopup
{
    internal ContextMenu Model { get; }
    internal IList<MenuEntry> Items => Model.Items;
    internal UIElement? PlacementTarget { get; private set; }
    internal GlassCard Card { get { EnsureContent(); return card!; } }
    internal IReadOnlyList<Button> Buttons => buttons;
    private GlassCard? card;
    private StackPanel? rows;
    private readonly List<Button> buttons = [];
    private MenuEntry[] rendered = [];
    private Window? sourceWindow;
    private CommandTarget commandTarget;
    private AnimatedContextMenu? parent, child;
    private Point? pointer;

    internal AnimatedContextMenu(MenuPlacement placement, Point offset, Menu? model = null)
    {
        Model = model == null ? new ContextMenu() : new ContextMenu(model);
        Model.Placement = placement; Model.PlacementOffset = offset;
        Model.ItemPadding = new Thickness(12, 0);
    }

    private void EnsureContent()
    {
        if (card != null) return;
        // Hundreds of comments can own menus. Allocate rows, glass and animation
        // only when one is actually opened; keep them for that owner's reuse.
        rows = new StackPanel().Vertical().Spacing(0);
        card = new GlassCard(PopupAppearance.AppBackground) { HoverEnabled = false }.Background(PopupAppearance.Surface)
            .CornerRadius(PopupAppearance.Radius).Padding(PopupAppearance.Padding).FontSize(14).MinWidth(224).Focusable(true)
            .Child(new ScrollViewer().Background(Color.Transparent).BorderThickness(0).Padding(0).CornerRadius(0).Content(rows));
        card.MaxWidth = 360; card.MaxHeight = 520;
        InitializePopup(card, Anchor, () => pointer != null ? MenuPlacement.Pointer : Model.Placement);
    }

    internal AnimatedContextMenu Item(Command command) { Model.Item(command); return this; }
    internal AnimatedContextMenu Item(string text, bool isEnabled = true) { Model.AddItem(text, isEnabled); return this; }
    internal AnimatedContextMenu Separator() { Model.AddSeparator(); return this; }

    internal void Show(UIElement owner) => Show(owner, null);
    internal void Show(UIElement owner, Point? pointer)
    {
        if (owner.FindVisualRoot() is not Window window) return;
        sourceWindow = window; PlacementTarget = owner;
        this.pointer = pointer;
        commandTarget = parent?.commandTarget ?? window.CommandRouter.CaptureTarget(owner);
        BuildRows();
        ShowPopup(owner, Card);
    }

    private Rect Anchor()
    {
        var anchor = PlacementTarget?.Bounds ?? Rect.Empty;
        if (pointer != null || Model.Placement == MenuPlacement.Pointer)
        { var position = pointer ?? new Point(anchor.X, anchor.Bottom); anchor = new(position.X, position.Y, 0, 0); }
        var offset = Model.PlacementOffset;
        return new(anchor.X + offset.X, anchor.Y + offset.Y, anchor.Width, anchor.Height);
    }

    private void BuildRows()
    {
        EnsureContent();
        if (rendered.SequenceEqual(Items)) return;
        foreach (var element in rows!.Children.OfType<Control>().ToArray()) { rows.Remove(element); element.Dispose(); }
        buttons.Clear(); rendered = Items.ToArray();
        foreach (var entry in Items)
        {
            if (entry is not MenuItem item)
            {
                rows.Add(new Border().Height(1).Margin(8, 4).Background(Color.FromRgb(56, 56, 62))); continue;
            }
            var label = new TextBlock().FontSize(14).CenterVertical().TextTrimming(TextTrimming.CharacterEllipsis);
            label.SetBinding(TextBlock.TextProperty, item, MenuItem.TextProperty,
                text => (text.Length == 0 ? item.Command?.Text ?? "" : text).Replace("_", ""));
            var line = new StackPanel().Horizontal().Spacing(12);
            if ((item.Icon ?? item.Command?.Icon) is { } icon)
                line.Children(icon.Build(new IconTemplateSize(20, (int)Math.Ceiling(20 * (sourceWindow?.DpiScale ?? 1)))).CenterVertical());
            line.Add(label);
            var button = TrackButtons.Action(line, () => { }, TrackButtons.Text, PopupAppearance.RowHeight).Padding(12, 0);
            line.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.SetBinding(UIElement.IsEnabledProperty, item, MenuItem.IsEnabledProperty);
            if (item.SubMenu != null)
                button.Click += () => OpenSubmenu(item, button);
            else if (item.Command is { } command)
            {
                button.Commands.Register(command, _ => InvokeAsync(command),
                    _ => item.IsEnabled && (item.CanClick?.Invoke() ?? true) && sourceWindow?.CommandRouter.CanExecute(command, commandTarget) == true);
                button.Command = command;
            }
            else button.Click += () => Close();
            buttons.Add(button); rows.Add(button);
        }
    }

    private async ValueTask InvokeAsync(Command command)
    {
        var window = sourceWindow; var target = commandTarget; var owner = PlacementTarget;
        var root = this; while (root.parent != null) root = root.parent;
        root.Close();
        if (window != null) await window.CommandRouter.ExecuteAsync(command, target, owner);
    }
    private void OpenSubmenu(MenuItem item, Button owner)
    {
        if (item.SubMenu == null) return;
        child?.Close();
        child = new AnimatedContextMenu(MenuPlacement.Right, default, item.SubMenu) { parent = this };
        child.Show(owner);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close(); PlacementTarget?.Focus(); e.Handled = true;
        }
        else if (!e.Handled && e.Key is Key.Up or Key.Down or Key.Home or Key.End)
        {
            var enabled = buttons.Where(b => b.IsEffectivelyEnabled).ToArray();
            if (enabled.Length > 0)
            {
                var current = Array.FindIndex(enabled, b => b.IsFocused);
                var next = e.Key switch { Key.Home => 0, Key.End => enabled.Length - 1,
                    Key.Up => (current < 0 ? enabled.Length - 1 : (current + enabled.Length - 1) % enabled.Length), _ => (current + 1) % enabled.Length };
                enabled[next].Focus();
            }
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
    protected override void OnDispose() { child?.Dispose(); Model.Dispose(); base.OnDispose(); }
}
