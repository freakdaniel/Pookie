# Playlists: creation, reading and editing

## Android sessions

Checked against the saved decompilation of SoundCloud Android
`2026.11.02-release` on 2026-11-06. Live list and track reads were also verified
through the mobile profile; live creation/append writes were not sent by the probe.

`DefaultPlaylistOperations.executeCreatePlaylistApiRequest` sends
`POST https://api-mobile.soundcloud.com/playlists`:

```json
{"playlist":{"title":"Example","public":false},"track_urns":["soundcloud:tracks:123"]}
```

The response is `ApiPlaylistWrapper`: a `playlist` object with a URN and an
owner in `_embedded.user`. It does not promise a track list. Pookie normalizes
these identifiers and reads `/playlists/{urn}/info` to confirm the owner, title,
privacy and seed track before reporting success. A lost POST response or failed
confirmation never repeats creation. A confirmation failure can therefore leave
a created playlist on the server; the client cannot safely infer non-creation.

`ProfileApiMobile.userPlaylists` lists `/users/{urn}/playlists/posted` with
`ApiCollection<ApiPlaylistPost>`. `ApiCollection` paginates through
`_links.next.href`. Pookie validates that every cursor stays on the same API host
and resource, excludes foreign owners, albums and system playlists, and reads
complete track pages to resolve membership in the picker.

Android artwork uses `artwork_url_template` on playlists/tracks and
`avatar_url_template` on users. The mobile adapter resolves `{size}` to
`t500x500`, matching the APK's `ImageUrlBuilder`/`ApiImageSize` contract.
The picker can then use the playlist cover or the existing track-cover fallback.

Appending reads the fresh mobile playlist and sends only
`POST /playlists/{urn}/tracks` with `{"track_urn":"soundcloud:tracks:123"}`.
It rejects unfinished cursor chains and inconsistent numeric IDs/URNs, and skips
tracks already present. It does not replace the full track order.
An explicit terminal Android `_links` object completes available-track pagination
even if `track_count` includes unavailable or removed tracks absent from the list.

Mobile API JSON bodies use `application/json; charset=utf-8`. Protection challenges
use the isolated [mobile challenge flow](MobileChallenge.md), followed by one
replay of the original request. Desktop web session preparation and web mutation
fallback are not used. Challenge replay was accepted on a real mobile like;
playlist mutation contracts are covered by fixtures, without probe writes to the account.

`SoundCloudWebClient.BrowserTransport` is a compatibility alias for `Transport`;
its name does not imply that a call executes in a browser.

### Mobile library and editing

Ordinary playlist pages and their playback queues now use mobile `/info` and
complete `/tracks` pagination, retaining the original card key. System playlists
retain their separate V2 route. Mobile library collections combine
`/users/{urn}/playlists/posted`, `/users/{urn}/albums/posted`, `/users/{urn}/likes`
and `/users/{urn}/reposts`. Subscription pages use `/followings/{urn}/users`.
Likes, playlist likes, reposts and follows derive their button states from these
same mobile collections, exhausting and validating the cursor chains. Track
metadata also maps Android `_embedded.stats` into the shared counters:
`playback_count`, `likes_count`, `reposts_count`, and `comments_count` (mapped to
the shared model's singular `comment_count`). Missing counters remain unknown;
zero is retained as a known value.
Completed snapshots have a bounded 15-second cache, scoped to the account. Mutations
invalidate it before sending and after success; an older in-flight snapshot is
re-read if a mutation intervened.

Contracts checked against `ApiEndpoints`, `DefaultPlaylistWithTracksSyncer`,
`PlaylistApiUpdateObject`, `DefaultPlaylistOperations.removeTrackFromPlaylist`,
`PlaylistImageUpdater` and `ImageUploadBodyCreator` in the APK:

| Operation | Mobile request |
|---|---|
| Remove a track | `DELETE /playlists/{playlistUrn}/tracks/{trackUrn}`, no body |
| Edit metadata | `PUT /playlists/{urn}`, only root fields `description`, `title`, `public`; all track fields omitted |
| Reorder | `PUT /playlists/{urn}`, root fields `track_urns`, `description`, `title`, `genre`, `public`, `tag_list` |
| Delete a playlist | `DELETE /playlists/{urn}`, no body |
| Replace artwork | `PUT /playlists/{urn}/artwork`, JSON `{"image_data":"<base64 JPEG>"}` |

Edits verify the authenticated owner and reject albums/system playlists. They
share the existing local playlist edit semaphore with append. Responses are
confirmed with a fresh mobile read; deletion requires 404/410 or absence from a
complete owner collection when the mobile info service retains stale data. A lost network
response never automatically repeats the write. The challenge path can replay
the originally rejected request once.

Metadata-only saves never serialize `track_urns` or `tracks`, and do not overwrite
genre/tags or other unrelated fields. They remain available when mobile metadata
declares 330 tracks but exposes only 326. Confirmation compares the fresh declared
count and available order before/after, along with the edited fields. A stale UI
track preview does not block metadata editing because it is not sent to the server.

An explicit reorder PUT replaces the complete order, preserving genre and tags.
It still rejects a stale expected order, dropped/added IDs, or a count mismatch
caused by hidden/unavailable tracks. No
server revision/conditional-write contract was found, so the fresh read cannot
eliminate a concurrent edit from another client between the read and PUT.

The page's More menu exposes metadata/privacy editing, JPEG artwork (up to 5 MB)
and a playlist deletion confirmation. Track menus in list/card/waveform views
expose removal and moving up/down. Confirmed changes refresh the page and library;
unconfirmed writes do not optimistically remove data. JPEG selection is decoded
locally before upload; the APK itself similarly sends base64 JPEG data.

Verification commands:

```bash
dotnet test tests/Pookie.SoundCloud.Tests --no-restore
dotnet run --project tests/Pookie.App.SmokeTests -- --collections-ui-smoke-test --mobile-playlist-editing-only
dotnet run --project tests/Pookie.App.SmokeTests -- --mobile-challenge-network-probe
```

The UI verification uses fixtures. The normal network probe only reads account
data. A separate, explicitly selected live fixture probe creates a uniquely named
private two-track test playlist, verifies metadata-only PUT and a full reorder,
then deletes that test resource. On 2026-11-06 both PUT forms were accepted; the
metadata-only request retained count/order, and cleanup confirmed the test resource
was absent from the owner's complete posted collection. The fixture probe did not
edit existing playlists. Artwork upload remains unverified against the live server.

The separate `--playlist-hidden-metadata-live-probe` sends a metadata-only PUT
with the existing title/description/privacy of the affected playlist. On
2026-11-06 it confirmed the live 330/326 case: declared count, available-track
order, title, description, privacy, genre and tags remained unchanged. No track
fields were sent. This probe performs a write with the current field values;
`--playlist-order-network-probe` is the read-only diagnostic.

Read-only investigation of the 330/326 case confirmed that mobile info, V2 full/ID
representations and the `access=playable,preview,blocked` filter all returned the
same ordered 326 IDs. The affected playlist is private. The regional playback pool
is anonymous and can resolve individual public tracks without obtaining private
playlist membership; account OAuth/cookies are not forwarded to that pool.

## Web sessions

Checked against the authenticated SoundCloud web form and its public production
bundles on 2026-11-05. No account data or live write was needed for inspection.

- [Form bundle, module 266](https://a-v2.sndcdn.com/assets/55-528f1bd3.js): title is required, maximum 255 UTF-16 characters; public/private sharing; selected track is the initial playlist content. Private seed tracks default the form to private.
- [Playlist model, module 66](https://a-v2.sndcdn.com/assets/0-9f1dc20a.js): new models use the `playlistCreate` endpoint and JSON save format. `getAttributesToBeSaved` serializes tracks to numeric IDs, not track objects.
- [Creation view, module 2635](https://a-v2.sndcdn.com/assets/0-9f1dc20a.js): builds the playlist with form attributes and the selected track collection, then saves once.

The resulting request is `POST https://api-v2.soundcloud.com/playlists`:

```json
{"playlist":{"title":"Example","sharing":"private","tracks":[123]}}
```

Pookie implements this request natively. The existing browser transport can send
the same bounded command after an explicit site verification challenge. Arbitrary
script execution is not part of the command. Confirmation requires the returned
playlist ID, owner, title, privacy and seed track to match. Losing a POST response
does not trigger an automatic repeat. Creation and insertion of the initial track
are one operation.

## Adding to an existing playlist

[Button module 2630](https://a-v2.sndcdn.com/assets/2-48d6f85f.js) calls the
playlist model's `addSound` and `saveOrder`. The latter sends
`{"playlist":{"tracks":[existingIds...,newId]}}` to `playlistUpdate`, which is
`PUT /playlists/{id}`. The V2 web API does not use the mobile append endpoint
`POST /playlists/{id}/tracks`.

Pookie reads a fresh `representation=full` snapshot first, verifies its owner,
count and every track ID, and preserves order and unavailable/private ID
placeholders. It refuses to overwrite an incomplete list, skips a write if the
track is already present, and serializes playlist edits to avoid two local
read/modify/write operations losing each other's changes. Mobile sessions retain
their separate mobile API append contract.

## Initial membership state

Rechecked against the user's open session and its current public bundles on
2026-11-05: [collection module 918](https://a-v2.sndcdn.com/assets/55-37fbc1b1.js)
retains the own-playlists collection with `neverRelease: true`. The
[button, module 2632](https://a-v2.sndcdn.com/assets/2-f565feec.js) requires
`tracks` and calls `allSoundsAreInPlaylist`; the
[playlist model, module 61](https://a-v2.sndcdn.com/assets/55-37fbc1b1.js)
implements `containsSoundId` by lookup in `soundsCollection`.

Pookie requests track data for the picker list and determines membership before
revealing its rows. A match in a partial list is sufficient; a missing match in a
partial list requires a fresh full playlist read. Complete lists need no extra
requests. This reads only playlist metadata, without decoding artwork or media.
