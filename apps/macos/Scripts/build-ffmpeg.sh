#!/bin/bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE_DIR="$ROOT_DIR/.cache/FFmpeg"
BUILD_ROOT="$ROOT_DIR/.cache/ffmpeg-build"
INSTALL_ROOT="$ROOT_DIR/.cache/ffmpeg-install"
SDKROOT="$(xcrun --sdk macosx --show-sdk-path)"
MIN_VERSION="13.0"

clone_source() {
  if [ -d "$SOURCE_DIR/.git" ]; then
    git -C "$SOURCE_DIR" fetch --depth 1 origin
    git -C "$SOURCE_DIR" reset --hard origin/master
  else
    rm -rf "$SOURCE_DIR"
    git clone --depth 1 https://github.com/FFmpeg/FFmpeg "$SOURCE_DIR"
  fi
}

build_arch() {
  local arch="$1"
  local ffmpeg_arch="$2"
  local build_dir="$BUILD_ROOT/$arch"
  local install_dir="$INSTALL_ROOT/$arch"
  local extra_config=()

  mkdir -p "$build_dir" "$install_dir"
  rm -rf "$build_dir"/*

  if [ "$arch" = "arm64" ]; then
    extra_config+=(--enable-cross-compile)
  fi

  (
    cd "$build_dir"
    "$SOURCE_DIR/configure" \
      --prefix="$install_dir" \
      --arch="$ffmpeg_arch" \
      --target-os=darwin \
      --cc=clang \
      --cxx=clang++ \
      --extra-cflags="-arch $arch -mmacosx-version-min=$MIN_VERSION -isysroot $SDKROOT" \
      --extra-ldflags="-arch $arch -mmacosx-version-min=$MIN_VERSION -isysroot $SDKROOT" \
      --disable-shared \
      --enable-static \
      --disable-doc \
      --disable-debug \
      --disable-ffplay \
      --disable-network \
      --disable-autodetect \
      "${extra_config[@]}"
    make -j"$(sysctl -n hw.ncpu)"
    make install
  )
}

clone_source
build_arch "x86_64" "x86_64"
build_arch "arm64" "aarch64"

echo "Built FFmpeg into $INSTALL_ROOT"
