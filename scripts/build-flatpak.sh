#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Daniel Freak
# SPDX-License-Identifier: BSD-3-Clause
set -euo pipefail

project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
app_id=io.github.freakdaniel.Pookie
runtime_version=50
output_root="$project_root/artifacts/flatpak"
publish_dir="$output_root/publish"
build_dir="$output_root/build"
repo_dir="$output_root/repo"
bundle="$output_root/Pookie-linux-x86_64.flatpak"

for dependency in dotnet flatpak; do
    command -v "$dependency" >/dev/null || { echo "Missing dependency: $dependency" >&2; exit 1; }
done
flatpak info "org.gnome.Platform//$runtime_version" >/dev/null || {
    echo "Install the runtime: flatpak install flathub org.gnome.Platform//$runtime_version" >&2
    exit 1
}
mkdir -p "$output_root"
dotnet publish "$project_root/src/Pookie.App" -c Release -r linux-x64 --self-contained true -o "$publish_dir"
# Only this script's disposable staging directory is replaced on rebuild.
rm -rf -- "$build_dir"
# Publishing on the host needs no SDK download: the installed platform is enough
# to stage a self-contained .NET application and verify its runtime dependencies.
flatpak build-init --arch=x86_64 "$build_dir" "$app_id" org.gnome.Platform org.gnome.Platform "$runtime_version"
install -d "$build_dir/files/lib/pookie" "$build_dir/files/bin" \
    "$build_dir/files/share/applications" "$build_dir/files/share/metainfo" \
    "$build_dir/files/share/icons/hicolor/scalable/apps"
cp -a "$publish_dir/." "$build_dir/files/lib/pookie/"
install -m755 "$project_root/packaging/flatpak/pookie" "$build_dir/files/bin/pookie"
install -m644 "$project_root/packaging/flatpak/$app_id.desktop" "$build_dir/files/share/applications/"
install -m644 "$project_root/packaging/flatpak/$app_id.metainfo.xml" "$build_dir/files/share/metainfo/"
install -m644 "$project_root/src/Pookie.App/Assets/Branding/app-icon.svg" \
    "$build_dir/files/share/icons/hicolor/scalable/apps/$app_id.svg"
for size in 16 24 32 48 64 128 256 512; do
    icon_dir="$build_dir/files/share/icons/hicolor/${size}x${size}/apps"
    install -d "$icon_dir"
    install -m644 "$project_root/src/Pookie.App/Assets/Branding/app-icon-$size.png" "$icon_dir/$app_id.png"
done
flatpak build-finish "$build_dir" --command=pookie --sdk=org.gnome.Sdk/x86_64/50 \
    --share=network --share=ipc --socket=x11 --socket=pulseaudio --device=dri \
    --talk-name=org.freedesktop.secrets --own-name=org.mpris.MediaPlayer2.Pookie.* \
    --filesystem=xdg-run/discord-ipc-0 --filesystem=xdg-run/app/com.discordapp.Discord:ro \
    --env=GDK_BACKEND=x11 --env=DOTNET_ROLL_FORWARD=Disable
flatpak build-export --arch=x86_64 "$repo_dir" "$build_dir" stable
flatpak build-bundle --arch=x86_64 --runtime-repo=https://flathub.org/repo/flathub.flatpakrepo \
    "$repo_dir" "$bundle" "$app_id" stable
sha256sum "$bundle" > "$bundle.sha256"
printf 'Flatpak bundle: %s\n' "$bundle"
