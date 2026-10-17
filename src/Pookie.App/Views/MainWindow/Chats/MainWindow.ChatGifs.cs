using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private readonly ObservableValue<bool> chatGifsOpen = new(false);
    private TextBox chatGifSearch = null!;
    private TextBlock chatGifStatus = null!;
    private TrackCollectionGrid chatGifResults = null!;
    private ScrollViewer chatGifScroll = null!;
    private CancellationTokenSource? chatGifLoading;
    private long chatGifGeneration;

    private FrameworkElement ChatGifsView()
    {
        chatGifSearch = new TextBox().Placeholder("Найти GIF").Background(Color.Transparent).BorderThickness(0).FontSize(14)
            .OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; Run(SearchChatGifsAsync); } });
        chatGifStatus = new TextBlock().FontSize(12).Foreground(Muted).TextWrapping(TextWrapping.Wrap);
        chatGifResults = new TrackCollectionGrid(3, 120, 12);
        var panel = new Border().Background(Raised).CornerRadius(24).Padding(22).Width(520).MaxHeight(540).Center().Child(
            new Grid().Columns("*").Rows("Auto,Auto,Auto,*").Spacing(16).Children(
                new Grid().Columns("*,36").Rows("Auto").Row(0).Children(new TextBlock().Text("GIF · KLIPY").FontSize(22).Bold().Column(0).CenterVertical(),
                    TrackButtons.Icon(Icons.View("x", 20), CloseChatGifs, TrackButtons.Text, 36).Column(1)),
                new Grid().Columns("*,40").Rows("Auto").Spacing(8).Row(1).Children(MaterialSearchField.Create(chatGifSearch).Column(0),
                    TrackButtons.Icon(Icons.View("magnifying-glass", 20), () => Run(SearchChatGifsAsync), TrackButtons.Tonal, 40).Column(1)),
                chatGifStatus.Row(2), (chatGifScroll = new ScrollViewer()).Content(chatGifResults).Background(Color.Transparent).BorderThickness(0).Padding(0).Height(310).Row(3)));
        return new Grid().Columns("*").Rows("*").BindIsVisible(chatGifsOpen).Children(
            new Button().Background(Color.FromArgb(150, 0, 0, 0)).BorderThickness(0).Padding(0).OnClick(CloseChatGifs), panel);
    }
    private Task OpenChatGifsAsync()
    {
        if (chatRecipient == null || chatSending) return Task.CompletedTask;
        chatGifsOpen.Value = true;
        chatGifStatus.Text = klipy.Available ? "" : "Поиск GIF сейчас недоступен. Свой GIF можно прикрепить кнопкой +.";
        return klipy.Available ? SearchChatGifsAsync() : Task.CompletedTask;
    }
    private void CloseChatGifs()
    {
        chatGifsOpen.Value = false; ++chatGifGeneration;
        chatGifLoading?.Cancel(); chatGifLoading?.Dispose(); chatGifLoading = null;
        if (chatGifResults != null) { StopChatAnimations(chatGifResults); chatGifResults.Clear(); }
    }
    private async Task SearchChatGifsAsync()
    {
        if (!chatGifsOpen.Value || !klipy.Available || chatRecipient == null) return;
        chatGifLoading?.Cancel(); chatGifLoading?.Dispose();
        chatGifLoading = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = chatGifLoading.Token; var generation = ++chatGifGeneration; var recipient = chatRecipient.Id; var account = me?.Id;
        StopChatAnimations(chatGifResults); chatGifResults.Clear(); chatGifStatus.Text = "Ищем GIF…";
        try
        {
            var gifs = await klipy.SearchAsync(chatGifSearch.Text, token);
            if (disposed || generation != chatGifGeneration || chatRecipient?.Id != recipient || me?.Id != account) return;
            chatGifStatus.Text = gifs.Length == 0 ? "Ничего не найдено" : "";
            foreach (var gif in gifs)
            {
                var image = new Image().Width(140).Height(100).StretchMode(Stretch.Uniform);
                var button = new Button().StyleName("flat-button").Padding(0).BorderThickness(0).Background(Color.Transparent).CornerRadius(12)
                    .Content(new Border().CornerRadius(12).ClipToBounds().Child(image)).OnClick(() =>
                    { if (chatRecipient?.Id != recipient || me?.Id != account) return; chatSelectedGif = gif; chatSelectedFiles = []; UpdateChatAttachments(); CloseChatGifs(); });
                chatGifResults.Add(button);
                _ = LoadChatGifPreviewAsync(image, gif.PreviewUrl, generation, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!disposed && generation == chatGifGeneration) chatGifStatus.Text = "Не удалось загрузить GIF. Попробуй ещё раз."; }
    }
    private async Task LoadChatGifPreviewAsync(Image image, string url, long generation, CancellationToken token)
    {
        try
        {
            await chatMediaGate.WaitAsync(token);
            ImageSource? source; byte[] bytes;
            try { bytes = await klipy.DownloadAsync(url, token); source = await Task.Run(() => PrepareChatImage(bytes), token); }
            finally { chatMediaGate.Release(); }
            if (!disposed && !token.IsCancellationRequested && generation == chatGifGeneration) { image.Source = source; StartChatAnimation(image, bytes); }
        }
        catch (Exception) { }
    }
}
