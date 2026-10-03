# Sons of the Forest plugin (BepInEx 6 IL2CPP)

Status: **skeleton, never compiled.** It was written without the game's interop assemblies. It sends the camera and
the ground to the Minecraft mod (`mc/`), mirroring the GTA example's `cam` and `ground` messages.

## Build (on the PC with the game, after BepInEx has run once)
1. Install the .NET 6+ SDK.
2. `cd sotf && dotnet build -p:GameDir="<folder with SonsOfTheForest.exe>"`
   - It references `BepInEx\core\*.dll` and `BepInEx\interop\*.dll`, which BepInEx creates on first launch.
   - On success it copies `SotfPassthrough.dll` into `BepInEx\plugins`.
3. Start Minecraft (with the passthrough mod), then Sons of the Forest. The BepInEx log should say "Minecraft passthrough loaded".

Expect compile errors: IL2CPP interop signatures (for example `Physics.Raycast` with `out`) differ by interop version. Send me the errors.

## Axis test (needed to confirm the coordinate mapping)
Stand still, turn to look along +Z, then +X, in Sons of the Forest. Minecraft's view should turn the same way, not mirrored.

## Known gaps
- Player feet are camera minus 1.6 m; find the real player transform from the interop assemblies.
- Ground probes may hit trees and the player; layers need tuning.
- No compositing yet (needs the ReShade add-on port), no input, no events (TNT, blocks, axe).
