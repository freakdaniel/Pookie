#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "$0")/.." && pwd)
publish=$1
prefix=$2
id=io.github.freakdaniel.Pookie

test -x "$publish/Pookie"
test -f "$publish/licenses/THIRD-PARTY-NOTICES.txt"
install -d "$prefix/lib/pookie"
cp -a "$publish/." "$prefix/lib/pookie/"
install -Dm755 "$root/packaging/flatpak/pookie" "$prefix/bin/pookie"
install -Dm644 "$root/packaging/flatpak/$id.desktop" "$prefix/share/applications/$id.desktop"
install -Dm644 "$root/packaging/flatpak/$id.metainfo.xml" "$prefix/share/metainfo/$id.metainfo.xml"
install -Dm644 "$root/src/Pookie.App/Assets/Branding/app-icon.svg" "$prefix/share/icons/hicolor/scalable/apps/$id.svg"
for size in 16 24 32 48 64 128 256 512; do
    install -Dm644 "$root/src/Pookie.App/Assets/Branding/app-icon-$size.png" \
        "$prefix/share/icons/hicolor/${size}x${size}/apps/$id.png"
done
