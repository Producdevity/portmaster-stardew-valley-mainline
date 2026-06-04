#!/bin/bash

set -euo pipefail

IMAGE="${SDV_MAINLINE_NATIVE_IMAGE:-ghcr.io/monkeyx-net/portmaster-build-templates/portmaster-builder:aarch64-latest}"

docker pull --platform linux/arm64 "$IMAGE"
