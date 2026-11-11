# Linux libVLC runtime

The application CI prepares and packages libVLC during each workflow run.
`.github/workflows/libvlc-linux.yml` is a reusable job called by the regular and
Native AOT workflows. It builds the runtime, creates a tar.gz and SHA-256 file,
verifies installation and video decoding, and passes that archive to the
application jobs as the `libvlc-linux-x64` Actions artifact of the same run.
The runtime is bundled into the resulting Pookie distribution.

There is no separate libVLC release, tag, publication or dependency on a previous
workflow run. The workflow only needs repository read permission.

## Packaging

`build-runtime.cjs` runs in the CI packaging environment (Ubuntu 22.04). It fetches
signed packages using isolated apt/dpkg state, follows ELF dependencies and
rewrites RUNPATH relative to the shipped libraries. Package/source versions,
package hashes and redistribution notices stay in `manifest.json` and `licenses/`.
The host supplies glibc and its loader.

`pack-runtime.cjs` creates `libvlc-linux-x64.tar.gz` with deterministic ordering,
timestamps, ownership and gzip metadata, plus its `.sha256` file. The application
jobs download this artifact and pass the producer job's SHA-256 to `prepare.cjs`,
which verifies the archive before extracting it into `build/libvlc/linux-x64`.
MSBuild copies the runtime into build and publish outputs.

The artifact targets **Linux x86-64 with glibc 2.35 or newer**. It is not specific
to Ubuntu or Debian at runtime. Other architectures and musl-based systems need
separate artifacts; they are not added by this change. `runtime.json` describes
the archive format revision, architecture, filename and baseline.

## Local builds

`prepare.cjs` uses `POOKIE_LIBVLC_ARCHIVE`, or automatically finds the archive in
`artifacts/libvlc-runtime/` or `artifacts/libvlc-linux/`. It checks the explicit
`POOKIE_LIBVLC_SHA256` when provided, otherwise reads the adjacent `.sha256` file.
A valid installed cache can be reused without the archive. A failed checksum or
extraction preserves the previous cache, and concurrent builds share a kernel
lock. Application builds do not invoke a package manager or fetch from Releases.

For a fresh checkout, download the `libvlc-linux-x64` artifact from the desired
application workflow run into `artifacts/libvlc-runtime/`. With GitHub CLI:

```sh
gh run download <run-id> --name libvlc-linux-x64 --dir artifacts/libvlc-runtime
dotnet run --project src/Pookie.App
```

Alternatively, select an archive explicitly:

```sh
export POOKIE_LIBVLC_ARCHIVE="/absolute/path/libvlc-linux-x64.tar.gz"
# Place its .sha256 file alongside it, or set POOKIE_LIBVLC_SHA256 explicitly.
dotnet run --project tests/Pookie.App.SmokeTests -- --chat-video-smoke-test
```

Local archive installation needs Linux, Node.js 22+, tar/gzip and flock. It does
not need apt, dpkg, readelf, patchelf, Python or an Ubuntu keyring. If neither an
archive nor a valid cache is available, the build reports how to obtain the CI
artifact rather than silently selecting system libraries.

The producer can also be exercised manually in a Debian/Ubuntu packaging
environment:

```sh
node tools/libvlc-linux/build-runtime.cjs --output build/libvlc-ci/runtime
node tools/libvlc-linux/pack-runtime.cjs --input build/libvlc-ci/runtime --output artifacts/libvlc-linux
```
