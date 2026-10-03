# Minecraft × Sons of the Forest passthrough: plan

Base: `rehan-remade/universal-modder`, `examples/minecraft-gta5-passthrough` (MIT) and its `mashup-mods` skill.
Status tags: [ESTABLISHED] read in the example's code/README. [PLAUSIBLE] inference. [UNKNOWN] must be checked on the PC.

Assumption (not yet confirmed by the user): Sons of the Forest (SotF) is the host, Minecraft the guest, single-player, Windows.

## What carries over unchanged (the Minecraft side)
[ESTABLISHED] The Fabric mod is host-agnostic. Its contract is:
- WebSocket server on `127.0.0.1:25599`. Host sends `cam` (position, yaw/pitch/roll, fov, player feet), `ground` (solid columns), `clear`, `cmd`, `key`, `slot`, `scroll`. Minecraft sends `hello` and `explosion`.
- Shared memory `Local\MCPassthroughFrame`: three slots of world RGBA, float32 depth and a hand/HUD overlay, with a header describing near/far, flags (reversed Z, bottom-up rows) and the camera pose each frame was rendered with.
- The GTA-only parts are the host-specific messages (`peds`, `projhit`, `portal`, `glide`, `spawnmobs`). Keep `cam`, `ground`, `clear`, `cmd`, `key`, `slot`, `scroll`, `explosion`; drop or adapt the rest.

Open item: the example targets Minecraft Java 26.3 + Fabric. [UNKNOWN] which version the user runs; the mod's mixins are version-specific.

## What must be rebuilt (the host side)
| GTA piece | SotF replacement | Notes |
|---|---|---|
| ScriptHookV ASI (`script.cpp`, 2272 lines, C++) | BepInEx plugin (C#) | [PLAUSIBLE] SotF is Unity IL2CPP, so BepInEx 6 IL2CPP. Plugin is a WebSocket *client* to Minecraft's server, same as the ASI. |
| GTA natives (camera, ground probe, explosions) | Unity API via Il2CppInterop: `Camera.main`, `Physics.Raycast`, SotF's own damage code | SotF internals need an IL2CPP dump (Il2CppDumper / dnSpy on the interop assemblies). [UNKNOWN] names. |
| ReShade add-on (`compositor.cpp`) + `MCPassthrough.fx` | Same, retargeted | [PLAUSIBLE] SotF renders with D3D11, so ReShade add-on depth access should work. [UNKNOWN] depth format and whether it is reversed-Z; the shader has a `HostReversedZ` toggle and `DebugView` to check this. |
| GTA→MC coordinates (`x, z+off, -y`, yaw = 180 − heading) | Unity (left-handed, Y up, +Z forward) → MC | [PLAUSIBLE] X or yaw must be mirrored because Unity is left-handed and Minecraft is right-handed. Verify with a one-block test: look along each axis. |
| Ground = GTA probes → barrier blocks | Raycast grid below the player → barriers | SotF has terrain, caves and buildings; start with terrain only. Keep the example's limits (160 probes/frame, 40 blocks out). |
| GTA events (police, cars, Nether) | SotF equivalents | Design choice, see below. |

## Phases (each ends with something visible)
0. **Recon on the PC (before any code).** Confirm: SotF version, single-player offline, no anti-cheat blocking BepInEx or ReShade, BepInEx IL2CPP loads and logs, ReShade add-on loads in the SotF D3D11 process, RenderDoc capture shows the depth buffer. If any fails, stop and reassess the route.
1. **Minecraft side, no SotF.** Build the Fabric mod; run the example's `host/fakehost.py` to prove frames flow. Adjust to the user's Minecraft version.
2. **Camera link.** BepInEx plugin sends `cam` every frame. Success: Minecraft's view turns with the SotF camera.
3. **First pixels.** Port the compositor; draw Minecraft over SotF. Success: a Minecraft block sits on the ground at the right place and is hidden behind a tree or rock (depth test works).
4. **Ground collision.** Raycast grid → `ground`. Success: Minecraft items/mobs rest on SotF's terrain.
5. **Gameplay crossover.** Pick one or two (user's choice): Minecraft TNT/creepers damage SotF cannibals; Minecraft mobs mixed into SotF's enemy pool; placed blocks become SotF colliders.
6. **Polish.** Lighting/haze matching, latency re-projection, recording.

## Risks
- [UNKNOWN] SotF's current build may change often; IL2CPP names break on updates.
- [UNKNOWN] Unity's depth buffer may be unavailable or MSAA-resolved in ways that need extra ReShade work.
- Two heavy games at once need a strong GPU and RAM. The GTA demo ran both on one PC with an NVIDIA GPU. [ESTABLISHED]
- Scope: the GTA host script is ~2300 lines because of its features. The minimal SotF slice (phases 2–4) should be a few hundred lines.

## Guardrails (from the skill)
Offline single-player only. Publish code and converters, never game assets. Be honest that it is AI-built.

## Needed from the user
1. Minecraft Java version and launcher, and whether Fabric is installed.
2. Host/guest direction confirmed (assumed SotF host).
3. Which gameplay crossover to build first.
4. Windows machine with both games, GPU model, and whether I should write code for you to test (this cloud session cannot run either game).
