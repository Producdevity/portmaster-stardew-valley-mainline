#!/bin/bash

set -euo pipefail
unset CDPATH

ROOT="$(cd -- "$(dirname "$0")/../.." && pwd)"
SOURCE_PACKAGE="$ROOT/build/out/mainline/release-package"
DIAGNOSTIC_PACKAGE="$ROOT/build/out/mainline/diagnostic-package"
RELEASE_DIR="$ROOT/build/out/mainline/release"
ARCHIVE="$RELEASE_DIR/stardewvalleymainline_diagnostic.zip"
CHECKSUM="$ARCHIVE.sha256"
LISTING="$RELEASE_DIR/stardewvalleymainline_diagnostic.contents.txt"

if [ ! -d "$SOURCE_PACKAGE" ]; then
  echo "Missing release package at $SOURCE_PACKAGE. Run build/scripts/build-mainline.sh first." >&2
  exit 1
fi

rm -rf "$DIAGNOSTIC_PACKAGE"
mkdir -p "$DIAGNOSTIC_PACKAGE" "$RELEASE_DIR"
cp -R "$SOURCE_PACKAGE/." "$DIAGNOSTIC_PACKAGE/"
touch "$DIAGNOSTIC_PACKAGE/stardewvalleymainline/profiling.enabled"

rm -f "$ARCHIVE" "$CHECKSUM" "$LISTING"
(
  cd "$DIAGNOSTIC_PACKAGE"
  zip -qr "$ARCHIVE" .
)
(
  cd "$RELEASE_DIR"
  sha256sum "$(basename "$ARCHIVE")" > "$(basename "$CHECKSUM")"
  unzip -l "$(basename "$ARCHIVE")" > "$(basename "$LISTING")"
)

echo "$DIAGNOSTIC_PACKAGE"
echo "$ARCHIVE"
