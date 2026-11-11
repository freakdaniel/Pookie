# Pookie

Playback settings offer a maximum audio volume of 100%, 150% or 200% on Windows,
macOS and Linux. Lowering the limit also clamps the current and remembered volume.
Windows adds PCM gain before Media Foundation's volume control. Protected-output
policies can reject this effect; such streams keep playing at up to 100%, with
the restriction recorded under `Pookie.Audio.PlayReady` in the log.
Chat messages use Enter to send by default (Shift+Enter inserts a newline); an
explicitly saved Ctrl+Enter preference is preserved.

Chat attachments use a separately authenticated PKM2 metadata header with file
types, sizes, dimensions, duration and small previews. The UI fetches and caches
this header first with a bounded range request; originals for visible messages
load in a separate background queue. Inbox previews do not download originals.
Older attachment packages remain supported.

Native platform calls in the application use `LibraryImport` with generated
marshalling, explicit UTF-8/UTF-16 strings and native boolean widths. This makes
these bindings suitable for Native AOT. Native publishing is available through
`npm run build-aot-linux`, `build-aot-win` and `build-aot-mac`, on the target OS.
See [NATIVE-AOT.md](NATIVE-AOT.md) for the dependency audit, prerequisites,
verification commands and remaining limitations. Ordinary development builds
continue to use the managed runtime.
On Linux, copied cover images offer an exact PNG payload rather than GTK's
automatically converted image formats, so consumers use the same colour data.

On Windows, Pookie plays protected SoundCloud streams through the system
PlayReady CDM and Media Foundation. The playback integration is written in C#
and runs in the application process.

Linux x64 releases include libVLC, its base plugins, codec/audio dependencies and
their license notices in `libvlc/`. Run `Pookie` directly; installing VLC or setting
`LD_LIBRARY_PATH` is not required. Keep this directory with the application.
Video startup exceptions (including inner exceptions and stacks) and native VLC
errors are recorded under `Pookie.Video` in the application log.

Build Linux releases with `npm run build-linux` (or the equivalent `dotnet publish
-r linux-x64` command) on Ubuntu 22.04+ with Python 3, apt, dpkg, binutils and
ubuntu-keyring. Builds automatically download signed Ubuntu 22.04 packages
into a temporary directory, without installing anything on the build machine,
and bundle the ELF dependency closure with relative RUNPATHs. The host supplies
glibc (2.35+) and the ELF loader. The prepared runtime is cached in
`build/libvlc/linux-x64`; delete that directory to pick up package/security
updates. `libvlc/manifest.json` records exact package versions and hashes.
On Linux x64, `dotnet run --project src/Pookie.App` also prepares and copies the
same runtime next to the development executable, without installing VLC in the
system. The first build downloads the packages; later builds reuse the cache and
copy changed files only. IDE design-time builds do not download the runtime.
Cross-building Linux from Windows/macOS is not supported by this packaging step.

macOS Apple Silicon and Intel builds include LibVLC 3.0.24 from the official
`VideoLAN.LibVLC.Mac` 3.1.3.2 NuGet package. Both `dotnet run --project
src/Pookie.App` and `dotnet publish -r osx-arm64` / `-r osx-x64` copy the runtime,
plugins and data into `libvlc/osx-<architecture>/lib/` next to the executable.
NuGet downloads it on the first restore and reuses its package cache afterwards.
Installing VLC in `/Applications` or configuring `DYLD_LIBRARY_PATH` is not
required. The loader selects the runtime for the current process architecture,
including Intel processes running through Rosetta, and loads the packaged core
before libVLC and its plugins. macOS uses VLC's built-in container demuxers.
