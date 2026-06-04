# Build Workflow

This directory contains the build scripts and validation fixtures for the Stardew Valley Mainline PortMaster package.

## Inputs

- `../MonoGame`: patched MonoGame source used to build `MonoGame.Framework.dll`.
  Use the
  [`portmaster-stardew-mainline`](https://github.com/Producdevity/MonoGame/tree/portmaster-stardew-mainline)
  branch of `Producdevity/MonoGame`.
- `portmaster_stardewvalley_mainline`: package template copied into the staged port.
- `stardewvalley_steam/Stardew Valley`: optional local Steam game files used for validation only.
- SMAPI 4.5.2, .NET 6.0.32, and SkiaSharp native assets: downloaded into `build/out/mainline/cache` during packaging.

The release zip does not include Stardew Valley retail files.

## Commands

Build the package:

```bash
./build/scripts/build-mainline.sh
```

Build and keep syscall traces from validation:

```bash
./build/scripts/build-mainline.sh --trace-validation
```

Run validation against an existing staged build:

```bash
./build/scripts/validate-mainline.sh --skip-image
```

Regenerate MonoGame API compatibility reports:

```bash
csi build/scripts/audit-monogame-api.csx
```

Scan candidate SMAPI mods for direct references to missing MonoGame APIs:

```bash
csi build/scripts/scan-mod-monogame-refs.csx /path/to/Mods
```

## Generated Files

- `build/out/`: staged artifacts, package tree, release zip, cache, and validation logs.
- `build/reports/`: API and mod compatibility reports.
- `build/**/bin/` and `build/**/obj/`: .NET build output.

These paths are generated and should not be committed.

## Validation

The package build performs these checks before producing the release zip:

- Builds the patched MonoGame framework.
- Builds the native ARM64 shim libraries.
- Builds `MainlineGameDataPatcher`.
- Downloads and validates the official SMAPI 4.5.2 payload.
- Verifies required MonoGame compatibility APIs used by Stardew 1.6.
- Runs vanilla and SMAPI smoke tests under Linux ARM64.
- Confirms SMAPI default mods do not enable SMAPI mode by themselves.
- Runs `FixtureSmapiMod` to validate SMAPI loading and dynamic audio cue injection for WAV, decoded OGG, and streamed OGG.
- Checks that the release archive excludes retail game files and validation fixtures.

Device testing is still required for input, audio, rendering, saves, and performance.

## On-Device Preparation

At launch, `MainlineGameDataPatcher` prepares the user-copied Steam files in `gamedata` by:

- Rewriting `.deps.json` and `.runtimeconfig.json` for the bundled .NET 6 runtime.
- Removing conflicting Windows/native desktop files.
- Copying the rebuilt `MonoGame.Framework.dll`.
- Applying Stardew-specific IL rewrites.
- Staging SMAPI files when SMAPI mode is active.
- Normalizing managed assemblies so CoreCLR ARM64 can load them.

SMAPI mode is selected only when `ports/stardewvalleymainline/Mods` contains a user mod beyond SMAPI's bundled defaults.
