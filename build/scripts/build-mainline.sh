#!/bin/bash

set -euo pipefail
unset CDPATH

ROOT="$(cd -- "$(dirname "$0")/../.." && pwd)"
IMAGE="${SDV_MAINLINE_IMAGE:-sdv-mainline-build:latest}"
GAME_DATA_DIR="${SDV_GAME_DIR:-"$ROOT/stardewvalley_steam/Stardew Valley"}"
MONOGAME_DIR="${MONOGAME_DIR:-"$ROOT/../MonoGame"}"
SKIP_IMAGE=0
SKIP_VALIDATION=0
TRACE_VALIDATION=0

usage() {
  cat >&2 <<EOF
Usage: $0 [--skip-image] [--skip-validation] [--trace-validation]

Environment:
  SDV_GAME_DIR       Steam mainline game directory.
  MONOGAME_DIR       Patched MonoGame checkout.
  SDV_MAINLINE_IMAGE Docker image for managed build/validation.
EOF
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --skip-image)
      SKIP_IMAGE=1
      ;;
    --skip-validation)
      SKIP_VALIDATION=1
      ;;
    --trace-validation)
      TRACE_VALIDATION=1
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
  shift
done

if [ "$SKIP_IMAGE" -eq 0 ]; then
  "$ROOT/build/scripts/build-image.sh"
  "$ROOT/build/scripts/build-native-image.sh"
fi

if [ ! -d "$MONOGAME_DIR/MonoGame.Framework" ]; then
  echo "Missing patched MonoGame checkout at $MONOGAME_DIR" >&2
  echo "Set MONOGAME_DIR or place the checkout at ../MonoGame." >&2
  exit 1
fi
MONOGAME_DIR="$(cd -- "$MONOGAME_DIR" && pwd -P)"

MONOGAME_ROOT="$MONOGAME_DIR" "$ROOT/build/scripts/bootstrap-monogame-thirdparty.sh"

if [ ! -d "$GAME_DATA_DIR" ]; then
  echo "Missing Steam mainline game files at $GAME_DATA_DIR" >&2
  echo "Set SDV_GAME_DIR or create stardewvalley_steam/Stardew Valley." >&2
  exit 1
fi
GAME_DATA_DIR="$(cd -- "$GAME_DATA_DIR" && pwd -P)"

"$ROOT/build/scripts/build-native-shims.sh"

inner_args=(./build/scripts/package-mainline.sh --inside-docker)
if [ "$SKIP_VALIDATION" -eq 1 ]; then
  inner_args+=(--skip-validation)
fi
if [ "$TRACE_VALIDATION" -eq 1 ]; then
  inner_args+=(--trace-validation)
fi

docker run --rm \
  --platform linux/arm64/v8 \
  -e HOME=/tmp/sdv-mainline-home \
  -e MONOGAME_ROOT=/monogame \
  -v "$ROOT":/workspace \
  -v "$MONOGAME_DIR:/monogame" \
  -v "$GAME_DATA_DIR:/workspace/stardewvalley_steam/Stardew Valley:ro" \
  -w /workspace \
  "$IMAGE" \
  bash "${inner_args[@]}"
