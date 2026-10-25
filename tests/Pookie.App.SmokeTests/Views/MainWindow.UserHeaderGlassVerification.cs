using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyUserHeaderGlassAsync()
    {
        var glass = userHeaderGlass!;
        var original = userBanner.Source as ImageSource;
        var avatar = userAvatar.Source;
        var palette = userHeader.Backdrop.Target;
        var buttons = new List<MaterialTrackButton>();
        VisualTree.Visit(userHeader, element => { if (element is MaterialTrackButton button) buttons.Add(button); });
        if (buttons.Count != 3) throw new InvalidOperationException("Profile header lost one of its three glass actions");
        var follow = buttons.Single(button => button.Content is StackPanel);
        byte[] Read(bool blur)
        {
            glass.Prepare();
            var bounds = userHeader.BackgroundLayer.Bounds;
            using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
            using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.CpuPixels((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 1));
            using var context = Window.GraphicsFactory.CreateContext(surface);
            context.BeginFrame(surface); context.Clear(Color.Transparent); context.Translate(-bounds.X, -bounds.Y);
            if (blur) glass.Draw(context, bounds, new CornerRadius(0)); else userHeader.BackgroundLayer.Render(context);
            context.EndFrame();
            var cpu = (ICpuPixelSurface)surface;
            var pixels = cpu.GetReadOnlyPixelSpan();
            var row = new byte[surface.PixelWidth];
            for (var x = 0; x < row.Length; x++) row[x] = pixels[(surface.PixelHeight / 2) * cpu.StrideBytes + x * 4 + 1];
            return row;
        }
        void CheckButtons()
        {
            foreach (var button in buttons)
            {
                var blurred = CaptureSectionButton(button);
                byte[] flat;
                button.AttachGlassBackdrop(null);
                try { flat = CaptureSectionButton(button); }
                finally { button.AttachGlassBackdrop(glass); }
                var difference = blurred.Zip(flat, (left, right) => (double)Math.Abs(left - right)).Average();
                if (difference < 3) throw new InvalidOperationException($"A profile action did not paint its blurred backdrop: {button.StyleName}, difference={difference:F2}");
            }
        }
        try
        {
            const int width = 1240, height = 400;
            var stripes = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                stripes[index] = stripes[index + 1] = stripes[index + 2] = (byte)(x % 40 < 20 ? 255 : 0);
                stripes[index + 3] = 255;
            }
            userHeader.SetBanner(ImageSource.FromBgraPixels(width, height, stripes, hasAlpha: false));
            await WaitForLikedLayoutAsync(() => !userHeader.Revealing && !userHeader.Backdrop.Running);
            var raw = Read(false); var blurred = Read(true);
            int Edge(byte[] row) => Enumerable.Range(20, row.Length - 41).Max(x => Math.Abs(row[x + 1] - row[x]));
            if (!glass.HasBlurredArtwork || Edge(raw) < 30 || Edge(blurred) >= Edge(raw) * .7 || blurred.Max() - blurred.Min() < 10)
                throw new InvalidOperationException($"Profile glass did not soften banner edges: raw={Edge(raw)}, blurred={Edge(blurred)}");
            CheckButtons(); CaptureUiPreview("user-profile-glass-actions");
            TrackButtons.SetConfirmed(follow, true, TrackButtons.Glass);
            await WaitForLikedLayoutAsync(() => follow.Background == TrackButtons.ConfirmedSurface);
            CheckButtons(); CaptureUiPreview("user-profile-glass-following");
            userAvatar.Source = ImageSource.FromBgraPixels(1, 1, [0, 255, 0, 255], hasAlpha: false);
            glass.Invalidate();
            if (!Read(true).SequenceEqual(blurred)) throw new InvalidOperationException("Profile glass sampled the avatar or foreground content instead of the header background");
            var snapshots = 0;
            void Changed() => snapshots++;
            glass.Changed += Changed;
            try { Read(true); Read(true); if (snapshots != 0) throw new InvalidOperationException("Stable header glass kept rebuilding its snapshot"); }
            finally { glass.Changed -= Changed; }
            userHeader.SetBanner(null);
            userHeader.SetPalette(new(Color.FromRgb(80, 15, 15), Color.FromRgb(80, 15, 15), Color.FromRgb(80, 15, 15)));
            await WaitForLikedLayoutAsync(() => !userHeader.Backdrop.Running);
            var red = Read(true);
            userHeader.SetPalette(new(Color.FromRgb(15, 75, 15), Color.FromRgb(15, 75, 15), Color.FromRgb(15, 75, 15)));
            await WaitForLikedLayoutAsync(() => userHeader.Backdrop.Running && userHeader.Backdrop.BlendAmount > .25 && userHeader.Backdrop.BlendAmount < .9);
            var middle = Read(true);
            await WaitForLikedLayoutAsync(() => !userHeader.Backdrop.Running);
            var green = Read(true);
            if (green.Average(value => (double)value) - red.Average(value => (double)value) < 40 ||
                middle.Average(value => (double)value) <= red.Average(value => (double)value) + 2 ||
                middle.Average(value => (double)value) >= green.Average(value => (double)value) - 2)
                throw new InvalidOperationException("Profile glass froze during the fallback palette transition");
        }
        finally
        {
            userAvatar.Source = avatar; userHeader.SetPalette(palette); userHeader.SetBanner(original); RefreshFollowingButtons();
            await WaitForLikedLayoutAsync(() => !userHeader.Revealing && !userHeader.Backdrop.Running);
            glass.Prepare();
        }
        Console.WriteLine("USER_HEADER_GLASS_OK: pixel-verified banner blur, all three controls including confirmed follow, no avatar/text sampling, cached stable snapshots and animated fallback palette updates");
    }
}
