## Notes

Thanks to [ConcernedApe](https://www.stardewvalley.net/) for creating Stardew Valley.

Thanks to the [MonoGame](https://github.com/MonoGame/MonoGame) project for the framework that makes this port possible.

Thanks to JohnnyonFlame for the original Stardew Valley PortMaster work and help with this mainline port.

Mainline port work by Producdevity.

This port requires the Windows Steam version of Stardew Valley. Install the regular mainline Steam build, then copy all files from the Stardew Valley install folder into `ports/stardewvalleymainline/gamedata`.

Make sure Stardew Valley is not opted into the legacy compatibility beta. The port patches the copied game files automatically on launch.

SMAPI is bundled. Leave `ports/stardewvalleymainline/Mods` empty for vanilla play, or copy SMAPI mods into that folder to launch through SMAPI automatically. Do not place user mods inside `gamedata/Mods`.

SMAPI support is experimental. Some mods may not work on Linux ARM handhelds, especially mods with native Windows or x64 dependencies.

## Controls

| Button | Action |
|--|--|
|Left Stick / D-Pad|Move / menu navigation|
|A|Confirm / interact|
|B|Cancel / back|
|X|Use tool|
|Y|Open menu|
|L1 / R1|Change toolbar item|
|Start|Pause|
