#!/bin/bash

set -euo pipefail
unset CDPATH

ROOT="$(cd -- "$(dirname "$0")/../.." && pwd)"
DEFAULT_MONOGAME_ROOT="$ROOT/../MonoGame"
MONOGAME_ROOT="${MONOGAME_ROOT:-"$DEFAULT_MONOGAME_ROOT"}"
MONOGAME_ROOT="$(cd -- "$MONOGAME_ROOT" && pwd -P)"
NVORBIS_ROOT="$MONOGAME_ROOT/ThirdParty/NVorbis"
NVORBIS_TARGET="$NVORBIS_ROOT/NVorbis"
NVORBIS_CACHE_ROOT="$ROOT/build/cache/nvorbis"
NVORBIS_REPO_URL="https://github.com/NVorbis/NVorbis.git"
NVORBIS_COMMIT="519d4e2aae7d6a4d5bab552ec5c1e517e9c78855"

require_file() {
  local path="$1"
  local hint="$2"
  if [ ! -f "$path" ]; then
    echo "Missing required source file: $path" >&2
    echo "$hint" >&2
    exit 1
  fi
}

checkout_public_submodules() {
  git -C "$MONOGAME_ROOT" submodule update --init \
    ThirdParty/Dependencies \
    ThirdParty/SDL_GameControllerDB \
    ThirdParty/StbImageSharp \
    ThirdParty/StbImageWriteSharp
}

bootstrap_nvorbis() {
  if [ -f "$NVORBIS_TARGET/VorbisReader.cs" ]; then
    return
  fi

  mkdir -p "$NVORBIS_ROOT" "$NVORBIS_CACHE_ROOT"
  if [ -f "$NVORBIS_CACHE_ROOT/NVorbis/VorbisReader.cs" ]; then
    find "$NVORBIS_TARGET" -mindepth 1 -maxdepth 1 -exec rm -rf {} + 2>/dev/null || true
    mkdir -p "$NVORBIS_TARGET"
    cp -R "$NVORBIS_CACHE_ROOT/NVorbis/." "$NVORBIS_TARGET/"
    if [ -f "$NVORBIS_CACHE_ROOT/.bootstrap-revision" ]; then
      cp "$NVORBIS_CACHE_ROOT/.bootstrap-revision" "$NVORBIS_ROOT/.bootstrap-revision"
    fi
    if [ -f "$NVORBIS_CACHE_ROOT/.bootstrap-source" ]; then
      cp "$NVORBIS_CACHE_ROOT/.bootstrap-source" "$NVORBIS_ROOT/.bootstrap-source"
    fi
    return
  fi

  local tmpdir
  tmpdir="$(mktemp -d "${TMPDIR:-/tmp}/nvorbis-bootstrap.XXXXXX")"
  trap 'rm -rf "$tmpdir"' EXIT

  git clone --depth 1 "$NVORBIS_REPO_URL" "$tmpdir/repo"
  git -C "$tmpdir/repo" checkout "$NVORBIS_COMMIT" >/dev/null 2>&1

  find "$NVORBIS_TARGET" -mindepth 1 -maxdepth 1 -exec rm -rf {} + 2>/dev/null || true
  mkdir -p "$NVORBIS_TARGET"
  cp -R "$tmpdir/repo/NVorbis/." "$NVORBIS_TARGET/"

  git -C "$tmpdir/repo" rev-parse HEAD > "$NVORBIS_ROOT/.bootstrap-revision"
  cat > "$NVORBIS_ROOT/.bootstrap-source" <<EOF
$NVORBIS_REPO_URL
$NVORBIS_COMMIT
EOF

  rm -rf "$NVORBIS_CACHE_ROOT"
  mkdir -p "$NVORBIS_CACHE_ROOT"
  cp -R "$tmpdir/repo/." "$NVORBIS_CACHE_ROOT/"
  rm -rf "$tmpdir"
  trap - EXIT
}

checkout_public_submodules
bootstrap_nvorbis

require_file \
  "$MONOGAME_ROOT/ThirdParty/StbImageSharp/src/StbImageSharp/ImageResult.cs" \
  "StbImageSharp was not checked out correctly."
require_file \
  "$MONOGAME_ROOT/ThirdParty/StbImageWriteSharp/src/StbImageWriteSharp/ImageWriter.cs" \
  "StbImageWriteSharp was not checked out correctly."
require_file \
  "$MONOGAME_ROOT/ThirdParty/Dependencies/openal-soft/Linux/x64/libopenal.so.1" \
  "MonoGame dependencies were not checked out correctly."
require_file \
  "$NVORBIS_TARGET/VorbisReader.cs" \
  "NVorbis bootstrap did not populate the expected source tree."
