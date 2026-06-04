# Contributing

## Local Requirements

- Docker with Linux ARM64 emulation support.
- A legal Steam mainline Stardew Valley install for validation. GOG builds are not currently validated.
- The patched MonoGame checkout at `../MonoGame/`, using the
  [`portmaster-stardew-mainline`](https://github.com/Producdevity/MonoGame/tree/portmaster-stardew-mainline)
  branch of `Producdevity/MonoGame`.

## Workflow

1. Build with `./build/scripts/build-mainline.sh`.
2. Review `build/out/mainline/release/stardewvalleymainline.zip`.
3. Export to the sparse PortMaster-New checkout with `./scripts/export-portmaster-tree.sh`.
4. Run PortMaster repository checks from that checkout.
5. Test on hardware before opening or updating a PortMaster PR.
