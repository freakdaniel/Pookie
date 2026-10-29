using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private void VerifyGlassCachePixels()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var rendering = Window.GraphicsFactory.AcquireBackgroundRenderScope();
        var maximumMean = 0d; var maximumSampling = 0;
        foreach (var scale in new[] { 1d, 110 / 96d, 1.5, 2 })
        foreach (var size in new[] { new Size(36,36), new Size(113.25,36), new Size(200,48) })
        foreach (var emphasis in new[] { .35, 1d })
        {
            using var sheen = new GlassSheenRaster();
            var width = (int)Math.Ceiling(size.Width * scale); var height = (int)Math.Ceiling(size.Height * scale);
            using var ink = new WriteableBitmap(width,height);
            using (var write = ink.LockForWrite()) GlassSheenRaster.FillPixels(write.PixelsBgra32,width,height,emphasis);
            using var image = BitmapImage.Create(Window.GraphicsFactory,ink);
            var corners = new CornerRadius(18,4,4,18);
            foreach (var phase in new[] { 0d, .4, .8 })
            {
                var bounds = new Rect(4 + phase / scale,4 + phase / scale,size.Width,size.Height);
                var outputWidth = (int)Math.Ceiling((size.Width+12)*scale);
                var outputHeight = (int)Math.Ceiling((size.Height+12)*scale);
                byte[] Draw(bool cached)
                {
                    using var surface = Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(outputWidth,outputHeight,scale));
                    using var context = Window.GraphicsFactory.CreateContext(surface);
                    context.BeginFrame(surface); context.Clear(Color.FromRgb(24,30,40));
                    if(cached) sheen.Draw(context,bounds,corners,emphasis,scale);
                    else
                    {
                        // Original drawing path: dithered fill plus a native
                        // gradient stroke in absolute coordinates, independent
                        // of the reused local geometry and gradient endpoints.
                        context.Save(); context.SetClipPath(GlassSheen.Shape(bounds,corners));
                        context.DrawImage(image,bounds); context.Restore();
                        var inset = new Rect(bounds.X+.5,bounds.Y+.5,bounds.Width-1,bounds.Height-1);
                        context.DrawPath(GlassSheen.Shape(inset,new CornerRadius(17.5,3.5,3.5,17.5)),
                            new Pen(new LinearGradientBrush(new Point(bounds.X,bounds.Y),new Point(bounds.X,bounds.Bottom),
                                [new(0,Color.FromArgb((byte)(28+emphasis*16),255,255,255)),new(.55,Color.FromArgb(9,255,255,255)),new(1,Color.FromArgb(4,255,255,255))]),1));
                    }
                    context.EndFrame();
                    return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
                }
                var expected = Draw(false); var actual = Draw(true); double error=0;var sampling=0;
                for(var y=0;y<outputHeight;y++)for(var x=0;x<outputWidth;x++)
                {
                    var offset=(y*outputWidth+x)*4; error+=Math.Abs(actual[offset]-expected[offset]);
                    var low=255;var high=0;
                    for(var sy=Math.Max(0,y-1);sy<=Math.Min(outputHeight-1,y+1);sy++)
                    for(var sx=Math.Max(0,x-1);sx<=Math.Min(outputWidth-1,x+1);sx++)
                    {var value=expected[(sy*outputWidth+sx)*4];low=Math.Min(low,value);high=Math.Max(high,value);}
                    sampling=Math.Max(sampling,Math.Max(low-actual[offset],actual[offset]-high));
                }
                var mean=error/(outputWidth*outputHeight);maximumMean=Math.Max(maximumMean,mean);maximumSampling=Math.Max(maximumSampling,sampling);
                // Float transforms can differ within one physical edge pixel,
                // while flat interiors must agree.
                if(mean>.75 || sampling>4)
                    throw new InvalidOperationException($"Cached glass edge changed its shape: dpi={scale}, size={size}, phase={phase}, mean={mean}, sampling={sampling}.");
            }
        }
        Console.WriteLine($"GLASS_EDGE_CACHE_PIXELS_OK: native stroke comparison, connected corners, moving fractional pixels and 96/110/144/192 DPI; mean<={maximumMean:F3}, sampling<={maximumSampling}");
        VerifyChatFadePixels();
        VerifyChatProfileGradientPixels();
        VerifyButtonGraphicsState();
    }

    private void VerifyButtonGraphicsState()
    {
        foreach (var variant in new[] { TrackButtons.Text, TrackButtons.Glass })
        {
            using var button=(MaterialTrackButton)TrackButtons.Action(new TextBlock().Text("Проверка"),()=>{},variant,36);
            button.Measure(new Size(160,36));button.Arrange(new Rect(10,10,160,36));
            foreach (var (available,pending) in new[] { (true,false),(false,false),(false,true) })
            {
                button.Availability(available,pending);
                using var surface=Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(240,80,110/96d));
                using var context=Window.GraphicsFactory.CreateContext(surface);
                context.BeginFrame(surface);context.Clear(Color.FromRgb(24,24,24));
                context.Translate(2,3);context.SetClip(new Rect(0,0,200,60));context.GlobalAlpha=.73f;
                var transform=context.GetTransform();var clip=context.GetClipBoundsLocal();var alpha=context.GlobalAlpha;
                button.Render(context);
                if(context.GetTransform()!=transform || context.GetClipBoundsLocal()!=clip || context.GlobalAlpha!=alpha)
                    throw new InvalidOperationException($"Button leaked graphics state: variant={variant}, available={available}, pending={pending}.");
                context.EndFrame();
            }
        }
        Console.WriteLine("BUTTON_GRAPHICS_STATE_OK: ordinary/glass buttons preserve transform, clip and alpha when enabled, disabled or pending");
    }

    private void VerifyChatProfileGradientPixels()
    {
        var maximum = 0;
        foreach (var scale in new[] { 1d, 110 / 96d, 1.5, 2 })
        {
            using var profile = new ChatProfileBackdrop(Color.FromRgb(30,30,34));
            profile.Measure(new Size(113.25,300));
            profile.Arrange(new Rect(.4,.8,113.25,300));
            var bounds = profile.Bounds;
            var width = (int)Math.Ceiling(bounds.Width * Math.Max(scale,profile.GetDpi()/96d));
            const int rows = 96;
            using var ink = new WriteableBitmap(width,rows,clear:false,hasAlpha:true);
            using var image = BitmapImage.Create(Window.GraphicsFactory,ink);
            foreach (var gradient in new[] {
                new PlayerGradient(new(8,46,91),new(97,154,218),new(173,43,25)),
                new PlayerGradient(new(234,218,191),new(12,38,17),new(81,71,145)) })
            {
                // Independent reference: original straight-alpha raster and
                // per-frame coverage calculation, then the original upload.
                using (var write = ink.LockForWrite())
                for (var x = 0; x < width; x++)
                {
                    var color = gradient.Sample((x+.5)/width);
                    for (var y = 0; y < rows; y++)
                    {
                        var fade = Math.Clamp((y/(rows-1d)-.45)/.55,0,1);
                        fade = fade*fade*(3-2*fade);
                        var grain = (float)PlayerGradient.Noise(x,y);var i=(y*width+x)*4;
                        write.PixelsBgra32[i]=(byte)(color.Z+grain);
                        write.PixelsBgra32[i+1]=(byte)(color.Y+grain);
                        write.PixelsBgra32[i+2]=(byte)(color.X+grain);
                        write.PixelsBgra32[i+3]=(byte)Math.Clamp((1-fade)*255+grain,0,255);
                    }
                }
                typeof(ChatProfileBackdrop).GetField("current",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(profile,gradient);
                var draw = typeof(ChatProfileBackdrop).GetMethod("RenderGlassBackground",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                byte[] Draw(bool actual)
                {
                    using var target=Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(
                        (int)Math.Ceiling((bounds.Right+2)*scale),(int)Math.Ceiling((bounds.Bottom+2)*scale),scale));
                    using var context=Window.GraphicsFactory.CreateContext(target);
                    context.BeginFrame(target);context.Clear(Color.FromRgb(30,30,34));
                    if(actual) draw.Invoke(profile,[context]);
                    else
                    {
                        context.Save();context.SetClipPath(GlassSheen.Shape(bounds,new CornerRadius(profile.CornerRadius)));
                        context.DrawImage(image,new Rect(bounds.X,bounds.Y,bounds.Width,270));context.Restore();
                    }
                    context.EndFrame();return ((ICpuPixelSurface)target).GetReadOnlyPixelSpan().ToArray();
                }
                var expected=Draw(false);var actual=Draw(true);
                for(var i=0;i<expected.Length;i++)maximum=Math.Max(maximum,Math.Abs(expected[i]-actual[i]));
            }
        }
        if(maximum!=0)throw new InvalidOperationException($"Profile palette coverage/premultiplication changed pixels: {maximum}.");
        Console.WriteLine("CHAT_PROFILE_GRADIENT_PIXELS_OK: palette updates, fractional bounds and 96/110/144/192 DPI; exact original pixels");
    }

    private void VerifyChatFadePixels()
    {
        var largest = 0;
        foreach(var scale in new[] {1d,110/96d,1.5,2})
        foreach(var phase in new[] {0d,.4,.8})
        foreach(var reverse in new[] {false,true})
        {
            var opaque=Color.FromRgb(30,30,34);var clear=Color.FromArgb(0,30,30,34);
            var start=reverse?clear:opaque;var end=reverse?opaque:clear;
            var bounds=new Rect(4+phase/scale,4+phase/scale,80,12);
            var width=(int)Math.Ceiling(90*scale);var height=(int)Math.Ceiling(24*scale);
            var brush=new LinearGradientBrush(new Point(0,0),new Point(0,12),[new(0,start),new(1,end)]);
            byte[] Draw(bool local)
            {
                using var surface=Window.GraphicsFactory.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(width,height,scale));
                using var context=Window.GraphicsFactory.CreateContext(surface);
                context.BeginFrame(surface);context.Clear(Color.FromRgb(180,100,80));
                if(local)ChatViewport.DrawFade(context,bounds,brush);
                else context.FillRectangle(bounds,new LinearGradientBrush(new Point(bounds.X,bounds.Y),new Point(bounds.X,bounds.Bottom),[new(0,start),new(1,end)]));
                context.EndFrame();return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
            }
            var expected=Draw(false);var actual=Draw(true);
            for(var index=0;index<expected.Length;index++)largest=Math.Max(largest,Math.Abs(actual[index]-expected[index]));
        }
        if(largest>1)throw new InvalidOperationException($"Cached native chat fade changed pixels: max channel error={largest}.");
        Console.WriteLine($"CHAT_FADE_CACHE_PIXELS_OK: top/bottom gradients, fractional motion and 96/110/144/192 DPI; max channel error={largest}");
    }
}
