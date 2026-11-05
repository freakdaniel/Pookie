using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Text;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "PookieTextLayoutTransform")]
    private static extern ref Func<TextLayoutRequest, TextLayoutRequest>? CompatibilityEmojiTransform(TextLayoutRequest? declaringType);

    private async Task VerifyPublicEmojiEditorAsync()
    {
        var compatibility = CompatibilityEmojiTransform(null);
        var extension = chatEmojiEditor ?? throw new InvalidOperationException("Public emoji editor extension is missing.");
        var draft = chatInput.Text;
        var selection = (chatInput.SelectionStart, chatInput.SelectionLength);
        try
        {
            // Disable the fallback so it cannot make a broken public pipeline pass.
            CompatibilityEmojiTransform(null) = null;
            foreach (var emoji in new[] { "👨‍👩‍👧‍👦", "👋🏽", "🇨🇦", "😀" })
            {
                chatInput.Text = emoji;
                await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && !chatInput.IsArrangeDirty);
                var advance = chatInput.GetCharRectInWindow(emoji.Length).X - chatInput.GetCharRectInWindow(0).X;
                if (Math.Abs(advance - chatInput.FontSize * 1.3) > 1 / Window.DpiScale)
                    throw new InvalidOperationException($"Public editor emoji pipeline lost its artwork metrics: {emoji}, advance={advance}.");
                chatInput.SelectAll();
                if (chatInput.SelectedText != emoji) throw new InvalidOperationException("Public emoji presentation changed Unicode selection.");
            }
            chatInput.Text = "a👨‍👩‍👧‍👦b";
            chatInput.CaretPosition = chatInput.Text.Length - 1;
            chatInput.Focus();
            RouteSelectableTextKey(Key.Backspace);
            if (chatInput.Text != "ab") throw new InvalidOperationException("Public editor backspace split an emoji sequence.");
            chatInput.Undo();
            if (chatInput.Text != "a👨‍👩‍👧‍👦b") throw new InvalidOperationException("Public editor undo changed the Unicode sequence.");
            var text = "ฏ" + new string('\u0e4e', 256) + "😀";
            chatInput.Text = text;
            var scan = new TextElementScanContext(chatInput.Document, 0);
            var emojiOffset = text.Length - 2;
            var emojiObject = extension.ConstructElement(in scan, emojiOffset)?.Object
                ?? throw new InvalidOperationException("Public editor generator omitted the emoji.");
            var context = new TextLineTransformContext(new(0, 0, text.Length, text.Length), text.AsMemory(),
                new(chatInput.FontFamily, chatInput.FontSize), IdentityTextOffsetMap.Instance);
            var inlines = new List<InlineRun> { new(emojiOffset, 2, emojiObject, true) };
            extension.Transform(in context, new List<GeometryStyleRun>(), inlines);
            if (!inlines.Any(run => run.Object.Measure().Width == 0) ||
                !inlines.Any(run => text.Substring(run.Position, run.Length) == "😀"))
                throw new InvalidOperationException("Public editor transformer omitted emoji or combining-mark protection.");
            chatInput.Text = text;
            await WaitForLikedLayoutAsync(() => !chatComposerSurface.IsResizing && !chatInput.IsArrangeDirty);
            chatInput.SelectAll();
            if (chatInput.SelectedText != text) throw new InvalidOperationException("Combining-mark protection rewrote the document.");
            CaptureUiPreview("chat-emoji-public-pipeline");
            Console.WriteLine("PUBLIC_EMOJI_EDITOR_OK: compatibility hook disabled; family, skin tone, flag, Unicode selection and combining-mark protection use public extensions");
        }
        finally
        {
            CompatibilityEmojiTransform(null) = compatibility;
            chatInput.Text = draft;
            chatInput.Select(selection.SelectionStart, selection.SelectionLength);
        }
    }
}
