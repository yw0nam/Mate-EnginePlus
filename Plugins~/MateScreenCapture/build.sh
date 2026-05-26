#!/usr/bin/env bash
# Build MateScreenCapture.bundle (universal arm64 + x86_64) for Unity macOS plugin slot.
# Output: ../../Assets/Plugins/macOS/MateScreenCapture.bundle/

set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
SRC="$SCRIPT_DIR/Sources/MateScreenCapture.swift"
NAME="MateScreenCapture"
OUT="$SCRIPT_DIR/../../Assets/Plugins/macOS/${NAME}.bundle"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

ARM64="$TMP/${NAME}_arm64.dylib"
X86_64="$TMP/${NAME}_x86_64.dylib"

echo "[build] compiling arm64..."
xcrun -sdk macosx swiftc -emit-library -o "$ARM64" \
  -target arm64-apple-macos12.3 \
  -framework ScreenCaptureKit -framework AppKit -framework Foundation -framework CoreGraphics \
  -O \
  "$SRC"

echo "[build] compiling x86_64..."
xcrun -sdk macosx swiftc -emit-library -o "$X86_64" \
  -target x86_64-apple-macos12.3 \
  -framework ScreenCaptureKit -framework AppKit -framework Foundation -framework CoreGraphics \
  -O \
  "$SRC"

echo "[build] assembling .bundle at $OUT"
rm -rf "$OUT"
mkdir -p "$OUT/Contents/MacOS"
lipo -create "$ARM64" "$X86_64" -output "$OUT/Contents/MacOS/${NAME}"

cat > "$OUT/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>${NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>com.mateengine.${NAME}</string>
    <key>CFBundleName</key>
    <string>${NAME}</string>
    <key>CFBundlePackageType</key>
    <string>BNDL</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.3</string>
</dict>
</plist>
EOF

echo "[build] ad-hoc signing..."
codesign --force --sign - --timestamp=none "$OUT/Contents/MacOS/${NAME}"
codesign --force --sign - --timestamp=none "$OUT"

echo "[build] done: $OUT"
file "$OUT/Contents/MacOS/${NAME}"
