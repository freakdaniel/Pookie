#!/usr/bin/env bash
set -euo pipefail
mkdir -p "$1"
cd "$1"
fetch() {
    curl --fail --location --silent --show-error --retry 3 "$1" -o "$2"
    echo "$3  $2" | sha256sum --check -
}
nfpm=https://github.com/goreleaser/nfpm/releases/download/v2.49.0
case "$2" in
    Linux)
        fetch "$nfpm/nfpm_2.49.0_Linux_x86_64.tar.gz" nfpm.tar.gz 2afaa5ffb5e3493402e31e7c67a5a1fb42133e3f66fc315ca0051934125561c7
        tar xzf nfpm.tar.gz nfpm
        fetch https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage appimagetool ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
        fetch https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64 runtime-x86_64 2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d
        chmod +x appimagetool
        ;;
    Windows)
        fetch "$nfpm/nfpm_2.49.0_Windows_x86_64.zip" nfpm.zip e410472f50b580c05087acc9a8e62bbe617a9e17226aff274f207c3b1bd31c5f
        powershell.exe -NoProfile -Command 'Expand-Archive -LiteralPath nfpm.zip -DestinationPath . -Force'
        test -f nfpm.exe
        ;;
    *) echo "Unsupported packaging host: $2" >&2; exit 1 ;;
esac
