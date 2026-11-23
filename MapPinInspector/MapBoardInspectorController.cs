using System;
using System.Collections.Generic;
using Alta.Map;
using MapPinInspector.Core;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace MapPinInspector
{
    /// <summary>
    /// Attached to each MapBoard's Renderer Plane. Tracks player pins, builds non-blocking trigger anchors,
    /// detects VR finger pokes and trigger-ray clicks, and manages the 3D nameplate card lifecycle.
    /// </summary>
    public sealed class MapBoardInspectorController : MonoBehaviour
    {
        private const float PinWorldRadiusMeters = 0.036f;
        private const float FingerPokeEnterRadiusMeters = 0.044f;
        private const float FingerPokeExitRadiusMeters = 0.072f;
        private const float TriggerRayMaxMeters = 2.4f;
        private const float TriggerRayHitRadiusMeters = 0.052f;

        private sealed class PinAnchorEntry
        {
            public uint MarkerId;
            public GameObject AnchorGo;
            public Transform AnchorTransform;
            public SphereCollider TriggerCollider;
            public PinSample Sample;
            public Vector3 WorldNormal;
        }

        private MapBoard _mapBoard;
        private MapLandmarkDisplay _landmarkDisplay;
        private Renderer _previewRenderer;
        private InspectorConfig _config;
        private MelonLogger.Instance _logger;
        private BoardSurfaceMapper _surfaceMapper;
        private PlayerMetadataResolver _metadataResolver;
        private NameplateCardView _cardView;

        private readonly Dictionary<uint, PinAnchorEntry> _activeAnchors = new Dictionary<uint, PinAnchorEntry>();
        private readonly Stack<PinAnchorEntry> _anchorPool = new Stack<PinAnchorEntry>();
        private readonly List<MarkerInfo> _scratchMarkerList = new List<MarkerInfo>();
        private readonly List<PinSample> _scratchPinSamples = new List<PinSample>();
        private readonly List<uint> _scratchRemoveIds = new List<uint>();

        private readonly TapDebouncer _debouncer = new TapDebouncer(0.28f);
        private readonly HashSet<uint> _leftFingerInsidePins = new HashSet<uint>();
        private readonly HashSet<uint> _rightFingerInsidePins = new HashSet<uint>();

        private ClientLandmarkManager _subscribedClientManager;
        private ClusterSelection _activeSelection;
        private bool _isAutoDismissed;
        private bool _pinsDirty = true;
        private bool _isCleanedUp;
        private float _metadataRefreshTimer;
        private bool _prevLeftTrigger;
        private bool _prevRightTrigger;

        public MapBoard Board => _mapBoard;

        public void Initialize(
            MapBoard mapBoard,
            MapLandmarkDisplay landmarkDisplay,
            Renderer previewRenderer,
            InspectorConfig config,
            MelonLogger.Instance logger)
        {
            _mapBoard = mapBoard;
            _landmarkDisplay = landmarkDisplay;
            _previewRenderer = previewRenderer;
            _config = config;
            _logger = logger;

            _surfaceMapper = new BoardSurfaceMapper(mapBoard, transform, previewRenderer);
            _metadataResolver = new PlayerMetadataResolver();
            _cardView = new NameplateCardView(transform);

            SubscribeToLandmarkEvents();
            RefreshPinsNow();

            if (_config != null && _config.EffectiveDebugLogging)
                _logger?.Msg($"[MapPinInspector] Controller initialized on board '{(_mapBoard != null ? _mapBoard.name : name)}'.");
        }

        public void MarkPinsDirty()
        {
            _pinsDirty = true;
        }

        public void OnPlayerDisconnected(int playerId)
        {
            _metadataResolver?.ForgetPlayer(playerId);
            _pinsDirty = true;
        }

        private void SubscribeToLandmarkEvents()
        {
            try
            {
                LandmarkManager lm = LandmarkManager.Instance;
                ClientLandmarkManager clm = lm != null ? lm.ClientLandmarkManager : null;
                if (clm != null && clm != _subscribedClientManager)
                {
                    if (_subscribedClientManager != null)
                        _subscribedClientManager.Changed -= OnLandmarkChanged;

                    _subscribedClientManager = clm;
                    _subscribedClientManager.Changed += OnLandmarkChanged;
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[MapPinInspector] Failed subscribing to ClientLandmarkManager.Changed: {ex.Message}");
            }
        }

        private void OnLandmarkChanged(MarkerInfo info)
        {
            _pinsDirty = true;
        }

        private void Update()
        {
            if (_isCleanedUp || _mapBoard == null)
                return;

            try
            {
                if (_subscribedClientManager == null)
                    SubscribeToLandmarkEvents();

                _metadataRefreshTimer += Time.unscaledDeltaTime;
                if (_pinsDirty || _metadataRefreshTimer >= 0.40f)
                {
                    _metadataRefreshTimer = 0f;
                    _pinsDirty = false;
                    RefreshPinsNow();
                }

                PollInteractions();
                EvaluateAutoDismissal();
            }
            catch (Exception ex)
            {
                if (_config != null && _config.EffectiveDebugLogging)
                    _logger?.Warning($"[MapPinInspector] Board Update guarded error: {ex.Message}");
            }
        }

        private void LateUpdate()
        {
            if (_isCleanedUp || _cardView == null || _activeSelection == null)
            {
                if (_cardView != null && _cardView.IsAlive)
                    _cardView.UpdateTransformAndFade(transform.position, -transform.forward, GetViewerCamera(), GetCardScale(), Time.unscaledDeltaTime);
                return;
            }

            try
            {
                uint anchorId = _activeSelection.PrimaryMarkerId;
                if (_activeAnchors.TryGetValue(anchorId, out PinAnchorEntry entry) && entry.AnchorTransform != null)
                {
                    _cardView.UpdateTransformAndFade(
                        entry.AnchorTransform.position,
                        entry.WorldNormal,
                        GetViewerCamera(),
                        GetCardScale(),
                        Time.unscaledDeltaTime);
                }
            }
            catch (Exception ex)
            {
                if (_config != null && _config.EffectiveDebugLogging)
                    _logger?.Warning($"[MapPinInspector] Board LateUpdate guarded error: {ex.Message}");
            }
        }

        private void RefreshPinsNow()
        {
            if (_surfaceMapper == null || _metadataResolver == null)
                return;

            _metadataResolver.RefreshPlayerIconCache(Time.unscaledTime);

            _scratchMarkerList.Clear();
            Dictionary<uint, MarkerInfo> displayMarkers = HarmonyPatches.TryGetDisplayActiveMarkers(_landmarkDisplay);
            if (displayMarkers != null)
            {
                foreach (KeyValuePair<uint, MarkerInfo> kv in displayMarkers)
                {
                    if (kv.Value != null && kv.Value.DiscoveredIcon != null)
                        _scratchMarkerList.Add(kv.Value);
                }
            }
            else if (_subscribedClientManager != null && _subscribedClientManager.Markers != null)
            {
                foreach (MarkerInfo m in _subscribedClientManager.Markers)
                {
                    if (m != null && m.DiscoveredIcon != null)
                        _scratchMarkerList.Add(m);
                }
            }

            Dictionary<uint, ResolvedPinMetadata> metadataMap = _metadataResolver.ResolveBatch(
                _scratchMarkerList,
                _surfaceMapper.WorldCenter,
                _config == null || _config.EffectiveShowStatusInfo);

            _scratchPinSamples.Clear();
            var activeIds = new HashSet<uint>();

            for (int i = 0; i < _scratchMarkerList.Count; i++)
            {
                MarkerInfo marker = _scratchMarkerList[i];
                if (!metadataMap.TryGetValue(marker.Index, out ResolvedPinMetadata meta) || !meta.IsPlayerPin)
                    continue;

                if (!_surfaceMapper.TryProjectWorldPosition(
                        marker.Position,
                        out Vector3 localPoint,
                        out Vector3 localNormal,
                        out Vector3 worldPoint,
                        out Vector3 worldNormal))
                {
                    continue;
                }

                activeIds.Add(marker.Index);
                PinAnchorEntry anchor = GetOrCreateAnchor(marker.Index);
                anchor.AnchorTransform.localPosition = localPoint + localNormal * 0.006f;
                anchor.WorldNormal = worldNormal;

                if (anchor.TriggerCollider != null)
                    anchor.TriggerCollider.radius = ComputeColliderLocalRadius(PinWorldRadiusMeters);

                var sample = new PinSample(
                    marker.Index,
                    new Vec3(worldPoint.x, worldPoint.y, worldPoint.z),
                    new Vec2(marker.Position.x, marker.Position.y),
                    meta.IsFriend,
                    meta.IsDowned,
                    meta.IsSpirit,
                    meta.IsLocalPlayer,
                    meta.DisplayName,
                    meta.StatusLabel);

                anchor.Sample = sample;
                _scratchPinSamples.Add(sample);
            }

            // Release anchors for markers that despawned, disconnected, or moved off-board.
            _scratchRemoveIds.Clear();
            foreach (KeyValuePair<uint, PinAnchorEntry> kv in _activeAnchors)
            {
                if (!activeIds.Contains(kv.Key))
                    _scratchRemoveIds.Add(kv.Key);
            }

            for (int i = 0; i < _scratchRemoveIds.Count; i++)
                ReleaseAnchor(_scratchRemoveIds[i]);

            if (_activeSelection != null)
            {
                ClusterSelection refreshed = PinClusterResolver.RefreshActiveCluster(
                    _activeSelection,
                    _scratchPinSamples,
                    GetClusterRadius());

                if (refreshed == null)
                {
                    DismissSelection();
                }
                else
                {
                    _activeSelection = refreshed;
                    if (_activeAnchors.TryGetValue(_activeSelection.PrimaryMarkerId, out PinAnchorEntry primary))
                    {
                        _cardView.SetSelection(
                            _activeSelection,
                            primary.AnchorTransform,
                            _config == null || _config.EffectiveShowStatusInfo,
                            playFeedback: false);
                    }
                }
            }
        }

        private void PollInteractions()
        {
            if (_activeAnchors.Count == 0)
                return;

            PlayerController local = PlayerController.Current;
            if (local == null || !local.IsLocalPlayer)
                return;

            // Only process proximity/ray checks when local player is reasonably close to the board.
            float boardDistance = _surfaceMapper.DistanceToBoard(GetHeadPosition(local));
            if (boardDistance > Math.Max(GetAutoDismissDistance(), TriggerRayMaxMeters) + 1.0f)
                return;

            float now = Time.unscaledTime;

            // 1. Direct VR fingertip poke (left & right index fingertips).
            Transform leftTip = ResolveIndexTip(local, rightHand: false);
            Transform rightTip = ResolveIndexTip(local, rightHand: true);

            if (leftTip != null)
                PollFingertipPoke(leftTip.position, _leftFingerInsidePins, sourceId: 1, XRNode.LeftHand, now);
            else
                _leftFingerInsidePins.Clear();

            if (rightTip != null)
                PollFingertipPoke(rightTip.position, _rightFingerInsidePins, sourceId: 2, XRNode.RightHand, now);
            else
                _rightFingerInsidePins.Clear();

            // 2. VR controller trigger-ray click (left & right).
            PollControllerTriggerRay(local.LeftController, XRNode.LeftHand, ref _prevLeftTrigger, sourceId: 3, now);
            PollControllerTriggerRay(local.RightController, XRNode.RightHand, ref _prevRightTrigger, sourceId: 4, now);

            // 3. Desktop mouse click fallback for testing on pancake / simulator builds.
            PollDesktopMouseFallback(now);
        }

        private void PollFingertipPoke(
            Vector3 tipWorldPos,
            HashSet<uint> insideSet,
            int sourceId,
            XRNode node,
            float now)
        {
            uint bestEnteredId = 0;
            float bestDistSq = float.MaxValue;

            foreach (KeyValuePair<uint, PinAnchorEntry> kv in _activeAnchors)
            {
                PinAnchorEntry entry = kv.Value;
                if (entry.AnchorTransform == null)
                    continue;

                float distSq = (tipWorldPos - entry.AnchorTransform.position).sqrMagnitude;
                bool wasInside = insideSet.Contains(kv.Key);

                if (wasInside)
                {
                    if (distSq > FingerPokeExitRadiusMeters * FingerPokeExitRadiusMeters)
                        insideSet.Remove(kv.Key);
                }
                else if (distSq <= FingerPokeEnterRadiusMeters * FingerPokeEnterRadiusMeters)
                {
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestEnteredId = kv.Key;
                    }
                }
            }

            if (bestEnteredId != 0)
            {
                insideSet.Add(bestEnteredId);
                if (_debouncer.TryAccept(sourceId, now))
                {
                    HandlePinTapped(bestEnteredId);
                    SendHapticPulse(node, 0.25f, 0.04f);
                }
            }
        }

        private void PollControllerTriggerRay(
            Controller controller,
            XRNode node,
            ref bool prevTriggerPressed,
            int sourceId,
            float now)
        {
            bool pressed = IsXrTriggerPressed(node);
            bool risingEdge = pressed && !prevTriggerPressed;
            prevTriggerPressed = pressed;

            if (!risingEdge || controller == null)
                return;

            Transform rayOrigin = ResolvePointerTransform(controller, node == XRNode.RightHand);
            if (rayOrigin == null)
                return;

            var ray = new Ray(rayOrigin.position, rayOrigin.forward);
            if (TryFindRayHitPin(ray, TriggerRayMaxMeters, TriggerRayHitRadiusMeters, out uint hitMarkerId) &&
                _debouncer.TryAccept(sourceId, now))
            {
                HandlePinTapped(hitMarkerId);
                SendHapticPulse(node, 0.22f, 0.035f);
            }
        }

        private void PollDesktopMouseFallback(float now)
        {
            try
            {
                Mouse mouse = Mouse.current;
                if (mouse == null || mouse.leftButton == null || !mouse.leftButton.wasPressedThisFrame)
                    return;

                Camera cam = GetViewerCamera();
                if (cam == null)
                    return;

                Vector2 screenPos = mouse.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
                if (TryFindRayHitPin(ray, TriggerRayMaxMeters + 2f, TriggerRayHitRadiusMeters, out uint hitMarkerId) &&
                    _debouncer.TryAccept(5, now))
                {
                    HandlePinTapped(hitMarkerId);
                }
            }
            catch
            {
                // Ignore InputSystem polling errors when window is unfocused.
            }
        }

        private bool TryFindRayHitPin(Ray ray, float maxDistance, float hitRadius, out uint hitMarkerId)
        {
            hitMarkerId = 0;
            float bestRayT = float.MaxValue;
            float hitRadiusSq = hitRadius * hitRadius;

            foreach (KeyValuePair<uint, PinAnchorEntry> kv in _activeAnchors)
            {
                PinAnchorEntry entry = kv.Value;
                if (entry.AnchorTransform == null)
                    continue;

                Vector3 center = entry.AnchorTransform.position;
                Vector3 toCenter = center - ray.origin;
                float proj = Vector3.Dot(toCenter, ray.direction);
                if (proj < 0f || proj > maxDistance)
                    continue;

                Vector3 closestOnRay = ray.origin + ray.direction * proj;
                float perpDistSq = (center - closestOnRay).sqrMagnitude;
                if (perpDistSq <= hitRadiusSq && proj < bestRayT)
                {
                    bestRayT = proj;
                    hitMarkerId = kv.Key;
                }
            }

            return hitMarkerId != 0;
        }

        private void HandlePinTapped(uint tappedMarkerId)
        {
            _scratchPinSamples.Clear();
            foreach (KeyValuePair<uint, PinAnchorEntry> kv in _activeAnchors)
            {
                if (kv.Value.Sample != null)
                    _scratchPinSamples.Add(kv.Value.Sample);
            }

            TapOutcome outcome = PinClusterResolver.ResolveTap(
                _activeSelection,
                tappedMarkerId,
                _scratchPinSamples,
                GetClusterRadius(),
                out ClusterSelection nextSelection);

            if (_config != null && _config.EffectiveDebugLogging)
                _logger?.Msg($"[MapPinInspector] Pin {tappedMarkerId} tapped -> {outcome}");

            if (outcome == TapOutcome.Dismissed || nextSelection == null)
            {
                DismissSelection();
                return;
            }

            _activeSelection = nextSelection;
            _isAutoDismissed = false;

            if (_activeAnchors.TryGetValue(_activeSelection.PrimaryMarkerId, out PinAnchorEntry primary))
            {
                _cardView.SetSelection(
                    _activeSelection,
                    primary.AnchorTransform,
                    _config == null || _config.EffectiveShowStatusInfo,
                    playFeedback: true);
            }
        }

        private void EvaluateAutoDismissal()
        {
            if (_activeSelection == null)
                return;

            PlayerController local = PlayerController.Current;
            if (local == null)
            {
                DismissSelection();
                return;
            }

            float dist = _surfaceMapper.DistanceToBoard(GetHeadPosition(local));
            _isAutoDismissed = DismissalEvaluator.ShouldAutoDismiss(
                dist,
                GetAutoDismissDistance(),
                _isAutoDismissed,
                hysteresisMeters: 0.25f);

            if (_isAutoDismissed)
            {
                _cardView.SetVisible(false);
                if (_cardView.CurrentAlpha <= 0.01f)
                    _activeSelection = null;
            }
            else
            {
                _cardView.SetVisible(true);
            }
        }

        public void DismissSelection()
        {
            _activeSelection = null;
            _isAutoDismissed = false;
            _cardView?.SetVisible(false);
        }

        private PinAnchorEntry GetOrCreateAnchor(uint markerId)
        {
            if (_activeAnchors.TryGetValue(markerId, out PinAnchorEntry existing))
                return existing;

            PinAnchorEntry entry;
            if (_anchorPool.Count > 0)
            {
                entry = _anchorPool.Pop();
                entry.AnchorGo.SetActive(true);
            }
            else
            {
                var go = new GameObject($"MapPinAnchor_{markerId}");
                go.layer = 2; // Ignore Raycast — never blocks grabs or physics
                go.transform.SetParent(transform, false);

                SphereCollider sphere = go.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = ComputeColliderLocalRadius(PinWorldRadiusMeters);

                entry = new PinAnchorEntry
                {
                    AnchorGo = go,
                    AnchorTransform = go.transform,
                    TriggerCollider = sphere
                };
            }

            entry.MarkerId = markerId;
            entry.AnchorGo.name = $"MapPinAnchor_{markerId}";
            _activeAnchors[markerId] = entry;
            return entry;
        }

        private void ReleaseAnchor(uint markerId)
        {
            if (!_activeAnchors.TryGetValue(markerId, out PinAnchorEntry entry))
                return;

            _activeAnchors.Remove(markerId);
            _leftFingerInsidePins.Remove(markerId);
            _rightFingerInsidePins.Remove(markerId);

            if (entry.AnchorGo != null)
            {
                entry.AnchorGo.SetActive(false);
                _anchorPool.Push(entry);
            }
        }

        private float ComputeColliderLocalRadius(float worldRadiusMeters)
        {
            Vector3 lossy = transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
            if (maxScale < 1e-4f)
                maxScale = 1f;
            return worldRadiusMeters / maxScale;
        }

        private float GetAutoDismissDistance() =>
            _config != null ? _config.EffectiveAutoDismissDistance : 3.5f;

        private float GetCardScale() =>
            _config != null ? _config.EffectiveCardScale : 1.0f;

        private float GetClusterRadius() =>
            _config != null ? _config.EffectiveClusterRadius : 0.06f;

        private static Camera GetViewerCamera()
        {
            PlayerController pc = PlayerController.Current;
            if (pc != null && pc.Camera != null)
                return pc.Camera;
            return Camera.main;
        }

        private static Vector3 GetHeadPosition(PlayerController local)
        {
            if (local != null && local.Head != null)
                return local.Head.position;
            if (local != null && local.Camera != null)
                return local.Camera.transform.position;
            return local != null ? local.transform.position : Vector3.zero;
        }

        private static Transform ResolveIndexTip(PlayerController player, bool rightHand)
        {
            Controller ctrl = rightHand ? player.RightController : player.LeftController;
            if (ctrl == null)
                return null;

            string boneName = rightHand ? "indexEnd_R_jnt" : "indexEnd_L_jnt";
            Alta.Character.Hand hand = ctrl.Hand;

            if (hand != null && hand.IKWrist != null)
            {
                Transform found = FindDescendantByName(hand.IKWrist, boneName);
                if (found != null)
                    return found;
            }

            Transform ctrlFound = FindDescendantByName(ctrl.transform, boneName);
            if (ctrlFound != null)
                return ctrlFound;

            if (ctrl.GripPoint != null)
                return ctrl.GripPoint;

            return ctrl.transform;
        }

        private static Transform ResolvePointerTransform(Controller controller, bool rightHand)
        {
            Transform tip = null;
            if (controller.Hand != null && controller.Hand.IKWrist != null)
                tip = FindDescendantByName(controller.Hand.IKWrist, rightHand ? "indexEnd_R_jnt" : "indexEnd_L_jnt");

            if (tip != null)
                return tip;

            return controller.GripPoint != null ? controller.GripPoint : controller.transform;
        }

        private static Transform FindDescendantByName(Transform root, string targetName)
        {
            if (root == null)
                return null;
            if (string.Equals(root.name, targetName, StringComparison.Ordinal))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform childHit = FindDescendantByName(root.GetChild(i), targetName);
                if (childHit != null)
                    return childHit;
            }

            return null;
        }

        private static bool IsXrTriggerPressed(XRNode node)
        {
            try
            {
                UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
                return device.isValid &&
                       device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool pressed) &&
                       pressed;
            }
            catch
            {
                return false;
            }
        }

        private static void SendHapticPulse(XRNode node, float amplitude, float durationSeconds)
        {
            try
            {
                UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
                if (device.isValid)
                    device.SendHapticImpulse(0u, amplitude, durationSeconds);
            }
            catch
            {
                // Ignore unsupported haptic hardware.
            }
        }

        public void Cleanup()
        {
            if (_isCleanedUp)
                return;
            _isCleanedUp = true;

            try
            {
                if (_subscribedClientManager != null)
                {
                    _subscribedClientManager.Changed -= OnLandmarkChanged;
                    _subscribedClientManager = null;
                }
            }
            catch
            {
                // Ignore during shutdown.
            }

            _activeSelection = null;
            _metadataResolver?.ClearKnownBindings();

            if (_cardView != null)
            {
                _cardView.Destroy();
                _cardView = null;
            }

            foreach (KeyValuePair<uint, PinAnchorEntry> kv in _activeAnchors)
            {
                if (kv.Value.AnchorGo != null)
                    Destroy(kv.Value.AnchorGo);
            }
            _activeAnchors.Clear();

            while (_anchorPool.Count > 0)
            {
                PinAnchorEntry pooled = _anchorPool.Pop();
                if (pooled.AnchorGo != null)
                    Destroy(pooled.AnchorGo);
            }
        }

        private void OnDestroy()
        {
            Cleanup();
            MapPinInspectorMod.Instance?.UnregisterController(this);
        }
    }
}
