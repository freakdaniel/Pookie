using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Pookie.App.Platform;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly Dictionary<(KeyGesture Key, bool Expanded), ShortcutBinding> shortcutKeys = [];
    private readonly Dictionary<(string Button, bool Expanded), ShortcutBinding> shortcutMouse = [];

    private Key? capturedUntilRelease;

    private void InitializeShortcuts()
    {
        // MewUI installs this binding in every main window, including release builds.
        Window.InputMap.Unmap(ShortcutBinding.RepaintGesture);
        void Rebuild()
        {
            shortcutKeys.Clear(); shortcutMouse.Clear();
            foreach (var binding in ShortcutBinding.All)
            {
                var value = Assigned(binding);
                foreach (var expanded in new[] { false, true })
                {
                    if (!binding.Supports(expanded)) continue;
                    if (value is "Mouse4" or "Mouse5") shortcutMouse.TryAdd((value, expanded), binding);
                    else if (KeyGesture.TryParse(value, out var key)) shortcutKeys.TryAdd((key, expanded), binding);
                }
            }
        }
        shortcutRefreshers.Add(Rebuild);
        Rebuild();
        // Unmodified keys bubble after focused buttons, sliders and lists have handled them.
        Window.KeyDown += e =>
        {
            if (e.Modifiers == ModifierKeys.None) HandleShortcutKey(e);
            // Like the original window command, the diagnostics chord also works in search
            // fields and dialogs once their own key handlers have had a chance to consume it.
            else if (!e.Handled && !shortcutDialog.IsVisible &&
                shortcutKeys.TryGetValue((new(e.Key, e.Modifiers), expandedOpen), out var binding) &&
                binding.Action == ShortcutAction.RepaintOverlay)
                e.Handled = e.IsRepeat || ExecuteShortcut(binding.Action);
        };
        Window.NativeMessage += args => ShortcutMouseInput.Route(args, HandleShortcutMouse);
        Window.PreviewKeyUp += e => { if (e.Key == capturedUntilRelease) capturedUntilRelease = null; };
        Window.Deactivated += () => { capturedUntilRelease = null; CloseShortcutRecorder(); };
        settingsSection.Changed += CloseShortcutRecorder;
        page.Changed += CloseShortcutRecorder;
    }

    private bool ShortcutOverlayActive => disposed || !Window.IsActive || !CanUseWorkspace || startupSplash.IsVisible ||
        widevineDialog?.IsOpen == true || embeddedLoginDialog.IsOpen || chatMusicDialog.IsOpen ||
        playlistPickerClosing || playlistDialog.IsOpen || playlistEditor.IsOpen || shareDialog.IsOpen ||
        chatVideoOpen.Value || chatEmojiPopup.IsOpen || chatMessageMenu.IsOpen || chatAttachMenu.IsOpen ||
        chatGifsOpen.Value || InAppPopup.IsPresenting(Window) || settingsBusy || confirmingShortcut ||
        Application.Current!.AllWindows.Any(child => child.Owner == Window && child.IsVisible);

    private void HandleShortcutMouse(string value)
    {
        if (ShortcutOverlayActive) return;
        if (recordingShortcut != null) PreviewShortcut(value);
        else if (shortcutDialog.IsVisible) return;
        else if (shortcutMouse.TryGetValue((value, expandedOpen), out var match)) ExecuteShortcut(match.Action);
    }

    private bool CaptureShortcutKey(KeyEventArgs e)
    {
        if (e.IsRepeat && capturedUntilRelease == e.Key) { e.Handled = true; return true; }
        if (!shortcutDialog.IsOpen) return false;
        if (e.Key is Key.Escape or Key.Tab) return shortcutDialog.HandleKey(e);
        // Allow normal activation of Cancel/Save after Tab moves focus there.
        if (e.Modifiers == ModifierKeys.None && e.Key is Key.Space or Key.Enter &&
            Window.FocusManager.FocusedElement is Button) return true;
        e.Handled = true;
        if (e.IsRepeat || e.Key == Key.None) return true;
        capturedUntilRelease = e.Key;
        if (e.Key == Key.Enter && e.Modifiers == ModifierKeys.None) SavePendingShortcut();
        else if (e.Key == Key.Backspace && e.Modifiers == ModifierKeys.None) PreviewShortcut("");
        else if (e.Key is Key.Enter or Key.ContextMenu || e.Key == Key.F10 && e.ShiftKey)
        {
            PreviewShortcut(null);
            shortcutNotice.Text = "Эта клавиша используется интерфейсом. Нажми другое сочетание.";
            shortcutNotice.IsVisible = true;
        }
        else PreviewShortcut(new KeySequence(new KeyGesture(e.Key, e.Modifiers)).ToString());
        return true;
    }

    private void HandleShortcutKey(KeyEventArgs e)
    {
        if (e.Handled || shortcutDialog.IsVisible || !shortcutKeys.TryGetValue((new(e.Key, e.Modifiers), expandedOpen), out var binding) || ShortcutOverlayActive) return;
        var focus = Window.FocusManager.FocusedElement;
        if (focus is ITextCompositionClient { IsComposing: true }) return;
        if (focus is TextBase or SelectableTextBlock)
        {
            // Keep cursor movement, clipboard shortcuts, AltGr and printable text in the editor.
            if (!e.PrimaryKey || e.AltKey || binding.Action is not (ShortcutAction.Search or ShortcutAction.Find or ShortcutAction.Settings) ||
                e.Key is Key.A or Key.C or Key.V or Key.X or Key.Y or Key.Z or Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.Backspace or Key.Delete)
                return;
        }
        if (e.IsRepeat && binding.Action is not (ShortcutAction.SeekBack or ShortcutAction.SeekForward or ShortcutAction.VolumeUp or ShortcutAction.VolumeDown))
        { e.Handled = true; return; }
        e.Handled = ExecuteShortcut(binding.Action);
    }

    private bool ExecuteShortcut(ShortcutAction action)
    {
        if (action != ShortcutAction.RepaintOverlay && shellMenus.Any(menu => menu.Layer.IsOpen) &&
            !(queueOpen.Value && action == ShortcutAction.Queue)) return false;
        Action? command = action switch
        {
            ShortcutAction.Back when canGoBack.Value => () => Run(() => MoveNavigationAsync(-1)),
            ShortcutAction.Forward when canGoForward.Value => () => Run(() => MoveNavigationAsync(1)),
            ShortcutAction.Settings => () => Run(() => ShowSettingsAsync(settingsSection.Value)),
            ShortcutAction.Search => OpenTopSearch,
            ShortcutAction.Find when page.Value == Page.Chats && chatRecipient == null && !chatFavoritesOpen => () => chatSearch.Focus(),
            ShortcutAction.Find when page.Value == Page.Chats => OpenChatThreadSearch,
            ShortcutAction.Find when page.Value == Page.LibraryTracks => () => likedFilter.Focus(),
            ShortcutAction.Find when page.Value == Page.LibraryCollection && playlistCollectionVisible.Value => () => collectionFilter.Focus(),
            ShortcutAction.Play when playerVisible.Value => () => Run(ToggleAsync),
            ShortcutAction.Previous when playbackQueue.Snapshot.CanPrevious => () => Run(() => SkipAsync(-1)),
            ShortcutAction.Next when playbackQueue.Snapshot.CanNext => () => Run(() => SkipAsync(1)),
            ShortcutAction.VolumeUp => () => volume.Value = Math.Min(preferences.MaximumVolume, volume.Value + 5),
            ShortcutAction.VolumeDown => () => volume.Value = Math.Max(0, volume.Value - 5),
            ShortcutAction.Mute => ToggleMute,
            ShortcutAction.Queue when playerVisible.Value && expandedOpen => () => ToggleExpandedPanel(PlayerPanel.Queue),
            ShortcutAction.Queue when playerVisible.Value => ToggleCompactQueue,
            ShortcutAction.SeekBack or ShortcutAction.SeekForward when audioReady && !audioPreparing && !seekDragging =>
                () => { QueueSeek(progress.Value + (action == ShortcutAction.SeekForward ? 10 : -10)); FlushSeek(); },
            ShortcutAction.Expand when playerVisible.Value => () => SetExpandedPlayer(!expandedOpen),
            ShortcutAction.Lyrics when playerVisible.Value => ToggleLyrics,
            ShortcutAction.Like when likeAvailable.Value => () => Run(ToggleLikeAsync),
            ShortcutAction.Shuffle => () => shuffle.Value = !shuffle.Value,
            ShortcutAction.Repeat => CycleRepeat,
            ShortcutAction.RepaintOverlay => () => WindowPresentation.ToggleDirtyRegionOverlay(Window),
            _ => null
        };
        if (command == null) return false;
        tooltipLayer.ShortcutInvoked();
        command();
        return true;
    }

    private void ToggleLyrics()
    {
        var openingWithLyrics = !expandedOpen && expandedPanelMode == PlayerPanel.Lyrics;
        SetExpandedPlayer(true);
        if (!openingWithLyrics) ToggleExpandedPanel(PlayerPanel.Lyrics);
    }

    private void ToggleCompactQueue()
    {
        queueOpen.Value = !queueOpen.Value;
        profileOpen.Value = false;
    }
}
