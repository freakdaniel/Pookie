# Native protected audio

Pookie's native Widevine host uses Google's CDM to obtain a service license and
decrypt samples for normal playback. It does not extract content keys.

## Component installation

Normal Windows startup uses system PlayReady for protected playback and skips
Widevine setup entirely: no Google update request, download, consent card, or
Widevine graphics warmup. Existing Widevine files/consent are not consulted.
The following startup installation flow applies to Linux and macOS.

When a component needs to be installed, Pookie queries Google's component updater
at `https://update.googleapis.com/service/update2/json` for the current Widevine
version, HTTPS archive URL, exact size and SHA-256. There is no embedded Firefox
manifest or bundled Widevine license text. No browser installation/profile is
required or inspected.

While the startup loader runs, Pookie downloads and verifies the complete CRX3
archive, then reads its original UTF-8 `LICENSE` in memory. This follows the
InputStream Helper workflow: the archive must be downloaded to obtain the vendor
terms. No component files are installed and no native code is loaded at this
stage. Failed license retrieval leaves the loader with a retry action; a stale
local or hardcoded agreement is never substituted for a new consent card.

After the initial startup loader has appeared, the mandatory dense matte glass
card displays those downloaded terms. The user explicitly accepts by pressing
“Принять и продолжить”, or exits. The loader logo and spinner remain behind the
matte glass, without a separate hide/show animation. The right license/action
column keeps its width while the introduction flexes. The card has no checkbox
or module progress area; URLs open in the system browser. Escape and backdrop
clicks cannot bypass acceptance.

Acceptance is stored atomically in `Components/Widevine/consent.json` and tied to
the SHA-256 of the displayed text. Changed terms require fresh acceptance. The
card closes before installation begins, which reuses the already verified archive
without downloading it again. The installer checks its license against the
accepted text and retains the vendor's original `LICENSE` beside the library.
There are no manually maintained copies of that file in this repository.

The standard perspective/blur animation reuses a native-DPI content capture;
the Widevine logo participates in the same transition. Graphics surfaces are
warmed before the loader animation; updated license text invalidates the cached
capture. Static glass samples and the blue button rim are reused during hover.
Network, hashing, decompression and file operations run off the UI thread.
Installation failures and retries stay on the loader, outside the consent card.

The component lives in the application's durable data directory:

```text
Components/Widevine/<runtime>/<version>/
```

On Linux this is normally `~/.local/share/Pookie/Components/Widevine/`, honoring
`XDG_DATA_HOME`. Clearing media caches does not remove it. After a successful
installation, `current.json` records the selected Google package descriptor.
Subsequent launches validate the installed manifest, license, acceptance receipt
and library hash, and can reuse that accepted version without network access.
This is not a background auto-updater: a new installation resolves the latest
package from Google; an accepted installed version stays selected. If acceptance
is removed, the terms must be retrieved from Google before showing a new card.
Interrupted downloads and failed verification preserve a previous installation.

Supported process platforms are Linux x64, Windows x64/ARM64 and macOS x64/ARM64.
Other architectures are reported as unsupported; no alternative browser or OS
image is installed. Normal Windows production playback uses the existing
PlayReady backend and does not prepare Widevine. Windows Widevine packages remain
supported by the explicit native-CDM diagnostics, outside normal app startup.

The downloader has no account credentials or cookies. Metadata comes only from
the fixed Google HTTPS updater; archive URLs and redirects are restricted to
Google's component hosts over HTTPS. Extraction rejects traversal, duplicate
names and symbolic links, and bounds file sizes. Installation uses staging and
a cross-process lock. The complete archive's size and SHA-256 must match Google's
response before its license is displayed or any files are installed.

## Licensing

Widevine CDM is proprietary Google software. Downloading it directly instead of
shipping a binary in the Pookie distribution does **not** grant an integration,
use or redistribution license. Google's general documentation requires a
Widevine agreement, but it does not establish that every application must obtain
its own direct Master License Agreement: partner/runtime arrangements also
exist. The licensing basis for Pookie's standalone native C# host has not been
confirmed. The downloaded package's `LICENSE` is retained in its installation
directory. No license agreement is accepted automatically by Pookie.

Official information:

- https://developers.google.com/widevine/drm/overview
- https://developers.google.com/widevine/contact/support

Existing open-source integration patterns:

- Kodi InputStream Adaptive does not include the proprietary CDM. Its companion
  InputStream Helper downloads the component from Google and asks the user to
  accept the displayed vendor terms. These sources document the implementation,
  not a general grant of rights to other developers:
  https://github.com/xbmc/inputstream.adaptive
  https://github.com/emilsvennesson/script.module.inputstreamhelper/blob/master/lib/inputstreamhelper/widevine/widevine.py
  https://github.com/emilsvennesson/script.module.inputstreamhelper/blob/master/lib/inputstreamhelper/widevine/repo.py
  Team Kodi's 2017 explanation describes loading an external binary while keeping
  playback in Kodi. InputStream Helper contributor Dag Wieers stated in 2021 that
  Team Kodi accepted end-user download/extraction because redistribution was not
  permitted. Neither statement establishes the existence or absence of a private
  Google agreement:
  https://kodi.tv/article/dev-journal-kodi-and-drm/
  https://github.com/raspberrypi/Raspberry-Pi-OS-64bit/issues/11#issuecomment-847339381
- Castlabs Electron for Content Security downloads and updates the CDM at
  runtime. Its EVS service removes the need to obtain an application's own VMP
  signing certificate directly from Google. Free EVS covers packages derived
  from official ECS releases on Windows/macOS, not an arbitrary C# executable.
  The Linux CDM does not support VMP, so EVS does not sign Linux binaries. This
  technical distinction is not itself a general CDM licensing exception:
  https://github.com/castlabs/electron-releases
  https://github.com/castlabs/electron-releases/wiki/EVS
- The open-source music player Parachord uses ECS and production EVS signing:
  https://github.com/Parachord/parachord#build-from-source

Shaka Player is an open-source EME player, not a replacement for the platform
CDM. Clear Key requires the service to supply compatible keys/licenses. The
open-source pywidevine implementation requires Google-provisioned credentials
that the project does not supply, so replacing the binary with that library
does not make the integration independent of Google's trust/provisioning:
https://github.com/shaka-project/shaka-player/blob/main/docs/tutorials/drm-config.md
https://github.com/devine-dl/pywidevine#disclaimer

OpenWV is another relevant open-source implementation (LGPL-3.0), exposing the
native CDM shared-library API. Its authors require an externally obtained device
identity (`.wvd`, including a private key) and do not provide official prebuilt
binaries. This is an alternative implementation of the CDM, not a way to make
protected playback work without device credentials. It has not been integrated
or tested in Pookie:
https://github.com/tchebb/openwv

## SoundCloud formats without a CDM

Ordinary HLS and HLS with an identity AES-128 key are distinct from Widevine
CENC. yt-dlp supports the older SoundCloud `encrypted-hls` format as AES HLS but
skips `ctr-`/`cbc-` protected formats. NewPipeExtractor skips protocols containing
`encrypted`. The Go scdl extractor selects a plain `hls` / `audio/mpeg` entry from
the website's hydration data; it reports failure if no such entry is present:

- https://github.com/yt-dlp/yt-dlp/blob/412e7f0a4b0588fe884a02b344ac66a06538475b/yt_dlp/extractor/soundcloud.py
- https://github.com/TeamNewPipe/NewPipeExtractor/blob/1092d2c9822470086fcfd43c5ada9677b9eeeba8/extractor/src/main/java/org/schabi/newpipe/extractor/services/soundcloud/extractors/SoundcloudStreamExtractor.java
- https://github.com/imthaghost/scdl/blob/master/scrape.go

For `whyspurky-sc/toshno-1` (track 2402385261), inspection on 2026-11-12 found
only CBC/CTR variants in the authenticated `/resolve` and `/tracks/{id}` APIs
and in public page hydration. Both API responses listed six transcodings and
zero `hls` / `progressive` entries.
The AAC 160/96 kbit/s CTR manifests advertised Widevine and PlayReady; CBC
manifests advertised FairPlay. Both ABR variants returned HTTP 404. Available
manifests covered the full ~96.292 seconds. The public page also reported
`downloadable=false`. No ordinary HLS/MP3 or identity AES-128 alternative was
advertised in those responses. This is an observation for that track/session,
not a claim about every track or every undocumented endpoint.

Reproduce format inspection without loading a CDM or requesting a DRM license:

```sh
dotnet run --project tests/Pookie.App.SmokeTests -- --stream-formats-probe https://soundcloud.com/whyspurky-sc/toshno-1
```

The installer supplies the component, not a service license. Each protected
track still requires the streaming service to authorize playback; CDM availability
does not guarantee that every service or DRM host policy accepts the player.
