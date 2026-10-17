using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private static readonly Color ChatAccent = Color.FromRgb(128, 196, 249);
    private static readonly Color ChatOutgoingColor = Color.FromRgb(37, 75, 108);
    private static readonly Color ChatIncomingColor = Color.FromRgb(43, 45, 49);

    private sealed class ChatBubbleRow
    {
        internal Border Root { get; }
        internal readonly Border Bubble;
        internal readonly Border DateBadge;
        private readonly TextBlock date = new TextBlock().FontSize(12).SemiBold();
        internal readonly ChatMessageMotion Motion = new();
        internal ChatMessage? Message;
        internal readonly ChatBubbleContent Content;
        internal readonly Border Footer;
        internal readonly TextBlock Body = new TextBlock().FontSize(14).FontWeight(FontWeight.Normal).TextWrapping(TextWrapping.Wrap);
        internal readonly TextBlock Time = new TextBlock().FontSize(10).Foreground(Color.FromRgb(171, 189, 204));
        internal readonly TextBlock Checks = new TextBlock().Text("✓").FontSize(13).Foreground(ChatAccent);
        internal readonly ProgressRing Spinner = new ProgressRing().Width(12).Height(12).IsActive(false);
        internal readonly Button Error;
        internal readonly Button Open;
        internal readonly StackPanel Attachments = new StackPanel().Vertical().Spacing(8);
        internal CancellationTokenSource? Load;
        internal bool MediaLoading;
        internal bool Bound;
        private readonly MainWindow owner;
        internal ChatBubbleRow(MainWindow owner)
        {
            this.owner = owner;
            Error = TrackButtons.Action(new TextBlock().Text("!").FontSize(12).Bold().Foreground(Color.FromRgb(255, 132, 132)),
                () => owner.Run(owner.RetryChatOutgoingAsync), TrackButtons.Text, 20).Padding(4, 0).IsVisible(false);
            Open = TrackButtons.Action(new TextBlock().Text("↓").FontSize(20), () => owner.Run(() => owner.OpenChatMediaAsync(this)), TrackButtons.Tonal, 34).CenterHorizontal();
            var footer = new StackPanel().Horizontal().Spacing(5).Right().Children(Time, Spinner, Checks, Error);
            Footer = new Border().BorderThickness(0).Child(footer).IsHitTestVisible(false);
            Content = new ChatBubbleContent(Attachments, Body, Open, Footer).Initialize();
            Bubble = new Border().BorderThickness(0).CornerRadius(17).Padding(12, 8).Child(Content).MaxWidth(520).Left();
            Footer.Transitions = [Transition.Create(UIElement.OpacityProperty, 140)];
            Bubble.MouseEnter += () => UpdateMediaHover(true);
            Bubble.MouseLeave += () => UpdateMediaHover(false);
            DateBadge = new Border().Background(Color.FromRgb(48, 53, 59)).CornerRadius(14).Padding(12, 5)
                .Margin(0, 8, 0, 12).Child(date).CenterHorizontal();
            Motion.Content = new StackPanel().Vertical().Children(DateBadge, Bubble);
            Root = new Border().Background(Color.Transparent).Padding(0).Child(Motion);
            var copy = new Command("pookie.chat.copy", "Скопировать текст");
            var retry = new Command("pookie.chat.retry", "Повторить отправку");
            var remove = new Command("pookie.chat.discard", "Убрать сообщение");
            var save = new Command("pookie.chat.save", "Сохранить вложение");
            Root.Commands.Register(copy, () => { if (CopyContent() is { } text) owner.CopyText(text); }, () => !string.IsNullOrEmpty(CopyContent()));
            Root.Commands.Register(retry, () => owner.Run(owner.RetryChatOutgoingAsync), () => owner.ChatOutgoing(Message)?.State == ChatDelivery.Failed);
            Root.Commands.Register(remove, owner.DiscardPendingChat, () => owner.ChatOutgoing(Message) != null && !owner.chatSending);
            Root.Commands.Register(save, () => { if (Message != null) owner.Run(() => owner.SaveChatAttachmentAsync(Message.Id, 0)); },
                () => Message != null && owner.CachedChatMedia(Message)?.Attachments.Length > 0);
            CopyCommand = copy; RetryCommand = retry; RemoveCommand = remove; SaveCommand = save;
        }
        private readonly Command CopyCommand, RetryCommand, RemoveCommand, SaveCommand;
        private string? CopyContent() => Message == null ? null : ChatMediaReference.Parse(Message.Body) == null ? Message.Body :
            owner.CachedChatMedia(Message)?.Caption;
        internal void Bind(ChatMessage message)
        {
            Bound = true;
            if (Message?.SenderId == message.SenderId && (Message.Body == message.Body ||
                Message.Id == message.Id && owner.ChatOutgoing(message)?.HasMedia == true))
            {
                Message = message; Time.Text = ChatPresentation.Time(message.SentAt);
                RefreshDate(); RefreshDelivery(); UpdateMenu(); return;
            }
            Clear(); Message = message;
            Bound = true;
            var own = message.SenderId == owner.me?.Id;
            Bubble.HorizontalAlignment = own ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Bubble.Background = own ? ChatOutgoingColor : ChatIncomingColor;
            Time.Text = ChatPresentation.Time(message.SentAt);
            Time.Foreground = own ? Color.FromRgb(174, 199, 219) : Muted;
            var local = owner.ChatOutgoing(message);
            var media = ChatMediaReference.Parse(message.Body) != null || local?.HasMedia == true;
            Body.IsVisible = !media && message.Body.Length > 0; Body.Text = media ? "" : message.Body;
            Open.IsVisible = false;
            if (media) owner.BindChatMediaPreview(this);
            RefreshDate(); RefreshDelivery();
            UpdateMenu();
            if (owner.chatMessageEntrances.Remove(message.Id)) Motion.Reveal();
        }
        private void UpdateMenu()
        {
            var menu = new ContextMenu().Item(CopyCommand);
            if (owner.ChatOutgoing(Message) != null) menu.Item(RetryCommand).Item(RemoveCommand);
            if (Message != null && (owner.ChatOutgoing(Message)?.HasMedia == true || ChatMediaReference.Parse(Message.Body) != null)) menu.Item(SaveCommand);
            Root.ContextMenu = menu;
        }
        internal void RefreshDate()
        {
            if (Message == null) return;
            date.Text = ChatPresentation.Day(Message.SentAt);
            DateBadge.IsVisible = owner.ChatStartsDay(Message);
        }
        internal void RefreshDelivery()
        {
            var own = Message?.SenderId == owner.me?.Id;
            var state = owner.ChatOutgoing(Message)?.State ?? ChatDelivery.Sent;
            Spinner.IsVisible = Spinner.IsActive = own && state == ChatDelivery.Sending;
            Checks.IsVisible = own && state == ChatDelivery.Sent;
            Error.IsVisible = own && state == ChatDelivery.Failed;
            Error.ToolTip = new TextBlock().Text(owner.ChatOutgoing(Message)?.Error ?? "Повторить отправку");
            Footer.IsHitTestVisible = Error.IsVisible;
            UpdateMediaHover(Bubble.IsMouseOver);
        }
        internal void SetPhotoLayout(Size? size)
        {
            Content.SetPhoto(size); Bubble.Padding = new Thickness(0); Bubble.ClipToBounds = true;
            Bubble.Background = Color.Transparent;
            Content.CaptionBackground = Content.PhotoOnly ? Color.Transparent : Message?.SenderId == owner.me?.Id ? ChatOutgoingColor : ChatIncomingColor;
            Footer.Padding = Content.PhotoOnly ? new Thickness(6, 2) : new Thickness(0);
            Footer.CornerRadius = 10;
            Footer.Background = Content.PhotoOnly ? Color.FromArgb(155, 0, 0, 0) : Color.Transparent;
            Time.Foreground = Content.PhotoOnly ? Color.FromArgb(235, 255, 255, 255) : Message?.SenderId == owner.me?.Id ? Color.FromRgb(174, 199, 219) : Muted;
            UpdateMediaHover(Bubble.IsMouseOver);
        }
        internal void UpdateMediaHover(bool hovered)
        {
            Footer.IsVisible = !MediaLoading || Spinner.IsVisible || Error.IsVisible;
            Footer.Opacity = Content.PhotoOnly && !hovered && !Spinner.IsVisible && !Error.IsVisible ? 0 : 1;
        }
        internal void ResetMediaLayout()
        {
            Content.Reset(); MediaLoading = false; Footer.IsVisible = true; Bubble.Padding = new Thickness(12, 8); Bubble.ClipToBounds = false;
            Bubble.Background = Message?.SenderId == owner.me?.Id ? ChatOutgoingColor : ChatIncomingColor;
            Footer.Background = Color.Transparent; Footer.Padding = new Thickness(0); Footer.Opacity = 1;
        }
        internal void Clear()
        {
            Bound = false; DateBadge.Opacity = 1;
            Load?.Cancel(); Load?.Dispose(); Load = null;
            Motion.Finish(); Spinner.IsActive = false;
            owner.StopChatAnimations(Attachments);
            Attachments.Clear(); Attachments.IsVisible = false; Message = null; Body.Text = ""; Open.IsEnabled = true;
            ResetMediaLayout();
        }
    }
}
