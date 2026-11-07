# Web playlists

The application uses the logged-in website's live fetch and DataDome tag for
playlist reads and writes. All resource routes use the V2 web API and numeric
IDs. APK URNs, mobile wrappers, SDK telemetry and mobile challenge handling have
been removed. Protection UI belongs to the same embedded website session.

## Creation and membership

Creating a playlist sends `POST /playlists` with a `playlist` object containing
`title`, `sharing` and the initial numeric track ID. The browser verifies the
current user first; the facade validates the returned owner, title, privacy and
seed track. A lost POST response is not blindly replayed.

The picker reads `/users/{id}/playlists` with track data, preserving server order
and excluding albums/foreign owners. A match in a partial preview proves presence;
absence requires a complete track-ID list. Reads do not decode artwork or audio.

Appending reads `/playlists/{id}?representation=full`, validates owner, album flag,
track count and every numeric ID, and sends one `PUT /playlists/{id}` containing
the complete numeric track order plus the appended ID. Duplicates and unavailable
ID-only placeholders retain their original order. Already-present tracks do not
produce a write. An incomplete list cannot be overwritten.

## Editing

`playlist-edit` is a bounded browser command with separate update, remove, delete
and artwork variants. It rejects mixed commands, malformed IDs, oversized text
and invalid JPEG data before issuing a request. The browser reads the current
playlist and verifies ownership before mutation.

Metadata updates send only title, description and sharing in the `playlist`
object. Track order is included only when explicitly supplied. Reordering compares
the expected source order, including duplicates, to the fresh complete snapshot.
Removal likewise requires a complete ID list. A partial web snapshot remains
editable for metadata, but cannot safely replace the track list.

JPEG artwork uses multipart `playlist[artwork_data]` on the same web playlist
resource; the browser generates the multipart boundary. This field is documented
in [SoundCloud's playlist request schema](https://github.com/soundcloud/api/blob/master/openapi/api.yaml).
Deletion uses `DELETE /playlists/{id}` after the UI's confirmation. Successful
updates are read back; the facade verifies resource/owner and requested metadata
or order before refreshing the displayed page and library. Unconfirmed writes are
not automatically repeated.

## Verification

`dotnet test tests/Pookie.SoundCloud.Tests` covers web routes, owner checks,
complete source order, duplicate/placeholder preservation, stale accounts,
metadata confirmation and invalid artwork.

`node --test tests/browser-requests.test.cjs` checks browser-side mutation bodies,
fixed routes, credentials, protection pauses and bounded replay, and multipart
artwork. `--collections-ui-smoke-test --web-playlist-editing-only --no-audio`
checks the editor and library refresh using fixture data.

`--embedded-web-challenge-smoke-test` exercises the real native browser and the
existing modal overlay with a local slider iframe. These fixtures do not establish
live server acceptance or change a real account.
