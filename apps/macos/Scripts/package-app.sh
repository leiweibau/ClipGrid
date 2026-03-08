#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
X86_BUILD_DIR="$ROOT_DIR/.build/x86_64-apple-macosx/release"
ARM_BUILD_DIR="$ROOT_DIR/.build/arm64-apple-macosx/release"
EXECUTABLE_NAME="ThumbnailGridStudio"
CLI_EXECUTABLE_NAME="thumbnail-grid-studio-cli"
APP_BUNDLE_NAME="Thumbnail Grid Studio"
APP_DIR="$ROOT_DIR/dist/$APP_BUNDLE_NAME.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
TOOLS_DIR="$RESOURCES_DIR/Tools"
SWIFTPM_BUNDLE_NAME="${EXECUTABLE_NAME}_${EXECUTABLE_NAME}.bundle"
SWIFTPM_BUNDLE_SOURCE="$ARM_BUILD_DIR/$SWIFTPM_BUNDLE_NAME"
SWIFTPM_BUNDLE_APP_DEST="$APP_DIR/$SWIFTPM_BUNDLE_NAME"
FFMPEG_X86_DIR="$ROOT_DIR/.cache/ffmpeg-install/x86_64/bin"
FFMPEG_ARM_DIR="$ROOT_DIR/.cache/ffmpeg-install/arm64/bin"
ICON_SOURCE="$ROOT_DIR/icon.png"
ICON_PREPARED="$ROOT_DIR/Resources/AppIconSource.png"
ICONSET_DIR="/tmp/${EXECUTABLE_NAME}.iconset"
ICON_OUTPUT="$ROOT_DIR/Resources/AppIcon.icns"

require_bundled_tool() {
  local path="$1"
  if [ ! -f "$path" ]; then
    echo "Missing bundled FFmpeg tool: $path" >&2
    echo "Run Scripts/build-ffmpeg.sh first." >&2
    exit 1
  fi
}

require_bundled_tool "$FFMPEG_X86_DIR/ffmpeg"
require_bundled_tool "$FFMPEG_X86_DIR/ffprobe"
require_bundled_tool "$FFMPEG_ARM_DIR/ffmpeg"
require_bundled_tool "$FFMPEG_ARM_DIR/ffprobe"

mkdir -p /tmp/swiftpm-module /tmp/clang-module

cd "$ROOT_DIR"
env SWIFTPM_MODULECACHE_OVERRIDE=/tmp/swiftpm-module CLANG_MODULE_CACHE_PATH=/tmp/clang-module swift build -c release --arch x86_64
env SWIFTPM_MODULECACHE_OVERRIDE=/tmp/swiftpm-module CLANG_MODULE_CACHE_PATH=/tmp/clang-module swift build -c release --arch arm64

if [ -f "$ICON_SOURCE" ]; then
  swift "$ROOT_DIR/Scripts/prepare-icon.swift" "$ICON_SOURCE" "$ICON_PREPARED"

  rm -rf "$ICONSET_DIR"
  mkdir -p "$ICONSET_DIR"

  sips -z 16 16 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_16x16.png" >/dev/null
  sips -z 32 32 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_32x32.png" >/dev/null
  sips -z 64 64 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_128x128.png" >/dev/null
  sips -z 256 256 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_256x256.png" >/dev/null
  sips -z 512 512 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$ICON_PREPARED" --out "$ICONSET_DIR/icon_512x512.png" >/dev/null
  cp "$ICON_PREPARED" "$ICONSET_DIR/icon_512x512@2x.png"

  iconutil -c icns "$ICONSET_DIR" -o "$ICON_OUTPUT"
  rm -rf "$ICONSET_DIR"
fi

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"
mkdir -p "$TOOLS_DIR/x86_64"
mkdir -p "$TOOLS_DIR/arm64"
lipo -create \
  "$X86_BUILD_DIR/$EXECUTABLE_NAME" \
  "$ARM_BUILD_DIR/$EXECUTABLE_NAME" \
  -output "$MACOS_DIR/$EXECUTABLE_NAME"
lipo -create \
  "$X86_BUILD_DIR/$CLI_EXECUTABLE_NAME" \
  "$ARM_BUILD_DIR/$CLI_EXECUTABLE_NAME" \
  -output "$MACOS_DIR/$CLI_EXECUTABLE_NAME"
chmod +x "$MACOS_DIR/$CLI_EXECUTABLE_NAME"
cp "$ROOT_DIR/Resources/Info.plist" "$CONTENTS_DIR/Info.plist"
if [ -f "$ICON_OUTPUT" ]; then
  cp "$ICON_OUTPUT" "$RESOURCES_DIR/AppIcon.icns"
fi
if [ -d "$SWIFTPM_BUNDLE_SOURCE" ]; then
  cp -R "$SWIFTPM_BUNDLE_SOURCE" "$SWIFTPM_BUNDLE_APP_DEST"
fi
for tool in ffmpeg ffprobe; do
  if [ -f "$FFMPEG_X86_DIR/$tool" ]; then
    cp "$FFMPEG_X86_DIR/$tool" "$TOOLS_DIR/x86_64/$tool"
    chmod +x "$TOOLS_DIR/x86_64/$tool"
  fi
  if [ -f "$FFMPEG_ARM_DIR/$tool" ]; then
    cp "$FFMPEG_ARM_DIR/$tool" "$TOOLS_DIR/arm64/$tool"
    chmod +x "$TOOLS_DIR/arm64/$tool"
  fi
done

echo "Created $APP_DIR"
