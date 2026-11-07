using System.Text.Json.Serialization;

namespace Pookie.SoundCloud;

public sealed record SoundCloudUser
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public long? FollowersCount { get; init; }
    public long? TrackCount { get; init; }
    public string? PermalinkUrl { get; init; }
    public string? FullName { get; init; }
    public string? Description { get; init; }
    public string? City { get; init; }
    public string? CountryCode { get; init; }
    public long? FollowingsCount { get; init; }
    public long? LikesCount { get; init; }
    public long? PlaylistCount { get; init; }
    public bool Verified { get; init; }
    public UserBadges? Badges { get; init; }
    public CreatorSubscription? CreatorSubscription { get; init; }
    public CreatorSubscription[]? CreatorSubscriptions { get; init; }
    [JsonIgnore] public bool IsVerified => Verified || Badges?.Verified == true;
    [JsonIgnore] public bool HasArtistPro => Badges?.ProUnlimited == true || IsArtistPro(CreatorSubscription) || CreatorSubscriptions?.Any(IsArtistPro) == true;
    private static bool IsArtistPro(CreatorSubscription? subscription) => subscription?.Product?.Id == "creator-pro-unlimited";
    public UserVisuals? Visuals { get; init; }
    [JsonIgnore] public string? BannerUrl => Visuals?.Visuals?.FirstOrDefault(visual => !string.IsNullOrWhiteSpace(visual.VisualUrl))?.VisualUrl;
}

public sealed record UserBadges(bool Pro = false, bool ProUnlimited = false, bool Verified = false, bool CreatorMidTier = false);
public sealed record CreatorSubscription(CreatorProduct? Product);
public sealed record CreatorProduct(string? Id);

public sealed record UserWebProfile(string Url, string? Title, string? Service);
public sealed record UserVisual(string? VisualUrl);
public sealed record UserVisuals(UserVisual[]? Visuals);
