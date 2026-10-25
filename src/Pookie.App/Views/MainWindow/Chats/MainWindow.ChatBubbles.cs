using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Animation;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private static readonly Color ChatAccent = Color.FromRgb(128, 196, 249);
    private static readonly Color ChatOutgoingColor = Color.FromRgb(37, 75, 108);
    private static readonly Color ChatIncomingColor = Color.FromRgb(41, 41, 46);

    private sealed class ChatBubbleRow
    {
        internal Border Root { get; }
        internal readonly Border Bubble;
        internal readonly Border DateBadge;
        private readonly TextBlock date = new TextBlock().FontSize(12).SemiBold();
        internal readonly ChatMessageMotion Motion = new();
        internal ChatMessage? Message;
        internal string? ItemKey;
        internal readonly ChatBubbleContent Content;
        internal readonly Border Footer;
        internal readonly ChatMessageText Body = new() { FontSize = 14, FontWeight = FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
        internal readonly TextBlock Time = new TextBlock().Bottom().FontSize(10).Foreground(Color.FromRgb(171, 189, 204));
        internal readonly TextBlock Checks = new TextBlock().Text("✓").FontSize(11).Foreground(ChatAccent);
        internal readonly ProgressRing Spinner = new ProgressRing().Width(12).Height(12).IsActive(false);
        internal readonly Button Error;
        internal readonly Button Open;
        private readonly Grid deliveryIcon;
        private readonly Button quote;
        private readonly ChatMessageFrame frame;
        private readonly TextBlock quoteText = new TextBlock().FontSize(12).Foreground(ChatAccent).TextTrimming(TextTrimming.CharacterEllipsis);
        private readonly StackPanel reactions = new StackPanel().Horizontal().Spacing(3);
        internal readonly StackPanel Attachments = new StackPanel().Vertical().Spacing(8);
        internal Image MusicBackdrop = null!;
        private Border? musicContent;
        internal CancellationTokenSource? Load;
        internal bool MediaLoading, MediaResolved, FollowMediaResize;
        internal string? MediaKey;
        internal int MediaGeneration;
        internal bool Bound;
        private readonly MainWindow owner;
        private readonly AnimationClock highlight=new(TimeSpan.FromMilliseconds(900),Easing.Linear);
        internal ChatBubbleRow(MainWindow owner)
        {
            this.owner = owner;
            Body.Copy = owner.CopyText;
            Body.SelectionClick=()=>{if(owner.chatSelected.Count==0||Message==null)return false;owner.ToggleChatSelection(Message);return true;};
            Body.OpenMusic = url => owner.Run(() => owner.OpenChatMusicUrlAsync(url));
            Error = TrackButtons.Action(new TextBlock().Text("!").FontSize(12).Bold().Foreground(Color.FromRgb(255, 132, 132)),
                () => owner.Run(owner.RetryChatOutgoingAsync), TrackButtons.Text, 20).Padding(4, 0).IsVisible(false);
            Open = TrackButtons.Action(new TextBlock().Text("↓").FontSize(20), () => owner.Run(() => owner.OpenChatMediaAsync(this)), TrackButtons.Tonal, 34).CenterHorizontal();
            deliveryIcon = new Grid().Columns("*").Rows("*").Width(14).Height(14)
                .Children(Spinner.Center(), Checks.CenterHorizontal().Bottom().Margin(0, 0, 0, 1), Error.Width(14).Height(14).Padding(0).Center());
            var footer = new StackPanel().Horizontal().Spacing(2).Right().Children(Time, deliveryIcon);
            Footer = new Border().BorderThickness(0).Child(footer).IsHitTestVisible(false);
            Content = new ChatBubbleContent(Attachments, Body, Open, Footer).Initialize();
            Content.ResizeTick = () => { if (Bound && FollowMediaResize && owner.page.Value == Page.Chats) owner.RequestChatBottom(); };
            Bubble = new Border().BorderThickness(0).CornerRadius(18).Padding(10, 6).Child(Content).MaxWidth(520).Left();
            Footer.Transitions = [Transition.Create(UIElement.OpacityProperty, 140)];
            Bubble.MouseEnter += () => UpdateMediaHover(true);
            Bubble.MouseLeave += () => UpdateMediaHover(false);
            DateBadge = new Border().Background(Color.FromRgb(48, 53, 59)).CornerRadius(14).Padding(12, 5)
                .Margin(0, 8, 0, 12).Child(date).CenterHorizontal();
            quote=TrackButtons.Action(new Grid().Columns("3,*").Rows("*").Spacing(8).Children(new Border().Background(ChatAccent).BorderThickness(0).CornerRadius(1).Margin(0,6).Column(0), quoteText.Column(1).CenterVertical()),()=>
            {if(ReplyReference() is {} reply)owner.Run(()=>owner.JumpChatMessageAsync(reply.PeerId,reply.MessageId));},TrackButtons.Tonal,32).Padding(10,0).CornerRadius(8).MaxWidth(520).IsVisible(false);
            ((FrameworkElement)quote.Content!).HorizontalAlignment = HorizontalAlignment.Stretch;
            frame = new ChatMessageFrame(quote, Bubble).Initialize();
            Motion.Content = new StackPanel().Vertical().Children(DateBadge,frame,reactions);
            Root = new Border().Background(Color.Transparent).Padding(0).Child(Motion);
            var copy = new Command("pookie.chat.copy", "Скопировать текст");
            var retry = new Command("pookie.chat.retry", "Повторить отправку");
            var remove = new Command("pookie.chat.discard", "Убрать сообщение");
            var save = new Command("pookie.chat.save", "Сохранить вложение");
            Root.Commands.Register(copy, () => { if (CopyContent() is { } text) owner.CopyText(text); }, () => !string.IsNullOrEmpty(CopyContent()));
            Root.Commands.Register(retry, () => owner.Run(owner.RetryChatOutgoingAsync), () => owner.ChatOutgoing(Message)?.State == ChatDelivery.Failed);
            Root.Commands.Register(remove, owner.DiscardPendingChat, () => owner.ChatOutgoing(Message) != null && !owner.chatSending);
            Root.Commands.Register(save, () => { if (Message != null) owner.Run(() => owner.SaveChatAttachmentAsync(Message.Id, 0)); },
                () => Message != null && (owner.CachedChatMedia(Message)?.Attachments.Length > 0 || owner.ChatMediaSummaryFor(Message, owner.chatRecipient?.Id ?? 0)?.Files?.Length > 0));
            var reply=new Command("pookie.chat.reply","Ответить");
            var favorite=new Command("pookie.chat.favorite","В избранное / убрать");
            var select=new Command("pookie.chat.select","Выделить");
            Root.Commands.Register(reply,()=>{if(Message!=null)owner.ReplyToChat(Message);},()=>Message!=null&&owner.ChatOutgoing(Message)==null);
            Root.Commands.Register(favorite,()=>{if(Message!=null)owner.ChatFavoriteMessage(Message);},()=>Message!=null&&owner.ChatOutgoing(Message)==null);
            Root.Commands.Register(select,()=>{if(Message!=null)owner.ToggleChatSelection(Message);});
            ReplyCommand=reply;FavoriteCommand=favorite;SelectCommand=select;
            foreach(var emoji in ChatProtocol.Reactions)
            {var command=new Command("chat.react."+emoji,emoji); Root.Commands.Register(command,()=>{if(Message!=null)owner.Run(()=>owner.ReactToChatAsync(Message,emoji));},()=>!owner.chatSending);ReactionCommands.Add(command);}
            CopyCommand = copy; RetryCommand = retry; RemoveCommand = remove; SaveCommand = save;
        }
        private readonly Command CopyCommand, RetryCommand, RemoveCommand, SaveCommand,ReplyCommand,FavoriteCommand,SelectCommand;
        private readonly List<Command> ReactionCommands=[];
        private string? CopyContent() => Body.SelectedText ?? (Message == null ? null : ChatMediaReference.Parse(Message.Body) == null ? ChatProtocol.Project(Message,owner.chatAccountId,owner.chatRecipient?.Id??0).Text :
            owner.CachedChatMedia(Message)?.Caption);
        internal void Bind(ChatMessage message)
        {
            Bound = true;
            if (Message?.SenderId == message.SenderId && (Message.Body == message.Body ||
                Message.Id == message.Id && owner.ChatOutgoing(message)?.HasMedia == true))
            {
                Message = message; Time.Text = ChatPresentation.Time(message.SentAt);
                RefreshDate(); RefreshDelivery(); RefreshExtensions(); UpdateMenu(); return;
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
            var projection=ChatProtocol.Project(message,owner.chatAccountId,owner.chatRecipient?.Id??0);
            Body.IsVisible = !media && projection.Text.Length > 0; Body.Text = media ? "" : projection.Text;
            Open.IsVisible = false;
            if (media) owner.BindChatMediaPreview(this);
            else owner.BindChatMusic(this);
            RefreshDate(); RefreshDelivery(); RefreshExtensions();
            UpdateMenu();
            if (owner.chatMessageEntrances.Remove(message.Id)) Motion.Reveal();
        }
        private void UpdateMenu()
        {
            var menu = new ContextMenu().Item(ReplyCommand).Item(CopyCommand).Item(FavoriteCommand).Item(SelectCommand);
            foreach(var command in ReactionCommands)menu.Item(command);
            if (owner.ChatOutgoing(Message) != null) menu.Item(RetryCommand).Item(RemoveCommand);
            if (Message != null && (owner.ChatOutgoing(Message)?.HasMedia == true || ChatMediaReference.Parse(Message.Body) != null)) menu.Item(SaveCommand);
            Root.ContextMenu = menu;
        }
        internal void Highlight()
        {
            highlight.Stop();
            highlight.TickCallback=t=>{if(Bound)Root.Background=Color.FromArgb((byte)(75*(1-t)),128,196,249);};
            highlight.CompletedCallback=()=>Root.Background=owner.chatSelected.Contains(Message?.Id??"")?Color.FromArgb(35,128,196,249):Color.Transparent;
            highlight.Start();
        }
        private ChatReplyReference? ReplyReference()
        {
            if(Message==null)return null;
            var reply=ChatProtocol.Project(Message,owner.chatAccountId,owner.chatRecipient?.Id??0).Reply??owner.ChatMediaSummaryFor(Message,owner.chatRecipient?.Id??0)?.Reply;
            return reply==null?null:reply with{PeerId=owner.chatRecipient?.Id??reply.PeerId};
        }
        internal void RefreshExtensions()
        {
            if(Message==null)return;
            var projection=ChatProtocol.Project(Message,owner.chatAccountId,owner.chatRecipient?.Id??0);
            var reply=ReplyReference();quote.IsVisible=reply!=null;
            quoteText.Text = reply?.Preview ?? "";
            var own=Message.SenderId==owner.chatAccountId;
            frame.HorizontalAlignment=reactions.HorizontalAlignment=own?HorizontalAlignment.Right:HorizontalAlignment.Left;
            reactions.Clear();
            if(owner.chatReactions.TryGetValue(Message.Id,out var authors))
                foreach(var group in authors.GroupBy(p=>p.Value))
                {var emoji=group.Key;reactions.Add(TrackButtons.Action(new TextBlock().Text(emoji+" "+group.Count()).FontSize(12),()=>owner.Run(()=>owner.ReactToChatAsync(Message,emoji)),group.Any(p=>p.Key==owner.chatAccountId)?TrackButtons.Tonal:TrackButtons.Text,26).Padding(7,0));}
            reactions.IsVisible=reactions.Children.Count>0;
            ((MaterialTrackButton)quote).Palette(own ? ChatOutgoingColor : ChatIncomingColor, ChatAccent);
            var connected=owner.ChatMessageGroup(Message);
            Root.Margin=new Thickness(0,connected.Previous?2:8,0,0);
            Bubble.NonUniformCornerRadius=own?new CornerRadius(18,connected.Previous?6:18,connected.Next?6:18,18):new CornerRadius(connected.Previous?6:18,18,18,connected.Next?6:18);
            Root.Background=owner.chatSelected.Contains(Message.Id)?Color.FromArgb(35,128,196,249):Color.Transparent;
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
            deliveryIcon.IsVisible = own;
            var state = owner.ChatOutgoing(Message)?.State ?? ChatDelivery.Sent;
            Spinner.IsVisible = Spinner.IsActive = own && state == ChatDelivery.Sending;
            Checks.IsVisible = own && state == ChatDelivery.Sent;
            Error.IsVisible = own && state == ChatDelivery.Failed;
            Error.ToolTip = new TextBlock().Text(owner.ChatOutgoing(Message)?.Error ?? "Повторить отправку");
            Footer.IsHitTestVisible = Error.IsVisible;
            UpdateMediaHover(Bubble.IsMouseOver);
        }
        internal void SetPhotoLayout(Size? size, bool video = false)
        {
            Content.SetPhoto(size, video); Bubble.Padding = new Thickness(0); Bubble.ClipToBounds = true;
            Bubble.Background = Color.Transparent;
            Content.CaptionBackground = Content.PhotoOnly ? Color.Transparent : Message?.SenderId == owner.me?.Id ? ChatOutgoingColor : ChatIncomingColor;
            Footer.Padding = Content.PhotoOnly ? new Thickness(6, 2) : new Thickness(0);
            Footer.CornerRadius = 10;
            Footer.Background = Content.PhotoOnly ? Color.FromArgb(155, 0, 0, 0) : Color.Transparent;
            Time.Foreground = Content.PhotoOnly ? Color.FromArgb(235, 255, 255, 255) : Message?.SenderId == owner.me?.Id ? Color.FromRgb(174, 199, 219) : Muted;
            UpdateMediaHover(Bubble.IsMouseOver);
        }
        internal void SetMusicLayout(bool compact = false)
        {
            Bubble.Child = null;
            MusicBackdrop = new Image().StretchMode(compact ? Stretch.Fill : Stretch.UniformToFill).IsHitTestVisible(false);
            MusicBackdrop.Opacity = 0;
            MusicBackdrop.Transitions = [Transition.Create(UIElement.OpacityProperty, 260)];
            var hasCaption = Body.IsVisible;
            musicContent = new Border().Padding(12).Child(Content);
            Bubble.Child = new ChatMusicSurface(musicContent, MusicBackdrop, Content).Initialize();
            Bubble.Background = Raised; Bubble.Padding = new Thickness(0); Bubble.ClipToBounds = true; Bubble.MaxWidth = compact ? 484 : 584;
            Time.Foreground = hasCaption && Message?.SenderId == owner.me?.Id ? Color.FromRgb(174, 199, 219) : Muted;
            Content.CaptionBackground = hasCaption ? Message?.SenderId == owner.me?.Id ? ChatOutgoingColor : ChatIncomingColor : Color.Transparent;
            Content.SetMusic();
        }
        internal void SetCompactMusicLayout()
        {
            SetMusicLayout(compact: true);
        }
        internal void ShowMusicBackdrop(ImageSource? source)
        { if (source == null) return; MusicBackdrop.Source = source; MusicBackdrop.Opacity = 1; }
        internal void UpdateMediaHover(bool hovered)
        {
            Footer.IsVisible = !MediaLoading || Spinner.IsVisible || Error.IsVisible;
            Footer.Opacity = 1;
        }
        internal void ResetMediaLayout()
        {
            if (musicContent != null)
            {
                musicContent.Child = null; Bubble.Child = null; Bubble.Child = Content; musicContent = null;
                MusicBackdrop.Source = null;
            }
            Content.Reset(); MediaLoading = false; Footer.IsVisible = true; Bubble.MaxWidth = 520; Bubble.Padding = new Thickness(10, 6); Bubble.ClipToBounds = false;
            Bubble.Background = Message?.SenderId == owner.me?.Id ? ChatOutgoingColor : ChatIncomingColor;
            Footer.Background = Color.Transparent; Footer.Padding = new Thickness(0); Footer.Opacity = 1;
        }
        internal void Clear()
        {
            highlight.Stop(); Bound = false; DateBadge.Opacity = 1; Content.StopResize();
            ItemKey = null; MediaKey = null; MediaResolved = FollowMediaResize = false; ++MediaGeneration;
            Load?.Cancel(); Load?.Dispose(); Load = null;
            Motion.Finish(); Spinner.IsActive = false;
            owner.StopChatAnimations(Attachments);
            Attachments.Clear(); Attachments.IsVisible = false; Message = null; Body.Text = ""; Open.IsEnabled = true;
            ResetMediaLayout();
        }
    }
}
