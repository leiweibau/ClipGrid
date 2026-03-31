#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE_DIR="$ROOT_DIR/.cache/FFmpeg"
BUILD_ROOT="$ROOT_DIR/.cache/ffmpeg-build"
INSTALL_ROOT="$ROOT_DIR/.cache/ffmpeg-install"
SDKROOT="$(xcrun --sdk macosx --show-sdk-path)"
MIN_VERSION="13.0"
FFMPEG_REF="30a811cc7d878184646e35fb94e72c5b964d575e"
FFMPEG_REMOTE_URL="https://github.com/FFmpeg/FFmpeg"

clone_source() {
  if [ -d "$SOURCE_DIR/.git" ]; then
    git -C "$SOURCE_DIR" fetch --depth 1 origin "$FFMPEG_REF"
  else
    rm -rf "$SOURCE_DIR"
    git init "$SOURCE_DIR" >/dev/null
    git -C "$SOURCE_DIR" remote add origin "$FFMPEG_REMOTE_URL"
    git -C "$SOURCE_DIR" fetch --depth 1 origin "$FFMPEG_REF"
  fi

  git -C "$SOURCE_DIR" checkout --detach "$FFMPEG_REF" >/dev/null
}

build_arch() {
  local arch="$1"
  local ffmpeg_arch="$2"
  local build_dir="$BUILD_ROOT/$arch"
  local install_dir="$INSTALL_ROOT/$arch"
  local -a configure_args=()

  mkdir -p "$build_dir" "$install_dir"
  rm -rf "$build_dir"/*

  if [ "$arch" = "arm64" ]; then
    configure_args+=(--enable-cross-compile)
  fi

  configure_args+=(
    --prefix="$install_dir"
    --arch="$ffmpeg_arch"
    --target-os=darwin
    --cc=clang
    --cxx=clang++
    --extra-cflags="-arch $arch -mmacosx-version-min=$MIN_VERSION -isysroot $SDKROOT"
    --extra-ldflags="-arch $arch -mmacosx-version-min=$MIN_VERSION -isysroot $SDKROOT"
    --disable-shared
    --enable-static
    --disable-doc
    --disable-debug
    --disable-ffplay
    --disable-network
    --disable-autodetect
  )

  (
    cd "$build_dir"
    "$SOURCE_DIR/configure" "${configure_args[@]}"
    make -j"$(sysctl -n hw.ncpu)"
    make install
  )
}

clone_source
build_arch "x86_64" "x86_64"
build_arch "arm64" "aarch64"

echo "Built FFmpeg $FFMPEG_REF into $INSTALL_ROOT"
