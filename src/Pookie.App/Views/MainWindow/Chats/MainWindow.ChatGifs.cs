using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private ChatEmojiEditor? chatEmojiEditor;
    // Existing animation ownership uses this flag only for the GIF tab.
    private readonly ObservableValue<bool> chatGifsOpen = new(false);
    private TextBox chatGifSearch = null!;
    private TextBlock chatGifStatus = null!;
    private TrackCollectionGrid chatGifResults = null!;
    private ScrollViewer chatGifScroll = null!;
    private CancellationTokenSource? chatGifLoading;
    private long chatGifGeneration;
    private long chatEmojiGeneration;
    private ChatPopupLayer chatEmojiPopup = null!;
    private GlassCard chatEmojiCard = null!;
    private ChatEmojiPanelLayout chatEmojiLayout = null!;
    private ChatPanelTransition chatEmojiTransition = null!;
    private ItemsControl chatEmojiResults = null!;
    private ChatEmojiResultsFade chatEmojiResultsFade = null!;
    private FrameworkElement chatGifBody = null!, chatEmojiBody = null!;
    private StackPanel chatEmojiCategories = null!;
    private ChatTabs chatEmojiModes = null!;
    private DispatcherTimer chatEmojiSearchTimer = null!;
    private bool chatEmojiLoading, chatEmojiUpdating, chatEmojiGifMode;
    private string? chatEmojiGroup;
    private ChatMessage? chatEmojiTarget;
    private long chatEmojiPeer, chatEmojiAccount;
    private Rect? chatEmojiAnchor;
    private readonly List<(Button Button, string? Group)> chatEmojiCategoryButtons = [];
    private sealed record EmojiRow(string Key, string Title, ChatEmoji[] Values);
    private sealed class EmojiSlot { internal string? Value; }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, EmojiSlot> chatEmojiSlots = new();

    private FrameworkElement ChatGifsView()
    {
        chatGifSearch = new TextBox().Placeholder("Поиск эмодзи").Background(Color.Transparent).BorderThickness(0).FontSize(13).MaxLength(100);
        chatGifSearch.OnTextChanged(_ =>
        {
            if (chatEmojiUpdating) return;
            chatEmojiSearchTimer.Stop(); chatEmojiSearchTimer.Start();
        });
        chatGifSearch.GotFocus += () => chatEmojiLayout.SearchMode(true);
        chatGifSearch.OnKeyDown(e => { if (e.Key == Key.Enter && chatEmojiGifMode) { e.Handled = true; Run(SearchChatGifsAsync); } });
        chatEmojiSearchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(220));
        chatEmojiSearchTimer.Tick += () => { chatEmojiSearchTimer.Stop(); if (chatEmojiGifMode) Run(SearchChatGifsAsync); else RefreshChatEmojiResults(); };
        chatGifStatus = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap).Margin(12, 8);
        chatGifResults = new TrackCollectionGrid(3, 94, 3);
        chatGifScroll = new ScrollViewer().Content(chatGifResults).Background(Color.Transparent).BorderThickness(0).Padding(6, 0, 6, 6);
        chatGifScroll.ScrollChanged += QueueVisibleChatGifPreviews;
        chatGifBody = new DockPanel().LastChildFill().Children(chatGifStatus.DockTop(), chatGifScroll);
        chatEmojiResults = new ItemsControl().Items(Array.Empty<EmojiRow>(), r => r.Key).ItemHeight(40).VariableHeightPresenter()
            .Background(Color.Transparent).BorderThickness(0).Padding(8, 0).ItemPadding(new Thickness(0));
        chatEmojiResults.ItemTemplate = ChatEmojiRowTemplate();
        chatEmojiBody = chatEmojiResultsFade = new ChatEmojiResultsFade(chatEmojiResults).Initialize();
        chatEmojiTransition = new ChatPanelTransition(); chatEmojiTransition.Show(chatEmojiBody);
        chatEmojiCategories = new StackPanel().Horizontal().Spacing(0);
        void Category(string? group, string title, string icon)
        {
            var button = TrackButtons.Icon(Icons.View(icon, 19, Muted), () => SelectChatEmojiCategory(group), TrackButtons.Text, 34).Width(34).Padding(6, 0).ToolTip(title);
            chatEmojiCategoryButtons.Add((button, group)); chatEmojiCategories.Add(button);
        }
        Category(null, "Недавние и все эмодзи", "clock");
        foreach (var group in ChatEmojiCatalog.Groups) Category(group.Group, group.Title, group.Icon);
        var categories = new ScrollViewer().HorizontalScroll(ScrollMode.Auto).VerticalScroll(ScrollMode.Disabled).Content(chatEmojiCategories).Padding(0).BorderThickness(0).Background(Color.Transparent);
        chatEmojiModes = new ChatTabs();
        chatEmojiModes.AddTab("Эмодзи", () => SetChatEmojiMode(false));
        chatEmojiModes.AddTab("GIF", () => SetChatEmojiMode(true));
        var modes = new Border().Background(Color.Transparent).BorderThickness(0).Padding(8, 0).Child(chatEmojiModes.CenterHorizontal());
        chatEmojiLayout = new ChatEmojiPanelLayout(categories, MaterialSearchField.Create(chatGifSearch), chatEmojiTransition, modes).Initialize();
        chatEmojiCard = new GlassCard(Surface) { HoverEnabled = false, Focusable = true }.Background(ChatGlassSurface).CornerRadius(20).Padding(0).Child(chatEmojiLayout);
        chatEmojiPopup = new ChatPopupLayer(chatEmojiCard, () => chatEmojiAnchor ?? chatGif.Bounds, () => chatColumns.Bounds).Initialize();
        chatEmojiPopup.Closed = () => { chatEmojiTarget = null; chatEmojiAnchor = null; ClearChatGifResults(); };
        chatEmojiCard.OnKeyDown(e => { if (e.Key == Key.Escape) { e.Handled = true; CloseChatGifs(); chatInput.Focus(); } });
        return chatEmojiPopup;
    }

    private IDataTemplate ChatEmojiRowTemplate() => new DelegateTemplate<EmojiRow>(context =>
    {
        var title = new TextBlock().FontSize(12).Foreground(Muted).CenterVertical().Margin(6, 0);
        var grid = new Grid().Columns("*,*,*,*,*,*,*,*").Rows("40");
        context.Register("title", title); context.Register("grid", grid);
        for (var i = 0; i < 8; i++)
        {
            var image = new Image().Width(28).Height(28).StretchMode(Stretch.Uniform);
            var slot = new EmojiSlot();
            var button = TrackButtons.Action(image, () => { if (slot.Value != null) PickChatEmoji(slot.Value); }, TrackButtons.Text, 40).Padding(2).CornerRadius(10).Column(i);
            chatEmojiSlots.Add(button, slot);
            context.Register("image" + i, image); context.Register("button" + i, button); grid.Add(button);
        }
        var root = new Grid().Columns("*").Rows("*").Children(title, grid); context.Register("root", root); return root;
    }, (_, row, _, context) =>
    {
        var title = context.Get<TextBlock>("title"); title.Text = row.Title; title.IsVisible = row.Title.Length > 0;
        context.Get<Grid>("root").Height = row.Title.Length > 0 ? 28 : 40;
        context.Get<Grid>("grid").IsVisible = row.Title.Length == 0;
        for (var i = 0; i < 8; i++)
        {
            var button = context.Get<Button>("button" + i); var emoji = row.Values.ElementAtOrDefault(i);
            button.IsVisible = emoji != null; chatEmojiSlots.GetValue(button, _ => new()).Value = emoji?.Value;
            context.Get<Image>("image" + i).Source = emoji == null ? null : ChatEmojiArtwork.Source(emoji.Value);
            button.ToolTip = emoji == null ? null : new TextBlock().Text(emoji.Name);
        }
    }, (_, _, _, context) => { for (var i = 0; i < 8; i++) context.Get<Image>("image" + i).Source = null; });

    private async Task OpenChatGifsAsync()
    {
        if (chatEmojiPopup.IsOpen) { CloseChatGifs(); chatInput.Focus(); return; }
        if (chatRecipient == null || chatSending || chatVoiceDraft != null || chatVoiceRecorder is { Recording: true }) return;
        chatEmojiTarget = null; chatEmojiAnchor = null;
        await OpenChatEmojiPanelAsync();
    }
    private async Task OpenChatEmojiPanelAsync()
    {
        if (chatRecipient == null) return;
        chatEmojiAccount = chatAccountId; chatEmojiPeer = chatRecipient.Id;
        CloseChatMessageMenu(); CloseShellMenus(); CloseChatAttachMenu();
        chatEmojiUpdating = true; chatGifSearch.Text = ""; chatEmojiUpdating = false;
        chatEmojiGroup = null; chatEmojiLayout.SearchMode(false, true); chatEmojiLoading = true;
        SetChatEmojiMode(false); chatEmojiPopup.Show(); chatEmojiCard.Focus();
        var generation = ++chatEmojiGeneration;
        await Task.Run(ChatEmojiCatalog.Warm);
        if (disposed || !chatEmojiPopup.IsOpen || generation != chatEmojiGeneration || chatEmojiAccount != chatAccountId || chatEmojiPeer != chatRecipient?.Id) return;
        chatEmojiLoading = false; RefreshChatEmojiResults();
    }
    private void SetChatEmojiMode(bool gif)
    {
        if (gif && (!chatEmojiPopup.IsOpen || !preferences.KlipyEnabled || chatEmojiTarget != null)) return;
        var changed = chatEmojiGifMode != gif; chatEmojiGifMode = gif;
        chatEmojiModes.Select(gif ? 1 : 0);
        chatEmojiModes.SetTabVisible(1, preferences.KlipyEnabled && chatEmojiTarget == null);
        chatGifsOpen.Value = gif && chatEmojiPopup.IsOpen;
        chatEmojiUpdating = true; if (changed) chatGifSearch.Text = ""; chatEmojiUpdating = false;
        chatGifSearch.Placeholder = gif ? "Поиск GIF" : "Поиск эмодзи";
        chatEmojiCategories.IsVisible = !gif;
        chatEmojiTransition.Show(gif ? chatGifBody : chatEmojiBody, gif ? 1 : -1);
        if (gif)
        {
            chatGifsOpen.Value = true; chatEmojiLayout.SearchMode(true);
            chatGifStatus.IsVisible = !klipy.Available;
            chatGifStatus.Text = klipy.Available ? "" : "Поиск GIF недоступен. Можно прикрепить свой GIF скрепкой.";
            if (klipy.Available) Run(SearchChatGifsAsync);
        }
        else { ClearChatGifResults(); if (!chatEmojiLoading) RefreshChatEmojiResults(); }
    }
    private void SelectChatEmojiCategory(string? group)
    { chatEmojiGroup = group; chatEmojiUpdating = true; chatGifSearch.Text = ""; chatEmojiUpdating = false; chatEmojiLayout.SearchMode(false); RefreshChatEmojiResults(); chatEmojiCard.Focus(); }
    private void RefreshChatEmojiResults()
    {
        if (chatEmojiLoading || chatEmojiGifMode) return;
        var rows = new List<EmojiRow>();
        void Section(string key, string title, IEnumerable<ChatEmoji> values)
        {
            var array = values.ToArray(); if (array.Length == 0) return;
            rows.Add(new(key + ":title", title, []));
            var index = 0; foreach (var chunk in array.Chunk(8)) rows.Add(new(key + ":" + index++, "", chunk));
        }
        var query = chatGifSearch.Text.Trim();
        if (query.Length > 0) Section("search", "Результаты поиска", ChatEmojiCatalog.Search(query));
        else if (chatEmojiGroup != null) Section(chatEmojiGroup, ChatEmojiCatalog.Title(chatEmojiGroup), ChatEmojiCatalog.Search("", chatEmojiGroup));
        else
        {
            Section("recent", "Недавние", (chatWorkspace.RecentEmoji ?? ChatProtocol.Reactions).Select(ChatEmojiCatalog.Find).OfType<ChatEmoji>());
            foreach (var group in ChatEmojiCatalog.Groups) Section(group.Group, group.Title, ChatEmojiCatalog.All.Where(e => e.Group == group.Group));
        }
        if (rows.Count == 0) rows.Add(new("empty", "Ничего не найдено", []));
        chatEmojiResultsFade.Replace(() =>
        {
            chatEmojiResults.ItemsSource = ItemsView.Create(rows, r => r.Key, r => r.Key);
            if (chatEmojiResults.FindVisualChild<ScrollViewer>() is ScrollViewer scroll) scroll.SetScrollOffsets(0, 0);
        }, chatEmojiPopup.Reveal > .99);
        foreach (var (button, group) in chatEmojiCategoryButtons)
        { TrackButtons.SetVariant(button, group == chatEmojiGroup ? TrackButtons.Tonal : TrackButtons.Text); ((MaterialTrackButton)button).SetPassive(group == chatEmojiGroup); }
    }
    private void PickChatEmoji(string value)
    {
        if (!chatEmojiPopup.IsOpen || chatRecipient?.Id != chatEmojiPeer || chatAccountId != chatEmojiAccount || !ChatEmojiCatalog.IsEmoji(value)) return;
        if (chatEmojiTarget is { } target) { CloseChatGifs(); Run(() => ReactToChatAsync(target, value)); return; }
        if (chatMusicEditor != null)
        { if (!chatMusicEditor.InsertText(value, ChatRequest.MaxBodyLength)) return; }
        else
        { if (!ChatEmojiCatalog.Fits(chatInput.Text, chatInput.SelectionLength, value, ChatRequest.MaxBodyLength)) return; chatInput.ReplaceSelection(value); }
        chatInput.Focus();
        chatWorkspace = chatWorkspace with { RecentEmoji = ChatEmojiCatalog.Remember(chatWorkspace.RecentEmoji, value) }; SaveChatWorkspace();
    }
    private void CloseChatGifs()
    {
        chatEmojiSearchTimer?.Stop(); chatGifsOpen.Value = false; ++chatGifGeneration; ++chatEmojiGeneration;
        chatEmojiResultsFade?.Stop();
        chatGifLoading?.Cancel(); chatGifLoading?.Dispose(); chatGifLoading = null;
        chatGifPreviewQueue.Clear(); chatEmojiPopup?.Close();
        if (chatGifResults != null) StopChatAnimations(chatGifResults);
    }
    private void ClearChatGifResults()
    {
        chatGifsOpen.Value = false; chatGifLoading?.Cancel(); chatGifLoading?.Dispose(); chatGifLoading = null;
        chatGifPreviewQueue.Clear();
        if (chatGifResults != null) { StopChatAnimations(chatGifResults); chatGifResults.Clear(); }
    }
    private readonly List<(Button Button, Image Image, KlipyGif Gif, long Generation)> chatGifPreviewQueue = [];
    private async Task SearchChatGifsAsync()
    {
        if (!preferences.KlipyEnabled || !chatEmojiPopup.IsOpen || !chatGifsOpen.Value || !klipy.Available || chatRecipient == null) return;
        ClearChatGifResults(); chatGifsOpen.Value = true;
        chatGifLoading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = chatGifLoading.Token; var generation = ++chatGifGeneration; var recipient = chatRecipient.Id; var account = me?.Id;
        chatGifStatus.Text = "Ищем GIF…"; chatGifStatus.IsVisible = true;
        try
        {
            var gifs = await klipy.SearchAsync(chatGifSearch.Text, token);
            if (!preferences.KlipyEnabled || !chatEmojiPopup.IsOpen || disposed || token.IsCancellationRequested || generation != chatGifGeneration || chatRecipient?.Id != recipient || me?.Id != account) return;
            chatGifStatus.Text = gifs.Length == 0 ? "Ничего не найдено" : ""; chatGifStatus.IsVisible = gifs.Length == 0;
            foreach (var gif in gifs)
            {
                var image = new Image { Opacity = 0 }.Height(104).StretchMode(Stretch.UniformToFill);
                image.Transitions = [Transition.Create(UIElement.OpacityProperty, 180)];
                var skeleton = new ChatMediaSkeleton(() => chatGifsOpen.Value && Window.IsActive); skeleton.SetActive(true);
                var button = TrackButtons.Action(new Border().CornerRadius(6).ClipToBounds().Child(new Grid().Columns("*").Rows("104").Children(skeleton, image)), () =>
                {
                    if (!preferences.KlipyEnabled || chatRecipient?.Id != recipient || me?.Id != account) return;
                    chatSelectedGif = gif; chatSelectedFiles = []; UpdateChatAttachments(); CloseChatGifs(); chatInput.Focus();
                }, TrackButtons.Text, 104).Padding(0).CornerRadius(6);
                chatGifResults.Add(button); chatGifPreviewQueue.Add((button, image, gif, generation));
            }
            chatGifResults.InvalidateMeasure();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!disposed && generation == chatGifGeneration) { chatGifStatus.IsVisible = true; chatGifStatus.Text = "Не удалось загрузить GIF. Нажми Enter, чтобы повторить."; } }
    }
    private void QueueVisibleChatGifPreviews()
    {
        if (!chatGifsOpen.Value || chatGifLoading == null || !chatEmojiPopup.IsOpen) return;
        foreach (var entry in chatGifPreviewQueue.ToArray())
        {
            if (entry.Button.ActualHeight <= 0 || entry.Button.Bounds.Bottom < chatGifScroll.Bounds.Y - 104 || entry.Button.Bounds.Y > chatGifScroll.Bounds.Bottom + 104) continue;
            chatGifPreviewQueue.Remove(entry); var token = chatGifLoading.Token;
            Run(() => LoadChatGifPreviewAsync(entry.Image, entry.Gif.PreviewUrl, entry.Generation, token));
        }
    }
    private async Task LoadChatGifPreviewAsync(Image image, string url, long generation, CancellationToken token)
    {
        try
        {
            await chatMediaGate.WaitAsync(token);
            ImageSource? source; byte[] bytes;
            try { bytes = await klipy.DownloadAsync(url, token); source = await Task.Run(() => PrepareChatImage(bytes), token); }
            finally { chatMediaGate.Release(); }
            if (preferences.KlipyEnabled && !disposed && !token.IsCancellationRequested && generation == chatGifGeneration && chatGifsOpen.Value)
            {
                image.Source = source; image.Opacity = 1;
                if (image.Parent is Grid grid) foreach (var skeleton in grid.Children.OfType<ChatMediaSkeleton>()) { skeleton.SetActive(false); skeleton.IsVisible = false; }
                StartChatAnimation(image, bytes);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (token.IsCancellationRequested || generation != chatGifGeneration) return;
            if (image.Parent is Grid grid) foreach (var skeleton in grid.Children.OfType<ChatMediaSkeleton>()) skeleton.SetActive(false);
        }
    }
}
