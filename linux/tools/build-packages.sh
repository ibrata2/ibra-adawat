#!/bin/bash
# Builds Adawat for Linux on x64 and ARM64:
#   build/packages/Adawat-linux-amd64.deb, Adawat-linux-arm64.deb       (Ubuntu, Debian, Mint, Pop!_OS: system ffmpeg)
#   build/packages/Adawat-linux-x86_64.rpm, Adawat-linux-aarch64.rpm    (Fedora, openSUSE: system ffmpeg)
#   build/packages/Adawat-x86_64.AppImage, Adawat-aarch64.AppImage      (any distribution, ffmpeg inside)
# The names carry no version, so releases/latest/download/<name> links on the website keep working.
# Needs the .NET 10 SDK, nfpm and squashfs-tools (brew install nfpm squashfs on a Mac).
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
DOTNET=${DOTNET:-$(command -v dotnet || echo ~/.dotnet/dotnet)}
OUT=build/packages
rm -rf "$OUT" && mkdir -p "$OUT" build/runtime
tools/fetch-models.sh
tools/fetch-ffmpeg.sh >/dev/null

for pair in linux-x64:amd64:x86_64 linux-arm64:arm64:aarch64; do
  IFS=: read -r rid debarch appimagearch <<< "$pair"
  publish="build/publish/$rid"
  rm -rf "$publish"
  "$DOTNET" publish src/Adawat.Linux/Adawat.Linux.csproj -c Release -r "$rid" --self-contained true -p:DebugType=none -o "$publish" -v quiet -nologo
  # adawat-cli runs any wheel choice from a terminal; it shares the app's runtime files.
  "$DOTNET" publish ../windows/src/Adawat.Cli/Adawat.Cli.csproj -f net10.0 -c Release -r "$rid" --self-contained true -p:DebugType=none -o "$publish" -v quiet -nologo
  echo "published $rid: $(du -sh "$publish" | cut -f1)"

  # .deb and .rpm use the distribution's ffmpeg.
  config="build/nfpm-$rid.yaml"
  sed -e "s|\${ARCH}|$debarch|" -e "s|\${VERSION}|$VERSION|" -e "s|\${PUBLISH}|$publish|" packaging/nfpm.yaml > "$config"
  nfpm package --config "$config" --packager deb --target "$OUT/Adawat-linux-$debarch.deb" >/dev/null
  nfpm package --config "$config" --packager rpm --target "$OUT/Adawat-linux-$appimagearch.rpm" >/dev/null

  # The AppImage carries its own ffmpeg next to the app, in tools/, where Adawat looks first.
  appdir="build/AppDir-$rid"
  rm -rf "$appdir" && mkdir -p "$appdir/usr/lib/adawat/tools" "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/256x256/apps"
  cp -R "$publish/." "$appdir/usr/lib/adawat/"
  cp build/ffmpeg/$rid/ffmpeg build/ffmpeg/$rid/ffprobe "$appdir/usr/lib/adawat/tools/"
  cp build/ffmpeg/$rid/LICENSE.txt "$appdir/usr/lib/adawat/tools/FFMPEG-LICENSE.txt"
  cp packaging/AppRun "$appdir/AppRun"
  cp packaging/adawat.desktop "$appdir/adawat.desktop"
  cp packaging/adawat.desktop "$appdir/usr/share/applications/"
  cp assets/icons/adawat-256.png "$appdir/adawat.png"
  cp assets/icons/adawat-256.png "$appdir/usr/share/icons/hicolor/256x256/apps/adawat.png"
  ln -sf adawat.png "$appdir/.DirIcon"
  runtime="build/runtime/runtime-$appimagearch"
  [[ -f "$runtime" ]] || curl -fsSL -o "$runtime" "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-$appimagearch"
  rm -f "build/AppImage-$rid.squashfs"
  mksquashfs "$appdir" "build/AppImage-$rid.squashfs" -root-owned -noappend -comp zstd -Xcompression-level 19 -quiet >/dev/null
  cat "$runtime" "build/AppImage-$rid.squashfs" > "$OUT/Adawat-$appimagearch.AppImage"
  chmod +x "$OUT/Adawat-$appimagearch.AppImage"
done
(cd "$OUT" && shasum -a 256 *.deb *.rpm *.AppImage | tee SHA256SUMS)
ls -lh "$OUT"
