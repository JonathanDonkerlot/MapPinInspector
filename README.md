# MapPinInspector

**MapPinInspector** is a client-side MelonLoader mod for **A Township Tale** (PCVR Mono build) that makes every player pin on in-game **Map Boards** interactive. Touch, poke, or trigger-click any player pin on any Map Board to pop up a compact aged-parchment **3D world-space name tag** rendered in *A Township Tale*'s native Signpost typography and anchored directly to that moving pin.

## Features
- **Multi-Board Support:** Automatically attaches to every Map Board in the world, including dynamically loaded boards.
- **VR Tactile & Pointer Interaction:** Poke player pins directly with either index fingertip, point-and-click with your XR trigger, or left-click on desktop—using non-blocking trigger colliders (`Ignore Raycast` layer) that never interfere with grabs or board touch crystals.
- **Native Township Signpost Font & Parchment Tag:**
  - Compact aged-parchment card (`~9.6 cm × 2.7 cm` at default `CardScale = 1.0`) with warm mottled woven paper and organic deckled edges.
  - Displays only the player's name (or stacked player names on overlapping clusters) in dark sepia ink using *A Township Tale*'s native **`Signpost`** / UI font (`TextFont.LinkedFont` & `SignPostPanel` resolution with automatic stylized serif fallback and late-load hot-swap).
  - Anchored directly to the selected pin so it tracks moving players across the map and billboards cleanly toward your VR camera.
- **Multiplayer Remote Player Resolution:** Matches pins across both loaded and distant/unloaded chunks via exact `Landmark.Index`, spatial XZ correlation, persistent session bindings, and friend-bucket / join-order elimination.
- **Overlapping Pin Clustering:** Groups nearby pins within `ClusterRadius` into a clean stacked list—tap again to cycle, tap a single pin again to close, or walk away to auto-dismiss.
- **Tactile & Audio Feedback:** Subtle color-coded pulsing selection ring (green friend / red player / gold cluster), synthesized spatial click audio, and gentle XR controller haptics.

## Requirements
- **A Township Tale** (PCVR Mono build, Unity `2020.3.23f1`)
- **MelonLoader v0.7.x** (tested on MelonLoader v0.7.3 Open-Beta, `net35` facade)

## Installation
1. Download **`MapPinInspector.dll`** from the [Releases](../../releases) page.
2. Copy `MapPinInspector.dll` into your game's `Mods\` folder:
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
# Reserved preference toggle kept for backwards compatibility with v1.0 configs.
ShowStatusInfo = true
# Emit verbose [MapPinInspector] diagnostic logs for board attachment, font resolution, matching, and taps.
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
See [ARCHITECTURE.md](./ARCHITECTURE.md) for the full technical design, verified `Root.Township.dll` API mappings, font resolution pipeline, UV-to-surface projection math, and lifecycle guarantees.


<p>
    <img src="https://img.shields.io/github/downloads/JonathanDonkerlot/MapPinInspector/total?style=for-the-badge&logo=github&color=7fff6e">
</p>
