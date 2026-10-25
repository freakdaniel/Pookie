using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private async Task VerifyUserProfileBadgesAsync()
    {
        var original = userProfileState!;
        // Every bundled flag must be parseable offline, including the expanded <use> flags.
        var codes = typeof(Icons).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("Pookie.App.Assets.Flags.") && name.EndsWith(".svg"))
            .Select(name => name.Split('.')[^2]).ToArray();
        if (codes.Length < 249 || codes.Any(code => Icons.CountryFlag(code) == null) ||
            Icons.CountryFlag(null) != null || Icons.CountryFlag("zz") != null || Icons.CountryFlag("../ca") != null ||
            !ReferenceEquals(Icons.CountryFlag("ca"), Icons.CountryFlag(" CA ")))
            throw new InvalidOperationException("Country flags are incomplete, unsafe, not cached or could not be parsed offline");
        foreach (var width in new[] { 1000, 1600, 1160 })
        {
            Window.WindowSize = WindowSize.Resizable(width, 900, minWidth: 1000, minHeight: 680);
            userProfileState = original with { User = original.User with {
                Username = "Carhartt Work in Progress — очень длинное имя исполнителя с новым релизом",
                CountryCode = "DE", Verified = false, Badges = new(ProUnlimited: true, Verified: true) } };
            RenderUserProfile();
            await WaitForLikedLayoutAsync(() => Math.Abs(Window.ClientSize.Width - width) < 1 && !userHero.IsArrangeDirty && userCountryFlag.ActualWidth > 0);
            if (!userVerified.IsVisible || !userCountryFlag.IsVisible || !userArtistPro.IsVisible ||
                userVerified.Bounds.X < userName.Bounds.Right + 7 || userCountryFlag.Bounds.X < userVerified.Bounds.Right + 7 ||
                userCountryFlag.Bounds.Right > userHero.Bounds.Right - 20 ||
                Math.Abs(SettingsCenter(userCountryFlag).Y - SettingsCenter(userVerified).Y) > .5 ||
                Math.Abs(SettingsCenter(userVerified).Y - SettingsCenter(userName).Y) > .5)
                throw new InvalidOperationException("Profile name, verification and country badges collided, overflowed or lost vertical alignment");
            CaptureUiPreview("user-profile-badges-" + width);
        }
        foreach (var code in new[] { "ca", "cn", "gb", "in", "eu", "br", "us", "jp", "fr" })
        {
            userProfileState = original with { User = original.User with { CountryCode = code, Verified = false,
                Badges = new(ProUnlimited: true), CreatorSubscription = null, CreatorSubscriptions = null } };
            RenderUserProfile();
            await WaitForLikedLayoutAsync(() => !userHero.IsArrangeDirty && userCountryFlagImage.Source != null);
            if (userVerified.IsVisible || !userArtistPro.IsVisible || !userCountryFlag.IsVisible ||
                Math.Abs(userCountryFlag.Bounds.X - userName.Bounds.Right - 8) > 1)
                throw new InvalidOperationException("An unverified Artist Pro inherited verification or left a vacant badge slot");
            CaptureUiPreview("user-profile-flag-" + code);
        }
        userProfileState = original with { User = original.User with { CountryCode = null, Verified = false, Badges = null,
            CreatorSubscription = new(new("creator-pro-unlimited")) } };
        RenderUserProfile();
        if (userCountryFlag.IsVisible || userVerified.IsVisible || !userArtistPro.IsVisible)
            throw new InvalidOperationException("Artist Pro failed without country/verification metadata");
        CaptureUiPreview("user-profile-artist-pro");
        userProfileState = original with { User = original.User with { CountryCode = "zz", Verified = true, Badges = null,
            CreatorSubscription = null, CreatorSubscriptions = null } };
        RenderUserProfile();
        if (userCountryFlag.IsVisible || !userVerified.IsVisible || userArtistPro.IsVisible)
            throw new InvalidOperationException("A verified free profile inherited Artist Pro or an unknown country flag");
        userProfileState = original; RenderUserProfile();
        await WaitForLikedLayoutAsync(() => !userHero.IsArrangeDirty && userCountryFlag.IsVisible && !userArtistPro.IsVisible);
        Console.WriteLine($"USER_BADGES_UI_OK: {codes.Length} offline SVG flags, independent verification/Artist Pro, long-name truncation and responsive badge alignment");
    }
}
