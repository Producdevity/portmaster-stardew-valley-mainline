#!/bin/bash

set -euo pipefail
unset CDPATH

ROOT="$(cd -- "$(dirname "$0")/../.." && pwd)"
IMAGE="${SDV_MAINLINE_IMAGE:-sdv-mainline-build:latest}"

docker build \
  --platform linux/arm64/v8 \
  -t "$IMAGE" \
  -f "$ROOT/build/docker/mainline/Dockerfile" \
  "$ROOT/build/docker/mainline"
