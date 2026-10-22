#!/bin/bash
# Renders every tool window on the test machine (ssh host adawat-linux) and brings back contact sheets in build/snapshots.
set -euo pipefail
cd "$(dirname "$0")/.."
lang=${1:-en}
mkdir -p build/snapshots
ssh adawat-linux "bash -s" "$lang" <<'REMOTE'
lang=$1
. ~/adawat/env.sh; export XAUTHORITY=$(ls /run/user/1000/.mutter-Xwaylandauth.* | head -1)
mkdir -p ~/snaps/$lang; rm -f ~/snaps/$lang/*; cd ~/t; A=~/adawat/app/adawat
for spec in "compress photo.jpg" "metadata photo.jpg" "edit photo.jpg" "annotate photo.jpg" "frame photo.jpg" "crop photo.jpg" "redact photo.jpg" "collage photo.jpg photo.png" "organize doc.pdf" "merge doc.pdf note.pdf" "trim clip.mp4" "crop-video clip.mp4" "speed clip.mp4" "frames clip.mp4" "split clip.mp4" "redact-video clip.mp4" "join clip.mp4 clip.mov" "normalize tone.wav" "visualizer tone.wav" "channels tone.wav" "bleep tone.wav" "qr note.md" "settings" "welcome0" "welcome1" "welcome2" "welcome3"; do
  set -- $spec; name=$1; shift
  timeout 60 $A --snapshot-window $name ~/snaps/$lang/$name.png "$@" --lang $lang >/dev/null 2>~/snaps/$lang/$name.err || echo "FAIL $name: $(tail -2 ~/snaps/$lang/$name.err | tr '\n' ' ' | cut -c1-200)"
done
for mode in convert tools actions; do timeout 60 $A --snapshot-wheel $mode ~/snaps/$lang/wheel-$mode.png photo.jpg 1 --lang $lang >/dev/null 2>&1 || echo "FAIL wheel $mode"; done
cd ~/snaps/$lang; for f in *.png; do mogrify -strip -density 72 -units PixelsPerInch "$f"; done
montage compress.png metadata.png edit.png annotate.png frame.png crop.png redact.png collage.png -tile 4x2 -geometry 480x480+6+6 -background '#ddd' sheet1.png
montage organize.png merge.png trim.png crop-video.png speed.png frames.png split.png redact-video.png -tile 4x2 -geometry 480x480+6+6 -background '#ddd' sheet2.png
montage join.png normalize.png visualizer.png channels.png bleep.png qr.png settings.png welcome0.png welcome1.png welcome2.png -tile 5x2 -geometry 400x400+6+6 -background '#ddd' sheet3.png
montage wheel-convert.png wheel-tools.png wheel-actions.png welcome3.png -tile 4x1 -geometry 400x400+6+6 -background '#ddd' sheet4.png
REMOTE
rsync -az "adawat-linux:snaps/$lang/" "build/snapshots/$lang/"
ls build/snapshots/$lang | grep sheet
