#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="$(uname -m)"
BUILD_DIR="$ROOT_DIR/.build/$ARCH-apple-macosx/release"
APP_NAME="ClipGrid"
APP_DIR="$ROOT_DIR/dist/$APP_NAME.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
SWIFTPM_BUNDLE_NAME="${APP_NAME}_${APP_NAME}.bundle"
SWIFTPM_BUNDLE_SOURCE="$BUILD_DIR/$SWIFTPM_BUNDLE_NAME"
SWIFTPM_BUNDLE_APP_DEST="$APP_DIR/$SWIFTPM_BUNDLE_NAME"
ICON_SOURCE="$ROOT_DIR/icon.png"
ICON_PREPARED="$ROOT_DIR/Resources/AppIconSource.png"
ICONSET_DIR="/tmp/${APP_NAME}.iconset"
ICON_OUTPUT="$ROOT_DIR/Resources/AppIcon.icns"

mkdir -p /tmp/swiftpm-module /tmp/clang-module

cd "$ROOT_DIR"
env SWIFTPM_MODULECACHE_OVERRIDE=/tmp/swiftpm-module CLANG_MODULE_CACHE_PATH=/tmp/clang-module swift build -c release

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
cp "$BUILD_DIR/$APP_NAME" "$MACOS_DIR/$APP_NAME"
cp "$ROOT_DIR/Resources/Info.plist" "$CONTENTS_DIR/Info.plist"
if [ -f "$ICON_OUTPUT" ]; then
  cp "$ICON_OUTPUT" "$RESOURCES_DIR/AppIcon.icns"
fi
if [ -d "$SWIFTPM_BUNDLE_SOURCE" ]; then
  cp -R "$SWIFTPM_BUNDLE_SOURCE" "$SWIFTPM_BUNDLE_APP_DEST"
fi

echo "Created $APP_DIR"
