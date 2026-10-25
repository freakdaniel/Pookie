using Aprillz.MewUI;
using Pookie.App.Messages;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatFooterUiAsync(ChatMessage target)
    {
        const string wrapped = "Длинное сообщение проверяет переносы строк и положение времени. Мы оставляем текст на полной ширине пузыря, а отметку отправки ставим рядом с последними словами, если для неё хватает места. Всё готово. Подтверждаю.";
        var originals = chatMessages.ToArray(); var dpi = Window.Dpi;
        var stamp = DateTimeOffset.UtcNow.AddSeconds(1);
        ChatMessage[] fixtures = [new("footer-wrapped",42,wrapped,stamp),
            new("footer-break",90,wrapped+"\nГотово 👨‍👩‍👧‍👦",stamp.AddSeconds(1)),
            new("footer-tight",42,wrapped+"\n"+new string('Ш',36),stamp.AddSeconds(2)),
            new("footer-quote",42,ChatProtocol.Reply(42,90,target.Id,"Сохранённая цитата",wrapped),stamp.AddSeconds(3))];
        try
        {
            chatMessages.Clear(); chatMessages.AddRange(fixtures); RefreshChatTimeline(); RequestChatBottom();
            foreach (var scale in new uint[]{96,110,144,192})
            {
                typeof(Window).GetMethod("SetDpi",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(Window,[scale]);
                Window.PerformLayout();
                await WaitForLikedLayoutAsync(() => !chatScrollBottom && fixtures.All(m => ChatRows.Any(r => r.Bound && r.Message?.Id == m.Id && r.Body.ActualWidth > 0)));
                await WaitForLoginFrameAsync();
                CaptureUiPreview("chats-last-line-time-"+scale);
                foreach (var message in fixtures)
                {
                    var row = ChatRows.Single(r => r.Bound && r.Message?.Id == message.Id);
                    var last = row.Body.LastLineAt(row.Body.ActualWidth) ?? throw new InvalidOperationException("Message has no text line geometry.");
                    if (row.Content.InlineFooter)
                    {
                        if (Math.Abs(row.Footer.Bounds.Bottom-row.Body.Bounds.Bottom)>2 || row.Footer.Bounds.X < row.Body.Bounds.X+last.Bounds.Right+6)
                            throw new InvalidOperationException("Timestamp overlaps the final message line at DPI "+scale+": "+message.Id);
                    }
                    else if (row.Footer.Bounds.Y < row.Body.Bounds.Bottom)
                        throw new InvalidOperationException("Timestamp without room on the last line did not move below the text.");
                    if (message.Id != "footer-tight" && !row.Content.InlineFooter)
                        throw new InvalidOperationException("Multiline message with a short final line wastes a separate timestamp row at DPI "+scale+": "+message.Id+$", text={row.Body.Bounds}, last={last.Bounds}, footer={row.Footer.Bounds}");
                }
            }
        }
        finally
        {
            typeof(Window).GetMethod("SetDpi",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(Window,[dpi]);
            chatMessages.Clear(); chatMessages.AddRange(originals); RefreshChatTimeline(); RequestChatBottom();
        }
        await WaitForLikedLayoutAsync(() => !chatScrollBottom);
    }
}
