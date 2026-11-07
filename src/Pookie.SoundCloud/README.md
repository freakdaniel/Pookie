# Pookie.SoundCloud

SoundCloud API library shared by the native application, browser adapters and tests.
Public types use the `Pookie.SoundCloud` namespace throughout; folders describe
responsibilities without changing the library's public API.

| Folder | Responsibility |
| --- | --- |
| `Client/` | `SoundCloudWebClient` facade and its feature-specific partials. |
| `Transport/` | Transport contracts, native HTTP requests and session recovery. |
| `Protocol/` | Validated commands and events exchanged with the browser worker. |
| `Requests/` | Track, comment, playlist and chat request validation and GraphQL queries. |
| `Models/` | Track, user, playlist, feed, chat and paginated response models. |
| `Parsing/` | Mapping SoundCloud JSON into library items and discovery shelves. |
| `Authentication/` | Web session identity and login-session message validation. |
| `Playback/` | Stream resolution and waveform data/normalization. |
| `Serialization/` | Source-generated JSON serialization configuration. |
| `Errors/` | API errors, challenge kinds and unconfirmed playlist creation. |
| `Docs/` | [Web sessions](Docs/WebSession.md) and [playlist operations](Docs/PlaylistCreation.md). |

Keep API operations in the matching client partial; keep their input contracts
and response models in `Requests/` and `Models/`. Transport implementations own
HTTP/browser recovery, while the client owns domain validation and confirmation.
The library does not depend on the UI or audio engine.

Run `dotnet test tests/Pookie.SoundCloud.Tests` and
`node --test tests/browser-requests.test.cjs` from the repository root.
