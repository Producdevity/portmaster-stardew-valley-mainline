# Stardew Valley Mainline for PortMaster

This repository builds the PortMaster package for the regular Steam mainline build of Stardew Valley. It does not target the legacy compatibility branch.

Test builds are published on the
[GitHub releases page](https://github.com/Producdevity/portmaster-stardew-valley-mainline/releases).

## Repository Layout

- This repository contains the package template, build scripts, patcher source, native
  shims, validation fixtures, and maintainer documentation.
- `../MonoGame/` is the patched MonoGame fork used by this port. It should be
  checked out from
  [`Producdevity/MonoGame`](https://github.com/Producdevity/MonoGame/tree/portmaster-stardew-mainline)
  on the `portmaster-stardew-mainline` branch and kept as a sibling Git checkout
  so its history and upstream diff remain reviewable.
- `../portmaster-new/` is a sparse checkout of `Producdevity/PortMaster-New` used
  only to prepare the final PortMaster PR tree.

For local validation, either set `SDV_GAME_DIR` to the Steam mainline install or place/symlink it at:

```text
stardewvalley_steam/Stardew Valley/
```

## Runtime Policy

Stardew Valley mainline targets `net6.0`, so the release package uses a side-by-side
.NET 6 runtime. Do not force the game onto PortMaster's .NET 8 runtime with major
roll-forward for release builds.

The package uses Microsoft's `dotnet-runtime-6.0.32-linux-arm64` framework runtime
layout. Keep the runtime layout intact unless the result is validated with vanilla
Stardew, SMAPI, and representative mods; Stardew, SMAPI, Harmony, and mods use
reflection and dynamic loading.

## Package Layout

The PortMaster contribution tree is:

```text
ports/stardewvalleymainline/
├── README.md
├── StardewValleyMainline.sh
├── cover.png
├── gameinfo.xml
├── port.json
├── screenshot.jpg
└── stardewvalleymainline/
```

The final install zip places `StardewValleyMainline.sh` beside the
`stardewvalleymainline/` directory. Retail Stardew Valley files are excluded; users
copy their Steam mainline files into `stardewvalleymainline/gamedata`.

`port.json` uses the current PortMaster v4 format. This port is `aarch64`, marked
experimental until broader device coverage is complete, and does not declare a
PortMaster runtime because it currently carries its own .NET 6 runtime.

## SMAPI Support

Vanilla mainline Stardew Valley remains the default when no user mods are present.
When user-installed mods are present, the same launcher starts SMAPI 4.5.2 and uses
`stardewvalleymainline/Mods` as the external mod directory.

This package can launch SMAPI, but individual mods may still fail when they
require a different Stardew or SMAPI version, missing framework mods,
Windows/x64 native binaries, unsupported rendering/input/audio behavior, or
low-level Harmony patches that assume Stardew's exact Steam MonoGame build.

## Build

Before building, ensure the patched MonoGame checkout exists at `../MonoGame/`.
Use the
[`portmaster-stardew-mainline`](https://github.com/Producdevity/MonoGame/tree/portmaster-stardew-mainline)
branch of `Producdevity/MonoGame`.

Build and validate the package:

```bash
./build/scripts/build-mainline.sh
```

Reuse the existing Docker image:

```bash
./build/scripts/build-mainline.sh --skip-image
```

The release zip is written to:

```text
build/out/mainline/release/
```

## PortMaster Tree Export

After a successful build, export the unzipped PortMaster-New tree:

```bash
./scripts/export-portmaster-tree.sh ../portmaster-new
```

Then run the PortMaster checks inside the sparse checkout.

Before submitting a PortMaster PR from the sparse checkout, run:

```bash
tools/prepare_repo.sh
python3 tools/build_release.py --do-check
python3 tools/build_release.py --quick-build stardewvalleymainline
```
