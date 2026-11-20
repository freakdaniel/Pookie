#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Daniel Freak
# SPDX-License-Identifier: BSD-3-Clause
set -euo pipefail

project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
app_id=io.github.freakdaniel.Pookie
runtime_version=50
output_root="$project_root/artifacts/flatpak"
publish_dir=${1:-"$output_root/publish"}
bundle=${2:-"$output_root/Pookie-linux-x86_64.flatpak"}
mkdir -p "$output_root" "$(dirname -- "$bundle")"
if [[ $# == 0 ]]; then
    dotnet publish "$project_root/src/Pookie.App" -c Release -r linux-x64 --self-contained true -o "$publish_dir"
fi

work=$(mktemp -d "$output_root/staging.XXXXXX")
trap 'rm -rf -- "$work"' EXIT
build_dir="$work/build"
repo_dir="$work/repo"
# The payload has already been published. Exporting this tree does not need a local SDK.
mkdir -p "$build_dir/files"
cat > "$build_dir/metadata" <<METADATA
[Application]
name=$app_id
runtime=org.gnome.Platform/x86_64/$runtime_version
sdk=org.gnome.Sdk/x86_64/$runtime_version
METADATA
"$project_root/scripts/stage-linux-app.sh" "$publish_dir" "$build_dir/files"
flatpak build-finish "$build_dir" --command=pookie --sdk=org.gnome.Sdk/x86_64/"$runtime_version" \
    --share=network --share=ipc --socket=x11 --socket=pulseaudio --device=dri \
    --talk-name=org.freedesktop.secrets --own-name=org.mpris.MediaPlayer2.Pookie.* \
    --filesystem=xdg-run/discord-ipc-0 --filesystem=xdg-run/app/com.discordapp.Discord:ro \
    --env=GDK_BACKEND=x11 --env=DOTNET_ROLL_FORWARD=Disable
flatpak build-export --arch=x86_64 "$repo_dir" "$build_dir" stable
flatpak build-bundle --arch=x86_64 --runtime-repo=https://flathub.org/repo/flathub.flatpakrepo \
    "$repo_dir" "$bundle" "$app_id" stable
if [[ $# == 0 ]]; then sha256sum "$bundle" > "$bundle.sha256"; fi
printf 'Flatpak bundle: %s\n' "$bundle"
