using System;
using System.Collections.Generic;
using Alta.Map;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace MapPinInspector
{
    /// <summary>
    /// Defensive Harmony patches for Alta.Map.MapBoardDirectRenderer and Alta.Map.MapLandmarkDisplay.
    /// </summary>
    internal static class HarmonyPatches
    {
        private static readonly AccessTools.FieldRef<MapBoardDirectRenderer, MapBoard> DirectMapBoardRef =
            CreateFieldRef<MapBoardDirectRenderer, MapBoard>("mapBoard");

        private static readonly AccessTools.FieldRef<MapBoardDirectRenderer, MapLandmarkDisplay> DirectLandmarkDisplayRef =
            CreateFieldRef<MapBoardDirectRenderer, MapLandmarkDisplay>("landmarkDisplay");

        private static readonly AccessTools.FieldRef<MapBoardDirectRenderer, Renderer> DirectPreviewRendererRef =
            CreateFieldRef<MapBoardDirectRenderer, Renderer>("previewRenderer");

        private static readonly AccessTools.FieldRef<MapBoardDirectRenderer, bool> DirectWasDestroyedRef =
            CreateFieldRef<MapBoardDirectRenderer, bool>("wasDestroyed");

        private static readonly AccessTools.FieldRef<MapLandmarkDisplay, Dictionary<uint, MarkerInfo>> ActiveMarkersRef =
            CreateFieldRef<MapLandmarkDisplay, Dictionary<uint, MarkerInfo>>("activeMarkers");

        private static readonly AccessTools.FieldRef<MapLandmarkDisplay, MapBoard> DisplayMapBoardRef =
            CreateFieldRef<MapLandmarkDisplay, MapBoard>("mapBoard");

        private static MelonLogger.Instance _logger;

        private static AccessTools.FieldRef<TInstance, TField> CreateFieldRef<TInstance, TField>(string fieldName)
        {
            try
            {
                return AccessTools.FieldRefAccess<TInstance, TField>(fieldName);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[MapPinInspector] Failed creating FieldRef {typeof(TInstance).Name}.{fieldName}: {ex.Message}");
                return null;
            }
        }

        public static void Apply(HarmonyLib.Harmony harmony, MelonLogger.Instance logger)
        {
            _logger = logger;

            try
            {
                harmony.PatchAll(typeof(HarmonyPatches).Assembly);
                logger.Msg("[MapPinInspector] Applied Harmony patches to MapBoardDirectRenderer & MapLandmarkDisplay.");
            }
            catch (Exception ex)
            {
                logger.Error($"[MapPinInspector] Failed applying Harmony patches: {ex}");
            }
        }

        public static bool TryExtractRendererRefs(
            MapBoardDirectRenderer renderer,
            out MapBoard mapBoard,
            out MapLandmarkDisplay landmarkDisplay,
            out Renderer previewRenderer,
            out bool wasDestroyed)
        {
            mapBoard = null;
            landmarkDisplay = null;
            previewRenderer = null;
            wasDestroyed = false;

            if (renderer == null)
                return false;

            try
            {
                wasDestroyed = DirectWasDestroyedRef != null && DirectWasDestroyedRef(renderer);
                if (wasDestroyed)
                    return false;

                mapBoard = DirectMapBoardRef != null ? DirectMapBoardRef(renderer) : null;
                landmarkDisplay = DirectLandmarkDisplayRef != null ? DirectLandmarkDisplayRef(renderer) : null;
                previewRenderer = DirectPreviewRendererRef != null
                    ? DirectPreviewRendererRef(renderer)
                    : renderer.GetComponent<Renderer>();

                if (mapBoard == null)
                    mapBoard = renderer.GetComponentInParent<MapBoard>();
                if (landmarkDisplay == null && mapBoard != null)
                    landmarkDisplay = mapBoard.GetComponent<MapLandmarkDisplay>();

                return mapBoard != null && landmarkDisplay != null;
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[MapPinInspector] Error extracting MapBoardDirectRenderer refs: {ex.Message}");
                return false;
            }
        }

        public static Dictionary<uint, MarkerInfo> TryGetDisplayActiveMarkers(MapLandmarkDisplay display)
        {
            if (display == null || ActiveMarkersRef == null)
                return null;

            try
            {
                return ActiveMarkersRef(display);
            }
            catch
            {
                return null;
            }
        }

        [HarmonyPatch(typeof(MapBoardDirectRenderer), "Awake")]
        private static class MapBoardDirectRendererAwakePatch
        {
            private static void Postfix(MapBoardDirectRenderer __instance)
            {
                try
                {
                    if (__instance == null || ApplicationManager.IsHeadless)
                        return;

                    MapPinInspectorMod.Instance?.NotifyBoardRendererAwake(__instance);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"[MapPinInspector] Exception in MapBoardDirectRenderer.Awake postfix: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(MapBoardDirectRenderer), "OnDestroy")]
        private static class MapBoardDirectRendererOnDestroyPatch
        {
            private static void Prefix(MapBoardDirectRenderer __instance)
            {
                try
                {
                    if (__instance == null)
                        return;

                    MapPinInspectorMod.Instance?.NotifyBoardRendererDestroy(__instance);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"[MapPinInspector] Exception in MapBoardDirectRenderer.OnDestroy prefix: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(MapLandmarkDisplay), "Setup", new[] { typeof(MapBoard) })]
        private static class MapLandmarkDisplaySetupPatch
        {
            private static void Postfix(MapLandmarkDisplay __instance, MapBoard mapBoard)
            {
                try
                {
                    if (__instance == null)
                        return;

                    MapBoard board = mapBoard != null
                        ? mapBoard
                        : (DisplayMapBoardRef != null ? DisplayMapBoardRef(__instance) : null);

                    MapPinInspectorMod.Instance?.NotifyLandmarkDisplaySetup(__instance, board);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"[MapPinInspector] Exception in MapLandmarkDisplay.Setup postfix: {ex}");
                }
            }
        }
    }
}
