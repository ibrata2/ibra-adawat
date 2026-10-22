#!/bin/zsh
# Builds the ffmpeg that ships inside Adawat.app from the official sources: one static binary for this Mac,
# GPL v3, with only free libraries, so the app can be shared legally. The exact versions are pinned below;
# together with this script they are the corresponding source the GPL asks for.
#
#   scripts/build-ffmpeg.sh        → build/ffmpeg/bin/ffmpeg   (needs Xcode tools, cmake and pkgconf)
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$PWD/build/ffmpeg"
SRC="$ROOT/src"
PREFIX="$ROOT/deps"
mkdir -p "$SRC" "$PREFIX" "$ROOT/bin"
export MACOSX_DEPLOYMENT_TARGET=14.0
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"
export CFLAGS="-O2 -mmacosx-version-min=14.0" CXXFLAGS="-O2 -mmacosx-version-min=14.0"
JOBS=$(sysctl -n hw.ncpu)

FFMPEG=7.1.1 X265=4.1 LAME=3.100 OPUS=1.5.2 OGG=1.3.5 VORBIS=1.3.7 VPX=1.15.0
fetch() { # url name
  [[ -f "$SRC/$2" ]] || curl -fL --retry 3 -o "$SRC/$2" "$1"
}
fetch "https://ffmpeg.org/releases/ffmpeg-$FFMPEG.tar.xz" "ffmpeg-$FFMPEG.tar.xz"
fetch "https://code.videolan.org/videolan/x264/-/archive/stable/x264-stable.tar.bz2" "x264-stable.tar.bz2"
fetch "https://bitbucket.org/multicoreware/x265_git/downloads/x265_$X265.tar.gz" "x265_$X265.tar.gz"
fetch "https://downloads.sourceforge.net/project/lame/lame/$LAME/lame-$LAME.tar.gz" "lame-$LAME.tar.gz"
fetch "https://downloads.xiph.org/releases/opus/opus-$OPUS.tar.gz" "opus-$OPUS.tar.gz"
fetch "https://downloads.xiph.org/releases/ogg/libogg-$OGG.tar.gz" "libogg-$OGG.tar.gz"
fetch "https://downloads.xiph.org/releases/vorbis/libvorbis-$VORBIS.tar.gz" "libvorbis-$VORBIS.tar.gz"
fetch "https://github.com/webmproject/libvpx/archive/refs/tags/v$VPX.tar.gz" "libvpx-$VPX.tar.gz"
(cd "$SRC" && shasum -a 256 *.tar.* > ../SOURCES.sha256)

# A library already installed by an earlier run is kept, so a failed build resumes where it stopped.
have() { [[ -f "$PREFIX/lib/$1" ]]; }
unpack() { rm -rf "$SRC/$2" && mkdir -p "$SRC/$2" && tar -xf "$SRC/$1" -C "$SRC/$2" --strip-components 1; }

have libx264.a || { echo "== x264"; unpack x264-stable.tar.bz2 x264
(cd "$SRC/x264" && ./configure --prefix="$PREFIX" --enable-static --enable-pic --disable-cli && make -j$JOBS && make install); }

have libx265.a || { echo "== x265"; unpack "x265_$X265.tar.gz" x265
# Current CMake no longer accepts the two old policies x265 4.1 asks for; its code works with the new ones.
sed -i '' -E '/cmake_policy\(SET CMP00(25|54) OLD\)/d; s/STREQUAL "Clang"/MATCHES "Clang"/g' "$SRC/x265/source/CMakeLists.txt"
(cd "$SRC/x265/build" && cmake ../source -DCMAKE_INSTALL_PREFIX="$PREFIX" -DENABLE_SHARED=OFF -DENABLE_CLI=OFF \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=14.0 -DCMAKE_POLICY_VERSION_MINIMUM=3.5 && make -j$JOBS && make install); }

have libmp3lame.a || { echo "== lame"; unpack "lame-$LAME.tar.gz" lame
sed -i '' '/lame_init_old/d' "$SRC/lame/include/libmp3lame.sym"
(cd "$SRC/lame" && ./configure --prefix="$PREFIX" --disable-shared --enable-static --disable-frontend && make -j$JOBS && make install); }

have libopus.a || { echo "== opus"; unpack "opus-$OPUS.tar.gz" opus
(cd "$SRC/opus" && ./configure --prefix="$PREFIX" --disable-shared --enable-static --disable-doc --disable-extra-programs && make -j$JOBS && make install); }

have libogg.a || { echo "== ogg"; unpack "libogg-$OGG.tar.gz" ogg
(cd "$SRC/ogg" && ./configure --prefix="$PREFIX" --disable-shared --enable-static && make -j$JOBS && make install); }
have libvorbis.a || { echo "== vorbis"; unpack "libvorbis-$VORBIS.tar.gz" vorbis
# The current linker rejects an option libvorbis 1.3.7 still passes on macOS.
sed -i '' 's/-force_cpusubtype_ALL//g' "$SRC/vorbis/configure"
(cd "$SRC/vorbis" && ./configure --prefix="$PREFIX" --disable-shared --enable-static --with-ogg="$PREFIX" --disable-examples \
  && make -j$JOBS && make install); }

have libvpx.a || { echo "== libvpx"; unpack "libvpx-$VPX.tar.gz" vpx
TARGET=$(cd "$SRC/vpx" && { ./configure --help || true; } | grep -oE 'arm64-darwin[0-9]+-gcc' | sort -V | tail -1)
(cd "$SRC/vpx" && ./configure --prefix="$PREFIX" --target="$TARGET" --disable-shared --enable-static --disable-examples \
  --disable-unit-tests --disable-docs --disable-tools --enable-vp9-highbitdepth && make -j$JOBS && make install); }

echo "== ffmpeg"; unpack "ffmpeg-$FFMPEG.tar.xz" ffmpeg
(cd "$SRC/ffmpeg" && ./configure --prefix="$ROOT" --pkg-config-flags="--static" \
  --extra-cflags="-I$PREFIX/include -mmacosx-version-min=14.0" --extra-ldflags="-L$PREFIX/lib -mmacosx-version-min=14.0" \
  --extra-libs="-lc++ -lpthread -lm" \
  --enable-gpl --enable-version3 --disable-nonfree \
  --enable-libx264 --enable-libx265 --enable-libmp3lame --enable-libopus --enable-libvorbis --enable-libvpx \
  --enable-videotoolbox --enable-audiotoolbox \
  --disable-ffplay --disable-ffprobe --disable-doc --disable-debug --disable-network --disable-sdl2 --disable-xlib --disable-libxcb \
  && make -j$JOBS && make install)

"$ROOT/bin/ffmpeg" -hide_banner -L | head -3
echo "Built $ROOT/bin/ffmpeg ($(du -h "$ROOT/bin/ffmpeg" | cut -f1))"
