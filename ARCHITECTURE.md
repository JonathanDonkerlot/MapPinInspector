# MapPinInspector — Architecture & Design

## 1. Overview
**MapPinInspector** (`1.1.2`) is a standalone MelonLoader mod for **A Township Tale** (PCVR Mono build) that upgrades in-game **Map Boards** with tactile VR and pointer inspection of player pins. Instead of showing only unlabelled green (friend) and red (non-friend) markers baked into the map board's render texture, players can poke, touch, or trigger-click any player pin on any Map Board to display a compact aged-parchment **3D world-space name tag** rendered in ATT's native Signpost font and anchored directly to that moving pin.

---

## 2. Target Runtime & Dependencies
Verified against the workspace installation (`C:\Games\Alta\A Township Tale`):
- **Game Version:** `A Township Tale` `main-1.7.2.1.42203`
- **Unity Engine:** `2020.3.23f1` (MonoBleedingEdge x64)
- **Mod Loader:** `MelonLoader v0.7.3 Open-Beta` (`MelonLoader\net35\MelonLoader.dll`, `MelonLoader\net35\0Harmony.dll`)
- **Mod Target Framework:** `.NET Framework 4.7.2` (`net472` with `Microsoft.NETFramework.ReferenceAssemblies` `1.0.3`, `LangVersion` `latest`)
- **Unit Test Target Framework:** `.NET 8.0` (`net8.0`, xUnit) covering `MapPinInspector.Core`
- **Referenced Game Assemblies (`A Township Tale_Data\Managed\`):**
  - `Root.Township.dll` (`Alta.Map.*`, `Player`, `PlayerController`, `UserInfoAndRole`, `FriendshipManager`, `Alta.Character.Hand`, `Alta.Chunks.LocationChunk`, `TextFont`, `TextRenderer`, `SignPostPanel`)
  - `Township.Core.Runtime.dll`
  - `Alta.Values.dll` (`Alta.HashedGeneralValue<T>`)
  - `Alta.Singletons.dll` (`Alta.Utilities.SingletonBehaviour<T>`)
  - `Unity.InputSystem.dll` (`UnityEngine.InputSystem.Mouse`)
  - `UnityEngine.CoreModule.dll`, `UnityEngine.UI.dll`, `UnityEngine.UIModule.dll`, `UnityEngine.TextRenderingModule.dll`, `UnityEngine.PhysicsModule.dll`, `UnityEngine.AudioModule.dll`, `UnityEngine.XRModule.dll`

---

## 3. Verified Game Classes & Harmony Hooks

### 3.1 Map Board Scene Hierarchy (`GameObjectDump.txt`)
- `Map Board(Clone)`:
  - `Alta.Map.MapBoard`
  - `Alta.Map.MapLandmarkDisplay`
  - `Alta.Map.Landmark`
  - `Alta.Map.LandmarkDiscoverZone`
  - Child `Hand Touch`: `Interactable`, `HandHoverPoint`, `SphereCollider`
  - Child `Renderer Plane`: `MeshFilter`, `MeshRenderer`, `Alta.Map.MapBoardDirectRenderer`

### 3.2 How ATT Renders Map Pins
ATT does **not** instantiate separate Unity `Image` GameObjects for each marker on modern boards:
1. `Alta.Map.LandmarkManager.Instance.ClientLandmarkManager` (`Alta.Map.ClientLandmarkManager`) maintains `Dictionary<uint, MarkerInfo> markers` synced from the server (`InitialSync`, `ChangeSync`) and fires `event MarkerEvent Changed` whenever a marker is added, moved, or removed (`DiscoveredIcon == null` on removal).
2. `Alta.Map.MapLandmarkDisplay` listens to `ClientLandmarkManager.Changed` after `Setup(MapBoard)` runs, populating `Dictionary<uint, MarkerInfo> activeMarkers` and exposing `BelowTheMaskMarkers` and `AboveTheMaskMarkers`.
3. `Alta.Map.MapBoardDirectRenderer` renders `map`, `MapBoard.ActiveMapPieces`, and `MarkerInfo` sprites onto a `RenderTexture` (`rt`) assigned to `previewRenderer.material.mainTexture`. For every `MarkerInfo`, it computes:
   - `Vector2 mapPos = mapBoard.WorldToMapPosition(marker.Position);`
   - `Vector4 offsetScale = (Vector4)mapPos + mapBoard.MapOffsetScale;`
   - Where `MapBoard.MapOffsetScale = (positionOffset.x / resolution.x, positionOffset.y / resolution.y, zoom, zoom)`.
   - Thus the marker's center in `[0, 1]^2` texture UV space is:
     `u = 0.5f + mapPos.x + mapOffsetScale.x`
     `v = 0.5f + mapPos.y + mapOffsetScale.y`
4. `Alta.Map.PlayerLandmark` (attached to player character prefabs, inheriting `MovingLandmark : Landmark`) sets:
   - `friendIcon` (green pin) when `FriendshipManager.IsFriendsWith(targetId)` is true.
   - `discoveredIcon` (red pin) when `PlayerLandmarkMode == ShowAll` and the player is not a friend.

### 3.3 Harmony Patch Targets
All Harmony patches live in `HarmonyPatches.cs` and wrap every callback in defensive `try/catch` blocks with `[MapPinInspector]` logging:
1. `[HarmonyPatch(typeof(MapBoardDirectRenderer), "Awake")]` (**Postfix**):
   - Reads private fields `mapBoard`, `landmarkDisplay`, and `previewRenderer` via `AccessTools.FieldRefAccess`.
   - Ignores headless instances (`ApplicationManager.IsHeadless` destroys the renderer).
   - Prewarms ATT's native font cache, attaches `MapBoardInspectorController` to the `Renderer Plane` GameObject, and registers the board in `MapPinInspectorMod`.
2. `[HarmonyPatch(typeof(MapBoardDirectRenderer), "OnDestroy")]` (**Prefix**):
   - Triggers deterministic cleanup on the attached `MapBoardInspectorController` (unsubscribes events, destroys floating card and pin anchor GameObjects, unregisters board).
3. `[HarmonyPatch(typeof(MapLandmarkDisplay), "Setup")]` (**Postfix**, parameter `MapBoard mapBoard`):
   - Notifies any `MapBoardInspectorController` associated with that `MapBoard` that `activeMarkers` has finished initial population, ensuring pins already present at spawn immediately receive interactive anchors.
4. **Already-Loaded Board Scan:**
   - On `MelonMod.OnInitializeMelon` and `OnSceneWasLoaded`, prewarms font discovery and scans `UnityEngine.Object.FindObjectsOfType<MapBoardDirectRenderer>()` (excluding `wasDestroyed`) so boards spawned before mod initialization are still hooked.

---

## 4. Coordinate Projection, Native Font & 3D Nameplate UI

### 4.1 Board Surface Projection (`BoardSurfaceMapper` + `MapProjectionMath`)
Because `Renderer Plane` may use any quad mesh orientation/scale in prefab space, `BoardSurfaceMapper` inspects `MeshFilter.sharedMesh` vertices (`Vector3[]`), UVs (`Vector2[]`), and normals (`Vector3[]`) on board initialization to build an exact 2D affine mapping from `[0, 1]^2` UV coordinates to local 3D mesh coordinates (falling back to `Mesh.bounds` when `!mesh.isReadable`):
- Extracts 3 non-collinear UV-triangle vertices `(uv0, uv1, uv2)` and corresponding local positions `(p0, p1, p2)`.
- Solves barycentric weights `(w0, w1, w2)` for any marker `(u, v)` to yield `P_local(u, v) = w0*p0 + w1*p1 + w2*p2`.
- Orients the outward normal `N_local` toward the `Hand Touch` (`HandHoverPoint`) front face so anchors and cards never spawn behind the board.

### 4.2 Non-Blocking Pin Anchor & Trigger Colliders
Each active player pin on a board gets a pooled `PinAnchor` child GameObject parented under `Renderer Plane`:
- **Layer:** `2` (`Ignore Raycast`) so it never blocks grab raycasts, teleportation, player locomotion, or physical hand collision.
- **Collider:** `SphereCollider` with `isTrigger = true` (`0.036m` normalized to world space) positioned on the pin center and offset slightly along `+N_local` (`0.006m`).
- **Selection Ring:** A thin pulsing `LineRenderer` ring (`baseRadius = 0.020m`) color-coded to Friend (green), Player (red), or Cluster (gold).

### 4.3 Compact Parchment Nameplate Hierarchy & Native Font Pipeline (`NameplateCardView`)
The card is parented to the active `PinAnchor` transform, keeping zero-lag tracking of moving pins:

```text
PinAnchor_<MarkerIndex> (Transform on Renderer Plane surface, moves with marker)
 └─ MapPinInspector_CardRoot (Transform offset +0.026m along board outward normal + upward)
     ├─ CardCanvas (UnityEngine.Canvas, RenderMode.WorldSpace, sortingOrder = 600, scale ~0.00032 * CardScale)
     │   ├─ CanvasGroup (alpha fade control, blocksRaycasts = false, interactable = false)
     │   ├─ ParchmentBackdrop (UnityEngine.UI.Image, procedural 256x128 9-sliced woven/deckled parchment Sprite, double-sided material)
     │   └─ PlayerName (UnityEngine.UI.Text — ATT native Signpost/UI font, dark sepia ink #2B1E14, player name(s) only)
     └─ AudioSource (spatialBlend = 1.0, procedural crisp tactile click AudioClip)
```

**Native Township Font Resolution (`NameplateCardView.TryResolveNativeTownshipFont`):**
1. Scans `Resources.FindObjectsOfTypeAll<TextFont>()` and active `TextRenderer` instances for `TextFont.LinkedFont` (prioritizing `"Signpost"`, the wooden signpost font in `Root.Township.dll`).
2. Checks `SignPostPanel` UI `Text.font` components in the active scene.
3. Scans loaded `UnityEngine.Font` assets (`Resources.FindObjectsOfTypeAll<Font>()`), excluding generic `Arial` / `LiberationSans` and scoring `"Signpost"`, `"Township"`, `"Alta"`, `"Medieval"`, `"Uncial"`, and `"Serif"`.
4. Scans active scene `UnityEngine.UI.Text` components for any non-system game font.
5. Falls back temporarily to stylized OS serif (`Georgia`, `Palatino Linotype`, `Book Antiqua`, `Times New Roman`) and automatically hot-swaps to ATT's native font on `SetSelection` once scene font assets finish loading. Respects `Font.dynamic` before applying style or best-fit overrides so non-dynamic fonts never emit Unity warnings.

### 4.4 VR Billboarding Math (`MapProjectionMath.ComputeBillboardCanvasForward`)
Every `LateUpdate` while visible, `toCard = CardWorldPos - CameraPos` is pitch-clamped to `[-35°, +35°]` around the horizontal plane and rotated with `Quaternion.LookRotation(forward, Vector3.up)` while offsetting forward along `N_world` (`0.026m + 0.008m * CardScale`) and upward (`0.032m * CardScale`).

---

## 5. Player Pin Identification & Metadata Correlation (`PlayerMetadataResolver` + `PlayerPinMatcher`)
Because `ClientLandmarkManager` syncs `MarkerInfo` (`Index`, `Position`, `DiscoveredIcon`, `IsMasked`) without embedding a direct `Player` reference on remote clients:
1. **Player Icon Identification:** Reflects `friendIcon` and `DiscoveredIcon` from scene `PlayerLandmark` components (`AccessTools.FieldRefAccess<PlayerLandmark, LandmarkIcon>("friendIcon")`), with a `"player"`/`"friend"` icon-name fallback before any `PlayerLandmark` spawns.
2. **4-Tier Pin-to-Player Matching (`PlayerPinMatcher`):**
   - **Tier 1 (Exact Index):** On server/host (or when `PlayerLandmark.Index != 0`), matches `MarkerInfo.Index == playerLandmark.Index`.
   - **Tier 2 (Spatial + Friend Correlation):** Matches chunk-loaded players (`PlayerController != null` or `Player.Position != null`) to the closest unmatched pin within `35m` XZ, preferring exact friend parity (`IPlayer.FriendshipManager.IsFriend` or `Player.Current.FriendshipManager.IsFriendsWith`).
   - **Tier 3 (Persistent Session Bindings):** Retains high-confidence `MarkerInfo.Index -> PlayerId` bindings across frames so a player walking outside the client's loaded chunk radius keeps their resolved name.
   - **Tier 4 (Friend-Bucket & Join-Order Elimination):** For distant out-of-chunk remote players (`PlayerController == null`, `Player.Position == null`), pairs remaining unmatched pins and unmatched remote players (`!IsLocalPlayer`) within matching friend buckets ordered by `MarkerIndex` and `NetworkEntity.Identifier`, falling back to `"Traveler"` / `"Friend"` only if no remote candidates exist.

---

## 6. VR Interaction, Clustering & Lifecycle Handling
- **Direct VR Finger Poke:** Tracks local `indexEnd_L_jnt` / `indexEnd_R_jnt` under `Controller.Hand.IKWrist` (`0.044m` enter / `0.072m` exit hysteresis + `0.28s` per-hand `TapDebouncer`).
- **VR Trigger-Ray & Desktop Fallback:** XR `CommonUsages.triggerButton` rising edge (`2.4m` ray) or `Mouse.current.leftButton.wasPressedThisFrame`.
- **Cluster Handling (`PinClusterResolver`):** Groups pins within `ClusterRadius` (`0.06m`), displays stacked player names on the parchment card, cycles focus on repeated taps, toggles off on second tap for single pins, and switches immediately when tapping another pin.
- **Auto-Dismiss & Cleanup:** Fades smoothly when the player moves past `AutoDismissDistance` (`3.5m` with `0.25m` hysteresis), updates dynamically as pins move, clears bindings on `Player.OnPlayerRemoved`, and destroys all anchors/cards cleanly on `MapBoardDirectRenderer.OnDestroy` and scene unload.

---

## 7. Configuration (`UserData/MelonPreferences.cfg`)
Category `[MapPinInspector]`:
- `AutoDismissDistance` (`float`, default `3.5`): Distance in meters from the Map Board beyond which an open nameplate automatically fades and hides.
- `CardScale` (`float`, default `1.0`): Uniform scale multiplier for the 3D world-space parchment tag (`0.4`–`2.5`).
- `ClusterRadius` (`float`, default `0.06`): Radius in board-surface meters within which overlapping pins group together.
- `ShowStatusInfo` (`bool`, default `true`): Reserved preference entry preserved for config compatibility.
- `EnableDebugLogging` (`bool`, default `false`): Emits verbose `[MapPinInspector]` trace logs for board attachment, font resolution, matching, and interactions.
