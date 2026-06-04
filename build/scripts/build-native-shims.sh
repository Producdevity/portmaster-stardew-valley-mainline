#!/bin/bash

set -euo pipefail
unset CDPATH

ROOT="$(cd -- "$(dirname "$0")/../.." && pwd)"
IMAGE="${SDV_MAINLINE_NATIVE_IMAGE:-ghcr.io/monkeyx-net/portmaster-build-templates/portmaster-builder:aarch64-latest}"
OUT="${SDV_MAINLINE_NATIVE_OUT:-"$ROOT/build/out/mainline/native-shims"}"

rm -rf "$OUT"
mkdir -p "$OUT"

docker run --rm \
  --platform linux/arm64/v8 \
  -v "$ROOT/build/src/native":/native-src:ro \
  -v "$OUT":/native-out \
  -w /native-out \
  "$IMAGE" \
  bash -lc '
    set -euo pipefail

    src=/native-src
    out=/native-out
    cflags=(-shared -fPIC -O2 -s)

    build_shim() {
      local source="$1"
      local output="$2"
      shift 2

      gcc "${cflags[@]}" -o "$out/$output" "$src/$source" "$@"
    }

    build_shim imm32_shim.c Imm32.dll
    cp "$out/Imm32.dll" "$out/libImm32.dll.so"

    build_shim user32_shim.c user32.dll
    cp "$out/user32.dll" "$out/libuser32.dll.so"

    build_shim sdl2_clipboard_shim.c SDL2.dll -ldl
    cp "$out/SDL2.dll" "$out/libSDL2.dll.so"

    build_shim steam_api64_stub.c libsteam_api64.so
    build_shim sdkencryptedappticket64_stub.c libsdkencryptedappticket64.so

    for binary in "$out"/*; do
      file "$binary"
      if strings "$binary" | grep -Eq "GLIBC_2\.(3[4-9]|[4-9][0-9])"; then
        echo "Native shim requires a too-new glibc: $binary" >&2
        strings "$binary" | grep -Eo "GLIBC_[0-9]+\.[0-9]+" | sort -Vu >&2
        exit 1
      fi
    done
  '
