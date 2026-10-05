# Licensing

Pookie is distributed under [BSD-3-Clause](licenses/BSD-3-Clause.txt) and uses
[REUSE](https://reuse.software/) metadata. `REUSE.toml` records file-level
copyright notices and SPDX license identifiers. `licenses/` contains one complete
text per license used by repository files.

| Files | License | Attribution |
| --- | --- | --- |
| Project code, configuration, tests, documentation, and logo | BSD-3-Clause | Richard Habitzreuter; Daniel Freak |
| `src/Pookie.App/Assets/Icons/*.svg` | MIT | Phosphor Icons |
| `src/Pookie.App/Assets/Fonts/GoogleSans.ttf` | OFL-1.1 | The Google Sans Project Authors |
| `src/Pookie.App/Assets/Fonts/Bungee-Regular.ttf` | OFL-1.1 | The Bungee Project Authors |
| `src/Pookie.App/Assets/Branding/soundcloud-mark-white.png` | LicenseRef-SoundCloud-Marks | SoundCloud |
| `src/Pookie.Audio/Drm/CdmAbi.cs` | BSD-3-Clause | The Chromium Authors; Daniel Freak |

Pookie's original MIT copyright and permission notice for Richard Habitzreuter
is retained in `licenses/MIT.txt`, alongside the MIT notice for Phosphor Icons.
The BSD-3-Clause distribution terms do not remove these original notices.

## Asset provenance

Phosphor icons come from [phosphor-icons/core](https://github.com/phosphor-icons/core)
at commit `2b75f3ad12b420c9504ef05df8d2564a28f8500e`. Most assets are duotone SVGs;
filled heart and playback variants are also included. `squares-four`, `list-bullets`,
`copy`, `chat-circle`, and `arrow-left` use the corresponding upstream duotone
assets; `user` uses `assets/fill/user-fill.svg`. The SVGs are embedded, with no
runtime CDN dependency.

`GoogleSans.ttf` is the unmodified `GoogleSans[GRAD,opsz,wght].ttf` from
[Google Fonts](https://github.com/google/fonts/tree/a0e3dbcdc3a3ecfafff3f071159ae0221628922d/ofl/googlesans),
commit `a0e3dbcdc3a3ecfafff3f071159ae0221628922d`. It includes Cyrillic and
variable weights 400–700. It is embedded and registered before the UI is
created; the MewUI theme applies its family globally. Its
[OFL-1.1 license](https://github.com/google/fonts/blob/a0e3dbcdc3a3ecfafff3f071159ae0221628922d/ofl/googlesans/OFL.txt)
and copyright notice are retained in `REUSE.toml`, `licenses/OFL-1.1.txt`, and
the font metadata. System installation and runtime downloads are not required.
Google and Google Sans are trademarks of Google LLC; using the font does not
imply affiliation or sponsorship. See the upstream
[trademark notice](https://github.com/google/fonts/blob/a0e3dbcdc3a3ecfafff3f071159ae0221628922d/ofl/googlesans/TRADEMARKS.md).

`Bungee-Regular.ttf` is the unmodified font from
[Google Fonts](https://github.com/google/fonts/tree/main/ofl/bungee), used only
for the Pookie wordmark. Google Fonts identifies its upstream version as
Bungee 2.000, commit `eb03cf69adab5094f6b84e95357789cdf3bfeb99`.
Its [OFL-1.1 license](https://github.com/google/fonts/blob/main/ofl/bungee/OFL.txt)
and copyright notice are retained in `REUSE.toml`, `licenses/OFL-1.1.txt`, and
the embedded font metadata.
Google Sans is the UI font.

The SoundCloud login button uses the official white six-bar favicon from the
[SoundCloud Media Kit](https://community.soundcloud.com/company/media-kit):
[original transparent asset](https://cdn.prod.website-files.com/62a0a0168756b795debc65bc/69ef2abba42d7532aff4793c_Favicon%20Colors%20white%20(transparent)%20-%20download.webp).
Only its encoding was changed from WebP to PNG; the pixels, proportions, and
colors are preserved. Its size exceeds the kit's 24px minimum for this variant.
It identifies the SoundCloud sign-in action on the orange sign-in button.
This asset is governed by the SoundCloud API Terms of Use, especially
Attribution and Branding / SoundCloud Marks / Design Assets, and the current
Buttons & Logos guidance. These terms are retained in
`licenses/LicenseRef-SoundCloud-Marks.txt`. This is a limited brand permission,
not an open-source license or a sublicense of the SoundCloud trademark.
Pookie remains an unofficial client. Retrieved on 2026-10-05.

`CdmAbi.cs` translates the public Chromium Host/CDM v10 ABI declarations into
C# layouts and unmanaged function pointers. The declaration source is
[content_decryption_module.h](https://github.com/xbmc/inputstream.adaptive/blob/e14d199a5ed7224d1c42a36619ab0a67d3d8f3f6/lib/cdm/cdm/media/cdm/api/content_decryption_module.h)
from Kodi InputStream Adaptive commit `e14d199a5ed7224d1c42a36619ab0a67d3d8f3f6`.
No Kodi implementation code or proprietary Widevine binary is bundled. The host
is implemented in C#; the installed Google CDM remains native. The interop
targets 64-bit x64/ARM64 instance-call conventions. Linux x64 has been tested
against the real CDM; Windows/macOS require runtime validation.

## Validate and maintain

With [uv](https://docs.astral.sh/uv/) installed, run from the repository root:

```sh
npm run license:check
```

This runs `scripts/check-licenses.cjs`, which copies Git-tracked and non-ignored
untracked files into a temporary directory, excluding deleted files. Only in
that copy, `licenses/` is mapped to `LICENSES/`, then the unmodified REUSE 6.2.0
tool runs `reuse lint` with its portable encoding detector. The temporary copy
is removed after validation, including when lint fails.

[REUSE 3.3 requires uppercase `LICENSES/`](https://reuse.software/spec-3.3/#license-files).
The lowercase directory in this repository is an intentional layout deviation:
strict REUSE compliance is validated on the normalized copy. Direct `reuse lint`
and external REUSE scanners on case-sensitive filesystems need the same mapping.

GitHub Actions runs this same check before packaging. When adding third-party
files, record their actual notices and SPDX identifiers in `REUSE.toml`, keep
the source/version in this document, and add any new license text to `licenses/`.
Existing SPDX file headers take precedence over group annotations. New files
outside the declared path rules must be explicitly covered; unannotated files
fail the check.

## Binary distributions

`Directory.Build.targets` copies `LICENSE`, `LICENSING.md`, `REUSE.toml`, and the
all `licenses/*.txt` files into application builds and published
packages. This preserves both the complete terms and the copyright notices for
embedded assets and the CDM declaration translation.

SoundFlow and SoundFlow.Codecs.FFMpeg are restored NuGet dependencies. Their
license texts and bundled native-library notices are read directly from the
restored packages and combined into `THIRD-PARTY-NOTICES.txt` during the audio
project's build. The generated file is copied into the application distribution;
the dependency notices are not duplicated into the source tree. REUSE checks
repository files; dependency notices accompany the built binaries separately.
