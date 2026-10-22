#!/bin/bash
# Checks an installed Adawat package on a machine without a desktop: makes sample files, runs every wheel choice
# adawat-cli can run, starts the app under Xvfb, and renders a few windows. Run it on the test machine after
# installing the .deb, .rpm or AppImage (pass the AppImage's path to test that instead).
set -u
app=${1:-adawat}
cli=adawat-cli
work=$(mktemp -d)
cd "$work"
echo "== $(. /etc/os-release; echo "$PRETTY_NAME") · $(uname -m)"
"$app" --version
convert -size 800x600 gradient:"#7F70FF-#3B2AA8" -fill white -pointsize 48 -annotate +60+120 "Adawat Test 123" photo.jpg
ffmpeg -loglevel error -f lavfi -i testsrc=size=640x360:rate=30 -f lavfi -i sine=frequency=440 -t 3 -c:v libx264 -pix_fmt yuv420p -c:a aac clip.mp4 2>/dev/null \
  || ffmpeg -loglevel error -f lavfi -i testsrc=size=640x360:rate=30 -f lavfi -i sine=frequency=440 -t 3 -pix_fmt yuv420p clip.mp4
ffmpeg -loglevel error -f lavfi -i sine=frequency=330 -t 3 tone.wav
printf "# مرحبا\n\nهذا **اختبار**.\n" > note.md
printf "name,value\nأحمد,1\n" > sheet.csv
convert photo.jpg doc.pdf
mkdir folder && cp note.md folder/
pass=0; fail=0
if command -v $cli >/dev/null; then
  $cli doctor | sed -n '1,4p'
  for f in photo.jpg clip.mp4 tone.wav note.md sheet.csv doc.pdf folder; do
    for id in $($cli list $f | sed "s/^[A-Za-z]*: //" | tr " " "\n" | grep -v -E "^action\.(share|float|move|terminal|copy|paths)$"); do
      out=$(timeout 300 $cli run $id $f 2>&1 </dev/null); code=$?
      if [ $code -eq 0 ]; then pass=$((pass+1));
      elif echo "$out" | grep -q -E "needs its window|No QR codes|already as small|has no pages"; then :;
      else fail=$((fail+1)); echo "FAIL $id $f: $(echo "$out" | tail -1 | cut -c1-200)"; fi
    done
  done
  echo "cli: passed $pass, failed $fail"
fi
# The app itself, with no display of its own: render windows to pictures under Xvfb.
for name in compress trim qr settings; do
  file=photo.jpg; [ $name = trim ] && file=clip.mp4; [ $name = qr ] && file=note.md
  timeout 120 xvfb-run -a -s "-screen 0 1280x800x24" "$app" --snapshot-window $name "$work/$name.png" "$work/$file" >/dev/null 2>"$work/$name.err" \
    && echo "window $name: $(identify -format '%wx%h' "$work/$name.png")" || echo "FAIL window $name: $(tail -2 "$work/$name.err" | tr '\n' ' ')"
done
timeout 120 xvfb-run -a -s "-screen 0 1280x800x24" "$app" --snapshot-wheel tools "$work/wheel.png" "$work/photo.jpg" 1 >/dev/null 2>&1 && echo "wheel: ok" || echo "FAIL wheel"
echo "work folder: $work"
