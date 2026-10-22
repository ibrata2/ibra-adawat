#!/bin/bash
# Fetches the C libraries the app builds with the NDK, each pinned by its SHA-256:
#   LAME 3.100 (LGPL 2.0)          the MP3 encoder, in libadawat-mp3.so
#   libarchive 3.8.9 (BSD 2-Clause) archive reading and writing, in libadawat-archive.so
#   xz 5.8.4 (0BSD)                 LZMA and XZ for libarchive (7-Zip, .xz)
#   bzip2 1.0.8 (BSD-style)         bzip2 for libarchive
set -euo pipefail
cd "$(dirname "$0")/.."
root=app/src/main/cpp/third_party
mkdir -p "$root"
fetch() {
  local name=$1 url=$2 sha=$3 strip=$4
  [ -f "$root/$name/.fetched" ] && return 0
  local work
  work=$(mktemp -d)
  curl -sSfL -o "$work/source" "$url"
  echo "$sha  $work/source" | shasum -a 256 -c - >/dev/null
  rm -rf "${root:?}/$name"
  mkdir -p "$root/$name"
  tar -xf "$work/source" -C "$root/$name" --strip-components "$strip"
  rm -rf "$work"
  touch "$root/$name/.fetched"
  echo "$name ready"
}
fetch lame https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz \
  ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e 1
fetch libarchive https://github.com/libarchive/libarchive/releases/download/v3.8.9/libarchive-3.8.9.tar.xz \
  888c934f9d95648ecb9163dc8e23ab80a476ecb81a8f1154704a227b5b676dde 1
fetch xz https://github.com/tukaani-project/xz/releases/download/v5.8.4/xz-5.8.4.tar.gz \
  0014c7886930454fe8bd4228665b51af55eeae560ea135c9c4cd33f55b2591d9 1
fetch bzip2 https://sourceware.org/pub/bzip2/bzip2-1.0.8.tar.gz \
  ab5a03176ee106d3f0fa90e381da478ddae405918153cca248e682cd0c4a2269 1
