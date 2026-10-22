#!/bin/bash
# Downloads the ffmpeg and ffprobe that ship inside the Adawat AppImage: BtbN's static GPL builds of FFmpeg 8.1, the
# same pinned build as the Windows app. The .deb and .rpm use the distribution's own ffmpeg instead.
#   linux/tools/fetch-ffmpeg.sh   → linux/build/ffmpeg/{linux-x64,linux-arm64}/{ffmpeg,ffprobe}
set -euo pipefail
cd "$(dirname "$0")/.."
TAG=autobuild-2026-10-18-13-01
VERSION=n8.1.3-18-g46d899d7a0
BASE="https://github.com/BtbN/FFmpeg-Builds/releases/download/$TAG"
OUT=build/ffmpeg
mkdir -p "$OUT"
for pair in linux64:linux-x64 linuxarm64:linux-arm64; do
  arch=${pair%%:*}; rid=${pair#*:}
  archive="$OUT/ffmpeg-$VERSION-$arch-gpl-8.1.tar.xz"
  [[ -f "$archive" ]] || curl -fL --retry 3 -o "$archive" "$BASE/ffmpeg-$VERSION-$arch-gpl-8.1.tar.xz"
  rm -rf "$OUT/$rid" && mkdir -p "$OUT/$rid"
  tar -xJf "$archive" -C "$OUT/$rid" --strip-components=2 "ffmpeg-$VERSION-$arch-gpl-8.1/bin/ffmpeg" "ffmpeg-$VERSION-$arch-gpl-8.1/bin/ffprobe"
  tar -xJf "$archive" -C "$OUT/$rid" --strip-components=1 "ffmpeg-$VERSION-$arch-gpl-8.1/LICENSE.txt"
done
(cd "$OUT" && shasum -a 256 *.tar.xz > SOURCES.sha256 && cat SOURCES.sha256)
ls -la "$OUT"/linux-*/
