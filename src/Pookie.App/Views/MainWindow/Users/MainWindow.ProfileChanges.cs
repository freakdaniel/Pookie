using Pookie.SoundCloud;

namespace Pookie.App;

internal sealed partial class MainWindow
{
    private static LibraryPage ChangeMembership(LibraryPage page, LibraryItem item, bool included)
    {
        bool Matches(LibraryItem other) => item.Playlist is { Id: > 0 } playlist
            ? other.Playlist?.Id == playlist.Id : other.Key == item.Key;
        if (!included && !page.Items.Any(Matches)) return page;
        var remaining = page.Items.Where(other => !Matches(other));
        return page with { Items = (included ? remaining.Prepend(item) : remaining).ToArray() };
    }

    private void ObserveOwnLikes(LibraryPage? previous, LibraryPage updated)
    {
        var tracks = updated.Items.Select(item => item.Track).OfType<SoundCloudTrack>().ToArray();
        var playlists = updated.Items.Select(item => item.Playlist).OfType<SoundCloudPlaylist>().Select(item => item.Id);
        if (updated.NextHref == null)
        {
            // Only a complete list proves a previously observed item has disappeared.
            if (previous != null)
            {
                likedIds.ExceptWith(previous.Items.Where(item => item.Track != null).Select(item => item.Track!.Id));
                likedPlaylistIds.ExceptWith(previous.Items.Where(item => item.Playlist != null).Select(item => item.Playlist!.Id));
            }
            if (libraryLikes != null) libraryLikes = new(tracks, null);
        }
        likedIds.UnionWith(tracks.Select(track => track.Id));
        likedPlaylistIds.UnionWith(playlists);
        UpdateLikeState(); RefreshCollectionActions(); RefreshHomeShortcuts();
    }

    private void UpdateProfileMembership(long userId, UserSection section, LibraryItem item, bool included)
    {
        if (FindCachedProfile(userId) is not { } profile) return;
        var state = profile.State;
        if (state.Sections.TryGetValue(section, out var page))
            profile.State = state with { Sections = new(state.Sections) { [section] = ChangeMembership(page, item, included) } };
        MarkProfileChanged(profile, section, patched: page != null);
    }

    private void MarkProfileChanged(CachedProfile profile, UserSection section, bool patched)
    {
        var previous = profile.Reads.GetValueOrDefault(section);
        profile.Reads[section] = previous with { Revision = previous.Revision + 1,
            Checked = patched ? previous.Checked : DateTimeOffset.MinValue };
        if (page.Value != Page.UserProfile || cachedProfile != profile || !VisibleProfileSections.Contains(section)) return;
        if (patched) RenderUserProfile();
        else Run(RefreshVisibleProfileAsync);
    }

    private void UpdateProfileRepost(long userId, LibraryItem item, bool reposted)
    {
        item = item with { ActivityType = item.Track != null ? "track-repost" : "playlist-repost",
            ActivityAt = DateTimeOffset.UtcNow.ToString("O") };
        UpdateProfileMembership(userId, UserSection.Reposts, item, reposted);
        UpdateProfileMembership(userId, UserSection.Overview, item, reposted);
    }

    private void UpdateProfileFollow(SoundCloudUser owner, SoundCloudUser artist, bool following)
    {
        var delta = following ? 1 : -1;
        if (FindCachedProfile(owner.Id) is { } own)
        {
            var user = own.State.User;
            if (user.FollowingsCount is { } count)
                own.State = own.State with { User = user with { FollowingsCount = Math.Max(0, count + delta) } };
            UpdateProfileMembership(owner.Id, UserSection.Following, UserItem(artist), following);
        }
        if (FindCachedProfile(artist.Id) is { } target)
        {
            var user = target.State.User;
            if (user.FollowersCount is { } count)
                target.State = target.State with { User = user with { FollowersCount = Math.Max(0, count + delta) } };
            UpdateProfileMembership(artist.Id, UserSection.Followers, UserItem(owner), following);
        }
        if (page.Value == Page.UserProfile && (userProfileState?.User.Id == owner.Id || userProfileState?.User.Id == artist.Id))
            RenderUserProfile();
    }

    private void UpdateProfileComment(long userId, SoundCloudTrack track, SoundCloudComment comment, bool posted)
    {
        if (FindCachedProfile(userId) is not { } profile) return;
        var page = profile.State.Comments;
        if (page != null)
        {
            var remaining = page.Comments.Where(entry => entry.Comment.Id != comment.Id);
            profile.State = profile.State with { Comments = page with {
                Comments = (posted ? remaining.Prepend(new(comment, track)) : remaining).ToArray() } };
        }
        MarkProfileChanged(profile, UserSection.Comments, patched: page != null);
    }

    private void InvalidateProfilePlaylist(long playlistId)
    {
        // A playlist can appear on its owner's page and in other users' likes or reposts.
        var profiles = navigationHistory.Select(entry => entry.Snapshot?.UserProfile).Append(cachedProfile)
            .OfType<CachedProfile>().Distinct().ToArray();
        foreach (var profile in profiles)
        {
            var sections = profile.State.Sections.Where(pair => pair.Value.Items.Any(item => item.Playlist?.Id == playlistId))
                .Select(pair => pair.Key);
            if (profile.State.User.Id == me?.Id)
                sections = sections.Concat([UserSection.Overview, UserSection.Playlists, UserSection.Albums]);
            foreach (var section in sections.Distinct().ToArray())
                MarkProfileChanged(profile, section, patched: false);
        }
    }
}
