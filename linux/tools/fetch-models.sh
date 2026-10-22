#!/bin/bash
# Fetches the face detector the Linux app ships in models/ (Ultra-Light-Fast-Generic-Face-Detector RFB-640, MIT licence,
# from the ONNX model zoo), pinned by its SHA-256.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/models
target=build/models/face-detector.onnx
sha=8f4c659275977e7a3bfbfa339a9c769ad793df50f9c0baa8c14b11baa1646430
if [ -f "$target" ] && echo "$sha  $target" | shasum -a 256 -c - >/dev/null 2>&1; then exit 0; fi
curl -fsSL -o "$target" "https://github.com/onnx/models/raw/main/validated/vision/body_analysis/ultraface/models/version-RFB-640.onnx"
echo "$sha  $target" | shasum -a 256 -c -
