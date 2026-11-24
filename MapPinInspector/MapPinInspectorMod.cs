using System;
using System.Collections.Generic;
using System.IO;
using Alta.Map;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace MapPinInspector
{
    /// <summary>
    /// MelonLoader entry point for MapPinInspector. Coordinates MapBoard attachment,
    /// native font prewarming, configuration, and scene lifecycle cleanup.
    /// </summary>
    public sealed class MapPinInspectorMod : MelonMod
    {
        public static MapPinInspectorMod Instance { get; private set; }

        private readonly InspectorConfig _config = new InspectorConfig();
        private readonly Dictionary<MapBoardDirectRenderer, MapBoardInspectorController> _controllersByRenderer =
            new Dictionary<MapBoardDirectRenderer, MapBoardInspectorController>();
        private readonly Dictionary<MapBoard, MapBoardInspectorController> _controllersByBoard =
            new Dictionary<MapBoard, MapBoardInspectorController>();

        private bool _isIl2CppAborted;

        public override void OnInitializeMelon()
        {
            Instance = this;

            if (File.Exists(Path.Combine(MelonEnvironment.GameRootDirectory, "GameAssembly.dll")))
            {
                _isIl2CppAborted = true;
                LoggerInstance.Error("[MapPinInspector] IL2CPP GameAssembly.dll detected; MapPinInspector targets Mono PCVR only and will remain inactive.");
                return;
            }

            _config.Initialize();
            HarmonyPatches.Apply(HarmonyInstance, LoggerInstance);

            try
            {
                Player.OnPlayerRemoved += OnPlayerRemoved;
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[MapPinInspector] Could not subscribe to Player.OnPlayerRemoved: {ex.Message}");
            }

            string fontSource = NameplateCardView.PrewarmTownshipFont(LoggerInstance, _config.EffectiveDebugLogging);
            ScanExistingBoards();

            LoggerInstance.Msg(
                $"[MapPinInspector] Initialized v1.1.2 | Font={fontSource}, " +
                $"AutoDismiss={_config.EffectiveAutoDismissDistance:F1}m, " +
                $"Scale={_config.EffectiveCardScale:F2}, ClusterRadius={_config.EffectiveClusterRadius:F3}m");
        }

        public override void OnDeinitializeMelon()
        {
            try
            {
                Player.OnPlayerRemoved -= OnPlayerRemoved;
            }
            catch
            {
                // Ignore during shutdown.
            }

            CleanupAllControllers();
            if (Instance == this)
                Instance = null;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (_isIl2CppAborted)
                return;

            NameplateCardView.PrewarmTownshipFont(LoggerInstance, _config.EffectiveDebugLogging);
            ScanExistingBoards();
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (_isIl2CppAborted)
                return;

            CleanupAllControllers();
        }

        public void NotifyBoardRendererAwake(MapBoardDirectRenderer renderer)
        {
            if (_isIl2CppAborted || renderer == null)
                return;

            NameplateCardView.PrewarmTownshipFont(LoggerInstance, _config.EffectiveDebugLogging);
            AttachToRenderer(renderer);
        }

        public void NotifyBoardRendererDestroy(MapBoardDirectRenderer renderer)
        {
            if (renderer == null)
                return;

            if (_controllersByRenderer.TryGetValue(renderer, out MapBoardInspectorController ctrl))
            {
                _controllersByRenderer.Remove(renderer);
                if (ctrl != null)
                {
                    if (ctrl.Board != null)
                        _controllersByBoard.Remove(ctrl.Board);
                    ctrl.Cleanup();
                }
            }
        }

        public void NotifyLandmarkDisplaySetup(MapLandmarkDisplay display, MapBoard board)
        {
            if (_isIl2CppAborted)
                return;

            if (board != null && _controllersByBoard.TryGetValue(board, out MapBoardInspectorController ctrl) && ctrl != null)
            {
                ctrl.MarkPinsDirty();
                return;
            }

            if (board != null)
            {
                MapBoardDirectRenderer renderer = board.GetComponentInChildren<MapBoardDirectRenderer>(true);
                if (renderer != null)
                    AttachToRenderer(renderer);
            }
        }

        public void UnregisterController(MapBoardInspectorController controller)
        {
            if (controller == null)
                return;

            if (controller.Board != null &&
                _controllersByBoard.TryGetValue(controller.Board, out MapBoardInspectorController existing) &&
                existing == controller)
            {
                _controllersByBoard.Remove(controller.Board);
            }

            MapBoardDirectRenderer matchedKey = null;
            foreach (KeyValuePair<MapBoardDirectRenderer, MapBoardInspectorController> kv in _controllersByRenderer)
            {
                if (kv.Value == controller)
                {
                    matchedKey = kv.Key;
                    break;
                }
            }

            if (matchedKey != null)
                _controllersByRenderer.Remove(matchedKey);
        }

        private void OnPlayerRemoved(Player removedPlayer)
        {
            int playerId = 0;
            try
            {
                if (removedPlayer != null && removedPlayer.UserInfo != null)
                    playerId = removedPlayer.UserInfo.Identifier;
            }
            catch
            {
                playerId = 0;
            }

            foreach (KeyValuePair<MapBoardDirectRenderer, MapBoardInspectorController> kv in _controllersByRenderer)
            {
                if (kv.Value != null)
                    kv.Value.OnPlayerDisconnected(playerId);
            }
        }

        private void ScanExistingBoards()
        {
            try
            {
                MapBoardDirectRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<MapBoardDirectRenderer>();
                if (renderers == null)
                    return;

                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        AttachToRenderer(renderers[i]);
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[MapPinInspector] Error scanning Map Boards: {ex.Message}");
            }
        }

        private void AttachToRenderer(MapBoardDirectRenderer renderer)
        {
            if (renderer == null || _controllersByRenderer.ContainsKey(renderer))
                return;

            if (!HarmonyPatches.TryExtractRendererRefs(
                    renderer,
                    out MapBoard mapBoard,
                    out MapLandmarkDisplay landmarkDisplay,
                    out Renderer previewRenderer,
                    out bool wasDestroyed) ||
                wasDestroyed)
            {
                return;
            }

            MapBoardInspectorController controller = renderer.GetComponent<MapBoardInspectorController>();
            if (controller == null)
                controller = renderer.gameObject.AddComponent<MapBoardInspectorController>();

            controller.Initialize(mapBoard, landmarkDisplay, previewRenderer, _config, LoggerInstance);
            _controllersByRenderer[renderer] = controller;
            _controllersByBoard[mapBoard] = controller;
        }

        private void CleanupAllControllers()
        {
            var copy = new List<MapBoardInspectorController>(_controllersByRenderer.Values);
            _controllersByRenderer.Clear();
            _controllersByBoard.Clear();

            for (int i = 0; i < copy.Count; i++)
            {
                if (copy[i] != null)
                    copy[i].Cleanup();
            }
        }
    }
}
