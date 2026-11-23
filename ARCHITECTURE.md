# MapPinInspector — Architecture & Design

## 1. Overview
**MapPinInspector** is a standalone MelonLoader mod for **A Township Tale** (PCVR Mono build) that upgrades in-game **Map Boards** with tactile VR and pointer inspection of player pins. Instead of showing only unlabelled green (friend) and red (non-friend) markers baked into the map board's render texture, players can poke, touch, or trigger-click any player pin on any Map Board to display a world-space 3D UI nameplate card anchored directly to that moving pin.

---

## 2. Target Runtime & Dependencies
Verified against the workspace installation (`C:\Games\Alta\A Township Tale`):
- **Game Version:** `A Township Tale` `main-1.7.2.1.42203`
- **Unity Engine:** `2020.3.23f1` (MonoBleedingEdge x64)
- **Mod Loader:** `MelonLoader v0.7.3 Open-Beta` (`MelonLoader\net35\MelonLoader.dll`, `MelonLoader\net35\0Harmony.dll`)
- **Mod Target Framework:** `.NET Framework 4.7.2` (`net472` with `Microsoft.NETFramework.ReferenceAssemblies` `1.0.3`, `LangVersion` `latest`)
- **Unit Test Target Framework:** `.NET 8.0` (`net8.0`, xUnit) covering `MapPinInspector.Core`
- **Referenced Game Assemblies (`A Township Tale_Data\Managed\`):**
  - `Root.Township.dll` (`Alta.Map.*`, `Player`, `PlayerController`, `UserInfoAndRole`, `FriendshipManager`, `Alta.Character.Hand`, `Alta.Chunks.LocationChunk`)
  - `Township.Core.Runtime.dll`
  - `Alta.Values.dll` (`Alta.HashedGeneralValue<T>`)
  - `Alta.Singletons.dll` (`Alta.Utilities.SingletonBehaviour<T>`)
  - `Unity.TextMeshPro.dll` (`TMPro.TextMeshProUGUI`, `TMPro.TMP_FontAsset`, `TMPro.TMP_Settings`)
  - `UnityEngine.CoreModule.dll`, `UnityEngine.UI.dll`, `UnityEngine.UIModule.dll`, `UnityEngine.TextRenderingModule.dll`, `UnityEngine.PhysicsModule.dll`, `UnityEngine.AudioModule.dll`, `UnityEngine.XRModule.dll`
- **TextMeshPro Availability:** `Unity.TextMeshPro.dll` is present in `Managed/`. At runtime, `NameplateCardView` probes `TMP_Settings.defaultFontAsset` and `Resources.FindObjectsOfTypeAll<TMP_FontAsset>()`. When a valid `TMP_FontAsset` is loaded, cards render with `TMPro.TextMeshProUGUI`; if none is available in a given scene, it transparently falls back to `UnityEngine.UI.Text` (`Arial.ttf` / OS fallback font) using a double-sided `UI/Default` material so cards are always legible.

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
   - Attaches `MapBoardInspectorController` to the `Renderer Plane` GameObject and registers it in `MapPinInspectorMod`.
2. `[HarmonyPatch(typeof(MapBoardDirectRenderer), "OnDestroy")]` (**Prefix**):
   - Triggers deterministic cleanup on the attached `MapBoardInspectorController` (unsubscribes events, destroys floating card and pin anchor GameObjects, unregisters board).
3. `[HarmonyPatch(typeof(MapLandmarkDisplay), "Setup")]` (**Postfix**, parameter `MapBoard mapBoard`):
   - Notifies any `MapBoardInspectorController` associated with that `MapBoard` that `activeMarkers` has finished initial population, ensuring pins already present at spawn immediately receive interactive anchors.
4. **Already-Loaded Board Scan:**
   - On `MelonMod.OnInitializeMelon` and `OnSceneWasLoaded`, scans `UnityEngine.Object.FindObjectsOfType<MapBoardDirectRenderer>()` (excluding `wasDestroyed`) so boards spawned before mod initialization are still hooked.

---

## 4. Coordinate Projection & 3D World-Space Nameplate UI

### 4.1 Board Surface Projection (`BoardSurfaceMapper` + `MapProjectionMath`)
Because `Renderer Plane` may use any quad mesh orientation/scale in prefab space, `BoardSurfaceMapper` inspects `MeshFilter.sharedMesh` vertices (`Vector3[]`), UVs (`Vector2[]`), and normals (`Vector3[]`) on board initialization to build an exact 2D affine mapping from `[0, 1]^2` UV coordinates to local 3D mesh coordinates:
- Extracts 3 non-collinear UV-triangle vertices `(uv0, uv1, uv2)` and corresponding local positions `(p0, p1, p2)`.
- Solves barycentric weights `(w0, w1, w2)` for any marker `(u, v)` to yield the exact local point `P_local(u, v) = w0*p0 + w1*p1 + w2*p2`.
- Computes the outward surface normal `N_local` from the mesh triangle cross product / vertex normals, ensuring it points toward the front of the board (oriented toward the `Hand Touch` child or `+forward` of the board transform).
- Pins outside `[0.02, 0.98]^2` UV bounds or occluded by an undiscovered map mask (`IsMasked == true` when `DiscoveredIcon` is not visible above the mask) are ignored so unrevealed map edges never produce phantom hitboxes.

### 4.2 Non-Blocking Pin Anchor & Trigger Colliders
Each active player pin on a board gets a pooled `PinAnchor` child GameObject parented under `Renderer Plane`:
- **Layer:** `2` (`Ignore Raycast`) so it never blocks grab raycasts, teleportation, player locomotion, or physical hand collision.
- **Collider:** `SphereCollider` with `isTrigger = true` (`radius = 0.032f` in world-normalized local units) positioned on the pin center and offset slightly along `+N_local` (`0.008f`) for responsive fingertip poking.
- **Selection Ring:** A thin pulsing ring indicator (`LineRenderer` or procedural quad sprite) shown around the currently selected pin.

### 4.3 3D World-Space Nameplate Card Hierarchy (`NameplateCardView`)
The card is parented to the active `PinAnchor` transform (and dynamically switches parent if the player moves or a different pin is selected), ensuring zero-lag tracking of moving pins:

```text
PinAnchor_<MarkerIndex> (Transform on Renderer Plane surface, moves with marker)
 └─ MapPinInspector_CardRoot (Transform offset +0.026m along board outward normal + upward)
     ├─ CardCanvas (UnityEngine.Canvas, RenderMode.WorldSpace, sortingOrder = 600, scale ~0.00032 * CardScale)
     │   ├─ CanvasGroup (alpha fade control, blocksRaycasts = false, interactable = false)
     │   ├─ ParchmentBackdrop (UnityEngine.UI.Image, procedural 9-sliced woven/deckled parchment Sprite, double-sided material)
     │   └─ PlayerName (TMPro.TextMeshProUGUI or UnityEngine.UI.Text — auto-sized player name(s) only, sepia ink)
     └─ AudioSource (spatialBlend = 1.0, procedural crisp tactile click AudioClip)
```

### 4.4 VR Billboarding Math (`MapProjectionMath.ComputeBillboardForward`)
Every `LateUpdate` while the card is visible:
1. Read camera position `C` from `PlayerController.Current?.Camera` (fallback `Camera.main`).
2. Compute vector `toCard = CardWorldPos - C` (so canvas `+Z` points *away* from the viewer and UI front `-Z` faces the viewer, matching Unity WorldSpace `Canvas` facing convention) and clamp vertical pitch to `[-35°, +35°]` around the horizon while keeping world up `(0, 1, 0)` upright.
3. Also push the card root at least `0.026m` along the board's outward normal `N_world` and slightly along `Vector3.up` (`0.032m * CardScale`) so the compact card never Z-fights or intersects the board mesh even at steep viewing angles.

---

## 5. Player Pin Identification & Metadata Correlation (`PlayerMetadataResolver` + `PlayerPinMatcher`)
Because `ClientLandmarkManager` syncs `MarkerInfo` (`Index`, `Position`, `DiscoveredIcon`, `IsMasked`) without embedding a direct `Player` reference on remote clients:
1. **Player Icon Identification:**
   - Reflects `friendIcon` (`LandmarkIcon`) and `PlayerLandmark.DiscoveredIcon` from any active `PlayerLandmark` instance in the scene (using `AccessTools.FieldRefAccess<PlayerLandmark, LandmarkIcon>("friendIcon")`), including the local player's own `PlayerLandmark`.
   - Secondary check matches `LandmarkIcon.name` (case-insensitive match for `"player"`, `"friend"`, or `"enemy"`/`"red"`) when no `PlayerLandmark` has been cached yet.
   - Static landmarks (town teleporters, shrines, mines, camps) use distinct `LandmarkIcon` instances and are excluded from pin anchors.
2. **Pin-to-Player Matching (`PlayerPinMatcher`):**
   - **Tier 1 (Exact Index):** On server/host (or whenever a `PlayerLandmark` in the scene has matching `Index != 0`), `MarkerInfo.Index == playerLandmark.Index` gives an exact 1:1 match.
   - **Tier 2 (Spatial + Friend Correlation):** Collects all `IPlayer` entries from `Player.AllPlayers` with loaded world positions (`player.PlayerController?.transform.position` or `player.Position`). Accounts for `ClientLandmarkManager`'s 11-bit XZ quantization (`Stream.SerializeVector2(..., 11)`) and 1-second `MovingLandmark` sync cadence by matching each pin to the closest unmatched player within `35m` XZ (preferring exact `IsFriend` parity).
   - **Tier 3 (Persistent Session Bindings):** Retains high-confidence `MarkerInfo.Index -> PlayerId` bindings across frames so a player who walks outside the client's loaded chunk radius retains their exact name.
   - **Tier 4 (Friend-Bucket & Join-Order Elimination):** For distant out-of-chunk remote players (`PlayerController == null`, `Player.Position == null`), pairs remaining unmatched pins and unmatched remote players within matching friend buckets (green/friend vs red/non-friend) ordered by `MarkerIndex` and `NetworkEntity.Identifier`, falling back to `"Traveler"` / `"Friend"` only if no remote player candidates exist.
3. **Optional Status Info (`ShowStatusInfo`):**
   - Friend vs Non-Friend badge, alive/downed/spirit state (`PlayerController.IsDowned`, `IsSpirit`), and nearby named region or world XZ coordinates.

---

## 6. VR Interaction, Clustering & Lifecycle Handling

### 6.1 Dual VR & Desktop Interaction
1. **Direct VR Finger Poke:** Tracks local player's left and right index fingertip bones (`indexEnd_L_jnt` / `indexEnd_R_jnt` under `Controller.Hand.IKWrist`, with fallback to `Controller.GripPoint` then `Controller.transform`). When a fingertip enters a `PinAnchor`'s trigger radius (`<= pokeRadius`) with a brief entry edge + `0.28s` per-hand debounce (`DismissalEvaluator`), the pin activates.
2. **VR Trigger-Click Ray & Desktop Fallback:** Pressing the XR controller trigger (`UnityEngine.XR.InputDevices.GetDeviceAtXRNode` `CommonUsages.triggerButton` rising edge, or `Controller.PlayerInput.RawInput`) casts a short ray (`<= 2.2m`) from the fingertip/controller forward vector against `PinAnchor` bounds; on non-VR test runs, left mouse click on `Camera.main.ScreenPointToRay` also works.
3. **Feedback:** Plays a short synthesized tactile click (`AudioSource.PlayOneShot`) and triggers a gentle XR haptic impulse (`InputDevice.SendHapticImpulse(0, 0.25f, 0.04f)`).

### 6.2 Clustered / Overlapping Pins (`PinClusterResolver`)
When multiple player pins fall within `ClusterRadius` (default `0.06m` on the board surface):
- `PinClusterResolver` groups all overlapping pins sorted deterministically by surface distance to the tapped point, then `MarkerInfo.Index`.
- The card anchors to the primary pin of the cluster and renders up to 5 stacked player entries cleanly in the card (e.g., `● Alice (Friend)\n● Bob (Player)`) with color-coded status indicators, and tapping the cluster again either cycles focus or dismisses when at the end of a single-pin selection.
- Configurable behavior: tapping the exact same single active pin toggles the card off; tapping another pin immediately moves focus and updates the card.

### 6.3 Edge-Case & Lifecycle Safety
- **Moving Pins:** Every `ClientLandmarkManager.Changed` event updates the corresponding `PinAnchor`'s board-local position immediately. When a selected player moves, the anchored card glides smoothly with `PinAnchor`.
- **Pin Despawn / Player Disconnect:** On `ClientLandmarkManager.Changed` with `DiscoveredIcon == null`, or `Player.OnPlayerRemoved`, if the removed marker was the active selection (or the last member of an active cluster), the card dismisses cleanly and the `PinAnchor` returns to an internal pool.
- **Auto-Dismiss Distance (`DismissalEvaluator`):** Each frame, distance `d` from the local player's head (`PlayerController.Head` / camera position) to the Map Board center (or closest point on the board plane) is compared against `AutoDismissDistance` (default `3.5m`) with `0.25m` hysteresis and smooth `CanvasGroup.alpha` fade-out before deactivating.
- **Board / Scene Unload:** `MapBoardDirectRenderer.OnDestroy` prefix and `MelonMod.OnSceneWasUnloaded` tear down all anchors, card objects, and static/instance subscriptions safely with null/Unity-lifetime checks.

---

## 7. Configuration (`UserData/MelonPreferences.cfg`)
Category `[MapPinInspector]`:
- `AutoDismissDistance` (`float`, default `3.5`): Distance in meters from the Map Board at which an open nameplate automatically fades and hides.
- `CardScale` (`float`, default `1.0`): Uniform scale multiplier for the 3D world-space nameplate card (`0.4`–`2.5`).
- `ClusterRadius` (`float`, default `0.06`): Radius in board-surface meters within which overlapping pins group into a combined card.
- `ShowStatusInfo` (`bool`, default `true`): Displays relationship status (Friend / Player), spirit/downed state, and coordinates/zone info below the player name.
- `EnableDebugLogging` (`bool`, default `false`): Emits verbose `[MapPinInspector]` trace logs for board attachment, pin matching, and interactions.
