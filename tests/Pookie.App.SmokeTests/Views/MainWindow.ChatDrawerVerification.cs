using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyChatDrawerStabilityAsync()
    {
        var nativeDpi = Window.Dpi;
        var nativeSize = Window.WindowSize;
        var peer = chatRecipient ?? throw new InvalidOperationException("Drawer fixture needs a selected peer.");
        var avatar = chatHeaderAvatar.Source;
        const string readyUrl = "https://i1.sndcdn.com/drawer-ready-fixture.png";
        var readySource = ImageSource.FromBgraPixels(1,1,[120,180,70,255],hasAlpha:false);
        artworkRequests[readyUrl] = Task.FromResult<ImageSource?>(readySource);
        playerPalettes.Add(readySource,Task.FromResult(PlayerPalette.FromArtwork(readySource)));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sheen = typeof(GlassCard).GetField("sheen",flags)!.GetValue(chatDetailsCard)!;
        var sheenImage = typeof(GlassSheenRaster).GetField("bitmap",flags)!;
        var profileImage = typeof(ChatProfileBackdrop).GetField("raster",flags)!;
        try
        {
            chatRecipient = peer with { AvatarUrl = readyUrl };
            chatHeaderAvatar.Source = readySource;
            var body = chatInfoBody;
            foreach (var requestedWidth in new[] { 1280, 1840, 720 })
            {
                CloseChatDetails();
                await WaitForLikedLayoutAsync(() => !chatColumns.Animating);
                typeof(Window).GetMethod("SetDpi",flags)!.Invoke(Window,[nativeDpi]);
                // This chat-only fixture deliberately exercises the compact
                // mode below the ordinary 760-DIP verification minimum.
                var width=requestedWidth<760 ? requestedWidth : VerificationWindowWidth(requestedWidth);
                Window.WindowSize = WindowSize.Resizable(width,900,minWidth:680,minHeight:600);
                await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width-width)<1);
                foreach (var dpi in new uint[] { 96,110,144,192 })
                {
                    typeof(Window).GetMethod("SetDpi",flags)!.Invoke(Window,[dpi]);
                    Window.PerformLayout();await WaitForLoginFrameAsync();
                    var samples = new List<(double Panel,double Header,double Composer,object? Sheen,object? Profile)>();
                    void Sample()
                    {
                        if (!chatColumns.Animating || chatColumns.Reveal is <= .05 or >= .95) return;
                        samples.Add((Math.Round(chatDetailsCard.ActualWidth*Window.DpiScale,6),
                            Math.Round(chatIdentityCard.ActualWidth*Window.DpiScale,6),
                            Math.Round(chatComposer.ActualWidth*Window.DpiScale,6),
                            sheenImage.GetValue(sheen),profileImage.GetValue(chatProfileBackdrop)));
                    }
                    Window.FrameRendered += Sample;
                    try
                    {
                        OpenChatInfo();
                        if (!ReferenceEquals(body,chatDetailsBody) || chatDetailsTransition.Animating || chatMediaTransition.Animating)
                            throw new InvalidOperationException("Opening a closed profile rebuilt its body or animated an empty outgoing section.");
                        await WaitForLikedLayoutAsync(() => !chatColumns.Animating && !chatProfileBackdrop.Animating);
                        CloseChatDetails();
                        await WaitForLikedLayoutAsync(() => !chatColumns.Animating);
                    }
                    finally { Window.FrameRendered -= Sample; }
                    if (samples.Count < 2 || samples.Select(s=>s.Panel).Distinct().Count()!=1 ||
                        samples.Select(s=>s.Header).Distinct().Count()!=1 || samples.Select(s=>s.Composer).Distinct().Count()!=1)
                        throw new InvalidOperationException($"Moving chat columns changed physical width at {dpi} DPI/{width} DIP: "+
                            string.Join(';',samples.Select(s=>$"{s.Panel}/{s.Header}/{s.Composer}")));
                    var painted = samples.Where(s=>s.Sheen!=null && s.Profile!=null).ToArray();
                    if (painted.Length<2 || painted.Any(s=>!ReferenceEquals(s.Sheen,painted[0].Sheen) || !ReferenceEquals(s.Profile,painted[0].Profile)))
                        throw new InvalidOperationException($"Drawer recreated its glass/profile rasters while moving at {dpi} DPI/{width} DIP.");
                }
            }
            Console.WriteLine("CHAT_DRAWER_PIXELS_STABLE_OK: opening/closing, overlay/docked/compact, 96/110/144/192 DPI, stable reading widths and retained raster identities");
            typeof(Window).GetMethod("SetDpi",flags)!.Invoke(Window,[nativeDpi]);
            await VerifyChatDrawerRebindingAsync(peer);
        }
        finally
        {
            CloseChatDetails();
            chatRecipient = peer;chatHeaderAvatar.Source = avatar;
            artworkRequests[readyUrl] = Task.FromResult<ImageSource?>(null);
            typeof(Window).GetMethod("SetDpi",flags)!.Invoke(Window,[nativeDpi]);
            Window.WindowSize = nativeSize;
        }
    }

    private async Task VerifyChatDrawerRebindingAsync(Pookie.SoundCloud.SoundCloudUser peer)
    {
        const string oldUrl="https://i1.sndcdn.com/drawer-old-fixture.png", newUrl="https://i1.sndcdn.com/drawer-new-fixture.png";
        var oldSource=ImageSource.FromBgraPixels(1,1,[20,30,220,255],hasAlpha:false);
        var newSource=ImageSource.FromBgraPixels(1,1,[210,150,20,255],hasAlpha:false);
        var delayed=new TaskCompletionSource<PlayerPalette>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected=PlayerPalette.FromArtwork(newSource);
        artworkRequests[oldUrl]=Task.FromResult<ImageSource?>(oldSource);
        artworkRequests[newUrl]=Task.FromResult<ImageSource?>(newSource);
        playerPalettes.Add(oldSource,delayed.Task);playerPalettes.Add(newSource,Task.FromResult(expected));
        try
        {
            chatRecipient=peer with { AvatarUrl=oldUrl };chatHeaderAvatar.Source=oldSource;
            OpenChatInfo();await WaitForLikedLayoutAsync(()=>!chatColumns.Animating);
            var body=chatDetailsBody;
            CloseChatDetails();await WaitForLikedLayoutAsync(()=>!chatColumns.Animating);
            chatRecipient=peer with { AvatarUrl=newUrl,Username="Новая привязка" };chatHeaderAvatar.Source=newSource;
            OpenChatInfo();
            delayed.SetResult(PlayerPalette.FromArtwork(oldSource));
            await WaitForLikedLayoutAsync(()=>!chatColumns.Animating && !chatProfileBackdrop.Animating);
            if(!ReferenceEquals(body,chatDetailsBody) || chatProfileBackdrop.Target!=expected ||
                !ReferenceEquals(chatInfoAvatar.Source,newSource) || chatInfoName.Text!="Новая привязка")
                throw new InvalidOperationException("Late palette from a previous opening overwrote the reused profile body.");
            CloseChatDetails();await WaitForLikedLayoutAsync(()=>!chatColumns.Animating);
            ResetChatDetailsContent();
            if(chatInfoAvatar.Source!=null || chatInfoName.Text.Length!=0 || chatInfoUser!=null || chatInfoMediaTransition.Children.Count!=0)
                throw new InvalidOperationException("Reset retained account data in the reusable profile body.");
            Console.WriteLine("CHAT_DRAWER_REBIND_OK: reused body, rejected late palette from the previous opening, account data cleared on reset");
        }
        finally
        {
            delayed.TrySetResult(PlayerPalette.FromArtwork(oldSource));
            artworkRequests[oldUrl]=Task.FromResult<ImageSource?>(null);
            artworkRequests[newUrl]=Task.FromResult<ImageSource?>(null);
        }
    }
}
