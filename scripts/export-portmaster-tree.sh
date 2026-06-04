#!/bin/bash

set -euo pipefail
unset CDPATH

ROOT="$(cd -- "$(dirname "$0")/.." && pwd)"
DEST_ROOT="${1:-"$ROOT/../portmaster-new"}"
PORT_NAME="stardewvalleymainline"
PACKAGE_ROOT="$ROOT/build/out/mainline/release-package"
SOURCE_PORT_DIR="$PACKAGE_ROOT/$PORT_NAME"
DEST_PORT_DIR="$DEST_ROOT/ports/$PORT_NAME"

if [ ! -d "$SOURCE_PORT_DIR" ]; then
  echo "Missing built release package at $SOURCE_PORT_DIR" >&2
  echo "Run ./build/scripts/build-mainline.sh first." >&2
  exit 1
fi

mkdir -p "$DEST_PORT_DIR"
find "$DEST_PORT_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
mkdir -p "$DEST_PORT_DIR/$PORT_NAME"

cp "$PACKAGE_ROOT/StardewValleyMainline.sh" "$DEST_PORT_DIR/StardewValleyMainline.sh"
cp "$SOURCE_PORT_DIR/port.json" "$DEST_PORT_DIR/port.json"
cp "$SOURCE_PORT_DIR/stardewvalley.md" "$DEST_PORT_DIR/README.md"
cp "$SOURCE_PORT_DIR/cover.png" "$DEST_PORT_DIR/cover.png"
cp "$SOURCE_PORT_DIR/screenshot.jpg" "$DEST_PORT_DIR/screenshot.jpg"
cp "$SOURCE_PORT_DIR/gameinfo.xml" "$DEST_PORT_DIR/gameinfo.xml"

mkdir -p "$DEST_PORT_DIR/$PORT_NAME"
rsync -a \
  --exclude='port.json' \
  --exclude='stardewvalley.md' \
  --exclude='cover.png' \
  --exclude='screenshot.jpg' \
  --exclude='gameinfo.xml' \
  "$SOURCE_PORT_DIR/" "$DEST_PORT_DIR/$PORT_NAME/"

find "$DEST_PORT_DIR" \( -name '.DS_Store' -o -name '._*' \) -delete

if find "$DEST_PORT_DIR/$PORT_NAME/gamedata" -mindepth 1 ! -name '.gitkeep' | grep -q .; then
  echo "Export contains retail game files under gamedata." >&2
  exit 1
fi

echo "Exported PortMaster tree to $DEST_PORT_DIR"
