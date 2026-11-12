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
jobs download this artifact and pass the producer job's SHA-256 to the C# installer,
which verifies the archive before extracting it into `build/libvlc/linux-x64`.
MSBuild copies the runtime into build and publish outputs.

The artifact targets **Linux x86-64 with glibc 2.35 or newer**. It is not specific
to Ubuntu or Debian at runtime. Other architectures and musl-based systems need
separate artifacts; they are not added by this change. `runtime.json` describes
the archive format revision, architecture, filename and baseline.

## Local builds

The .NET 10 installer (`LibVlcLinux.csproj`) uses `POOKIE_LIBVLC_ARCHIVE`, or
automatically finds the archive in `artifacts/libvlc-runtime/` or
`artifacts/libvlc-linux/`. It checks the explicit
`POOKIE_LIBVLC_SHA256` when provided, otherwise reads the adjacent `.sha256` file.
A valid installed cache can be reused without the archive. A failed checksum or
extraction preserves the previous cache, and concurrent builds share a kernel
lock through .NET file sharing. Application builds do not invoke Node.js, a
package manager, tar, gzip or flock, or fetch from Releases. The installer uses
the .NET libraries for hashing, gzip/tar extraction and file locking.

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

Local archive installation needs only Linux and the .NET 10 SDK. Node.js is
only used by the maintenance scripts, their tests and CI runtime production.
If neither an archive nor a valid cache is available, the build reports how to obtain the CI
artifact rather than silently selecting system libraries.

MSBuild runs the installer automatically for build, run and publish. To invoke
it directly without Node.js:

```sh
dotnet run --project tools/libvlc-linux -- --output build/libvlc/linux-x64 --root .
```

The producer can also be exercised manually in a Debian/Ubuntu packaging
environment:

```sh
node tools/libvlc-linux/build-runtime.cjs --output build/libvlc-ci/runtime
node tools/libvlc-linux/pack-runtime.cjs --input build/libvlc-ci/runtime --output artifacts/libvlc-linux
```
