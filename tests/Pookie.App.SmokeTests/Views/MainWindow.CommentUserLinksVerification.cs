using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyCommentUserLinksAsync()
    {
        var names = new List<TextBlock>();
        VisualTree.Visit(trackDetailComments, element =>
        {
            if (element is TextBlock { Text: "Слушатель" or "Другой слушатель" } text) names.Add(text);
        });
        if (names.Count != 2) throw new InvalidOperationException("Comment/reply author fixtures are missing.");
        var offset = trackDetailScroll.VerticalOffset;
        MoveActionPointer(new Point(1, 1));
        try
        {
            foreach (var name in names)
            {
                trackDetailScroll.SetScrollOffsets(0, trackDetailScroll.VerticalOffset + name.Bounds.Y - 400);
                await WaitForLikedLayoutAsync(() => name.Bounds.Y is > 200 and < 600 && !trackDetailScroll.IsArrangeDirty);
                var resting = name.Foreground;
                var hovered = resting.Lerp(Color.White, .4);
                if (resting == Color.Black || resting == hovered)
                    throw new InvalidOperationException("Comment author did not receive its normal theme colour.");
                var frames = new HashSet<Color>();
                void Sample() => frames.Add(name.Foreground);
                void Hover() => MoveActionPointer(new Point(name.Bounds.X + name.ActualWidth / 2, name.Bounds.Y + name.ActualHeight / 2));
                Window.FrameRendered += Sample;
                try
                {
                    Hover();
                    if (name.Foreground != resting) throw new InvalidOperationException("Comment author hover colour snapped.");
                    await WaitForLikedLayoutAsync(() => name.IsMouseOver && name.Foreground == hovered);
                    CheckFrames(); frames.Clear();
                    MoveActionPointer(new Point(1, 1)); Window.FocusManager.ClearFocus();
                    if (name.Foreground != hovered) throw new InvalidOperationException("Comment author resting colour snapped.");
                    await WaitForLikedLayoutAsync(() => !name.IsMouseOver && name.Foreground == resting);
                    CheckFrames();
                    // Re-enter during the return transition, then leave again.
                    Hover();
                    await WaitForLikedLayoutAsync(() => name.Foreground != resting && name.Foreground != hovered);
                    MoveActionPointer(new Point(1, 1));
                    await WaitForLikedLayoutAsync(() => name.Foreground != resting && name.Foreground != hovered);
                    var intermediate = name.Foreground; Hover();
                    if (name.Foreground != intermediate) throw new InvalidOperationException("Re-entering an author link restarted its animation.");
                    await WaitForLikedLayoutAsync(() => name.Foreground == hovered);
                    MoveActionPointer(new Point(1, 1));
                    await WaitForLikedLayoutAsync(() => name.Foreground == resting);
                }
                finally { Window.FrameRendered -= Sample; }

                void CheckFrames()
                {
                    if (frames.Count(color => color != resting && color != hovered) < 3 || frames.Contains(Color.Black))
                        throw new InvalidOperationException("Comment author colour did not animate smoothly between its theme and hover colours.");
                }
            }
            Console.WriteLine("COMMENT_USER_LINKS_OK: themed comment/reply names, hover/leave colour frames and interrupted transitions restore the original colour");
        }
        finally
        {
            MoveActionPointer(new Point(1, 1)); trackDetailScroll.SetScrollOffsets(0, offset);
            await WaitForLoginFrameAsync();
        }
    }
}
