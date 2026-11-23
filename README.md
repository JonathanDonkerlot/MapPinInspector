# MapPinInspector

**MapPinInspector** is a client-side MelonLoader mod for **A Township Tale** (PCVR Mono build) that makes every player pin on in-game **Map Boards** interactive. Touch, poke, or trigger-click any green or red player pin on any Map Board to display a clean **3D world-space nameplate card** anchored directly to that moving pin.

## Features
- **Multi-Board Support:** Automatically attaches to every Map Board in the world, including dynamically loaded boards.
- **VR Tactile & Pointer Interaction:** Poke player pins directly with your left or right index fingertip, point-and-click with your XR trigger, or click with the mouse in desktop view—using non-blocking trigger colliders (`Ignore Raycast` layer) that never interfere with grabs or board touch crystals.
- **Dynamic World-Space Parchment Tag:**
  - Compact aged-parchment card with deckled paper edges displaying only the player's name in crisp ink.
  - Anchored directly to the selected pin so it tracks moving players across the map.
  - Offset cleanly in front of the board surface and billboarded toward your VR camera with zero Z-fighting.
- **Overlapping Pin Clustering:** When multiple players stand near the same map spot, overlapping pins automatically group into a stacked name list—tap again to cycle or walk away to auto-dismiss.
- **Tactile & Audio Feedback:** Subtle pulsing selection ring, synthesized spatial click audio, and gentle XR controller haptic pulse.

## Requirements
- **A Township Tale** (PCVR Mono build, Unity `2020.3.23f1`)
- **MelonLoader v0.7.x** (tested on MelonLoader v0.7.3 Open-Beta, `net35` facade)

## Installation
1. Download `MapPinInspector.dll` from the [Releases](../../releases) page.
2. Place `MapPinInspector.dll` into your game's `Mods\` folder:
   ```text
   <A Township Tale>\Mods\MapPinInspector.dll
   ```
3. Launch the game. A `[MapPinInspector]` configuration section is created automatically in `UserData\MelonPreferences.cfg`.

## Configuration (`UserData\MelonPreferences.cfg`)
```toml
[MapPinInspector]
# Distance in meters from the Map Board beyond which an open nameplate card fades and hides.
AutoDismissDistance = 3.5
# Uniform scale multiplier for the 3D world-space nameplate card (0.4 to 2.5).
CardScale = 1.0
# Radius in board-surface meters within which overlapping player pins combine into one card.
ClusterRadius = 0.06
# Show relationship status (Friend / Player), spirit/downed state, and zone/coordinates.
ShowStatusInfo = true
# Emit verbose [MapPinInspector] diagnostic logs for board attachment, matching, and taps.
EnableDebugLogging = false
```

## Building from Source
Requires the **.NET 8 SDK** (or newer) and an installed PCVR Mono copy of *A Township Tale* with MelonLoader in the parent directory (`../../../A Township Tale_Data/Managed` and `../../../MelonLoader/net35`).

```powershell
dotnet test -c Release
dotnet build MapPinInspector/MapPinInspector.csproj -c Release
```

The standalone compiled mod is output to `MapPinInspector\bin\Release\net472\MapPinInspector.dll`.

## Architecture
See [ARCHITECTURE.md](./ARCHITECTURE.md) for the full technical design, verified `Root.Township.dll` API mappings, UV-to-surface projection math, and lifecycle guarantees.


<p>
    <img src="https://img.shields.io/github/downloads/JonathanDonkerlot/MapPinInspector/total?style=for-the-badge&logo=github&color=7fff6e">
</p>
