using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyUserLinkFaviconsAsync()
    {
        var original = userProfileState!;
        var missing = Icons.Source("globe");
        Image[] LinkIcons(StackPanel host)
        {
            var images = new List<Image>();
            VisualTree.Visit(host, element => { if (element is Image image) images.Add(image); });
            return images.ToArray();
        }
        try
        {
            userProfileState = original with { Links = [.. original.Links,
                new UserWebProfile("https://example.com/another", "Другой раздел", null),
                new UserWebProfile("https://favicon-missing.invalid/path", "Без иконки", null)] };
            RenderUserProfile();
            var sidebar = LinkIcons(userSidebarLinks); var about = LinkIcons(userAboutLinks);
            await WaitForLikedLayoutAsync(() => sidebar.Length == 3 && about.Length == 3 &&
                sidebar[0].Source is ImageSource && sidebar[1].Source is ImageSource && about[0].Source is ImageSource);
            if (!ReferenceEquals(sidebar[0].Source, sidebar[1].Source) || !ReferenceEquals(sidebar[0].Source, about[0].Source) ||
                !ReferenceEquals(sidebar[2].Source, missing) || !ReferenceEquals(about[2].Source, missing))
                throw new InvalidOperationException("Profile links did not share cached site icons or preserve the missing-icon fallback.");
            foreach (var image in sidebar.Concat(about))
                if (image.Parent is not Border { ClipToBounds: true, Width: 14, Height: 14, CornerRadius: 7 })
                    throw new InvalidOperationException("Profile favicon lost its circular miniature styling.");
            Console.WriteLine("USER_FAVICONS_OK: circular external-link icons in sidebar/about, shared cache per site and unavailable-icon fallback");
        }
        finally { userProfileState = original; RenderUserProfile(); }
    }

    private sealed class UserFaviconFixtureHandler(byte[] icon) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(request.RequestUri!.Host == "example.com"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(icon) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
