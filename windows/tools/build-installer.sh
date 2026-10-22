#!/bin/zsh
# Builds Adawat-Setup-<arch>.exe for Windows ARM64 and x64.
#   windows/tools/build-installer.sh [arm64] [x64]      (default: both)
# The app is published here with the .NET SDK; the installer is compiled by Inno Setup 6 on a Windows machine reached
# over SSH (ADAWAT_WIN_HOST, default "adawat-win"), and the finished setups come back to windows/build/installer.
set -e
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
HOST=${ADAWAT_WIN_HOST:-adawat-win}
ARCHES=("$@"); (( $#ARCHES )) || ARCHES=(arm64 x64)
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
REMOTE='C:\Adawat\pkg'
mkdir -p build/installer

ssh $HOST "Remove-Item -Recurse -Force $REMOTE -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force $REMOTE\\windows\\build\\publish, $REMOTE\\windows\\build\\installer | Out-Null"
scp -q -r installer assets $HOST:"C:/Adawat/pkg/windows/"
scp -q ../LICENSE ../THIRD-PARTY-NOTICES.md $HOST:"C:/Adawat/pkg/"

for arch in $ARCHES; do
  out=build/publish/app-$arch
  [[ -f build/ffmpeg/win-$arch/ffmpeg.exe ]] || { echo "Missing build/ffmpeg/win-$arch/ffmpeg.exe: run windows/tools/fetch-ffmpeg.sh"; exit 1; }
  echo "Publishing $arch…"
  rm -rf $out
  for p in Adawat.App Adawat.Cli; do
    # adawat-cli also builds for Linux, so the Windows framework is named.
    dotnet publish src/$p/$p.csproj -f net10.0-windows10.0.22621.0 -c Release -r win-$arch --self-contained -p:DebugType=none -o $out 2>&1 | grep -E " error " | sed 's| \[/.*||' || true
  done
  [[ -f $out/Adawat.exe && -f $out/adawat-cli.exe ]] || { echo "Publish failed for $arch"; exit 1; }
  mkdir -p $out/tools
  cp build/ffmpeg/win-$arch/ffmpeg.exe $out/tools/
  cp build/ffmpeg/win-$arch/LICENSE.txt $out/tools/ffmpeg-LICENSE.txt
  echo "Copying $arch to $HOST…"
  scp -q -r $out $HOST:"C:/Adawat/pkg/windows/build/publish/"
  echo "Compiling the $arch installer…"
  ssh $HOST "& \"\$env:LOCALAPPDATA\\Programs\\Inno Setup 6\\ISCC.exe\" /Q /DArch=$arch /DAppVersion=$VERSION /DSource=$REMOTE\\windows\\build\\publish\\app-$arch $REMOTE\\windows\\installer\\Adawat.iss; if (\$LASTEXITCODE) { exit \$LASTEXITCODE }"
  scp -q $HOST:"C:/Adawat/pkg/windows/build/installer/Adawat-Setup-$arch.exe" build/installer/
  ls -la build/installer/Adawat-Setup-$arch.exe
done
(cd build/installer && shasum -a 256 Adawat-Setup-*.exe)
