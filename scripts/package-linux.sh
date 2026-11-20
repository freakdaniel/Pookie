#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "$0")/.." && pwd)
export VERSION=${1:?expected version, publish directory and release directory}
publish=$(realpath "$2")
mkdir -p "$3"
output=$(realpath "$3")
work=$(mktemp -d "$output/.pookie-linux.XXXXXX")
trap 'rm -rf -- "$work"' EXIT
base="$work/Pookie-$VERSION-linux-x64"
export TMPDIR="$work/tmp"
mkdir "$work/AppDir" "$TMPDIR"
cd "$work/AppDir"
"$root/scripts/stage-linux-app.sh" "$publish" "$PWD/usr"
for format in deb rpm; do
    nfpm package --config "$root/packaging/linux/nfpm.yaml" --packager "$format" --target "$base.$format"
done

id=io.github.freakdaniel.Pookie
ln -s usr/bin/pookie AppRun
ln -s "usr/share/applications/$id.desktop" "$id.desktop"
ln -s "usr/share/icons/hicolor/256x256/apps/$id.png" "$id.png"
runtime="$(dirname -- "$(command -v appimagetool)")/runtime-x86_64"
ln -s "$runtime" ../runtime-x86_64
# Pass relative arguments so appimagetool can also run inside non-ASCII checkout paths.
ARCH=x86_64 appimagetool --appimage-extract-and-run --runtime-file ../runtime-x86_64 --comp zstd . "../${base##*/}.AppImage"

"$root/scripts/build-flatpak.sh" "$publish" "$base.flatpak"
mv -- "$base".{deb,rpm,AppImage,flatpak} "$output/"
