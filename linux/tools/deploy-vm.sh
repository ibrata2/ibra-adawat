#!/bin/bash
# Builds the Linux app for ARM64 and runs it in the test machine's desktop session (ssh host adawat-linux).
set -euo pipefail
cd "$(dirname "$0")/.."
tools/fetch-models.sh
~/.dotnet/dotnet publish src/Adawat.Linux/Adawat.Linux.csproj -r linux-arm64 -c Release --self-contained true -o build/app-arm64 -v quiet -nologo | grep -E "error" || true
rsync -az --delete build/app-arm64/ adawat-linux:adawat/app/
ssh adawat-linux '. ~/adawat/env.sh; export XAUTHORITY=$(ls /run/user/1000/.mutter-Xwaylandauth.* 2>/dev/null | head -1); pkill -x adawat || true; sleep 0.5; (setsid ~/adawat/app/adawat > ~/adawat/app.log 2>&1 < /dev/null &); sleep 5; tail -15 ~/adawat/app.log; pgrep -x adawat >/dev/null && echo running'
