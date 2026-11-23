using MelonLoader;
using UnityEngine;

namespace MapPinInspector
{
    /// <summary>
    /// MelonPreferences configuration wrapper for MapPinInspector (persisted in UserData/MelonPreferences.cfg).
    /// </summary>
    public sealed class InspectorConfig
    {
        private MelonPreferences_Category _category;

        public MelonPreferences_Entry<float> AutoDismissDistance { get; private set; }
        public MelonPreferences_Entry<float> CardScale { get; private set; }
        public MelonPreferences_Entry<float> ClusterRadius { get; private set; }
        public MelonPreferences_Entry<bool> ShowStatusInfo { get; private set; }
        public MelonPreferences_Entry<bool> EnableDebugLogging { get; private set; }

        public float EffectiveAutoDismissDistance =>
            Mathf.Clamp(AutoDismissDistance != null ? AutoDismissDistance.Value : 3.5f, 0.8f, 25f);

        public float EffectiveCardScale =>
            Mathf.Clamp(CardScale != null ? CardScale.Value : 1.0f, 0.4f, 2.5f);

        public float EffectiveClusterRadius =>
            Mathf.Clamp(ClusterRadius != null ? ClusterRadius.Value : 0.06f, 0.005f, 0.40f);

        public bool EffectiveShowStatusInfo =>
            ShowStatusInfo == null || ShowStatusInfo.Value;

        public bool EffectiveDebugLogging =>
            EnableDebugLogging != null && EnableDebugLogging.Value;

        public void Initialize()
        {
            _category = MelonPreferences.CreateCategory("MapPinInspector", "Map Pin Inspector");

            AutoDismissDistance = _category.CreateEntry(
                "AutoDismissDistance",
                3.5f,
                "Auto-Dismiss Distance (m)",
                "Distance in meters from the Map Board beyond which an open nameplate card fades and hides.",
                false,
                false,
                null);

            CardScale = _category.CreateEntry(
                "CardScale",
                1.0f,
                "Nameplate Card Scale",
                "Uniform scale multiplier for the 3D world-space nameplate card (0.4 to 2.5).",
                false,
                false,
                null);

            ClusterRadius = _category.CreateEntry(
                "ClusterRadius",
                0.06f,
                "Pin Cluster Radius (m)",
                "Radius in board-surface meters within which overlapping player pins combine into one card.",
                false,
                false,
                null);

            ShowStatusInfo = _category.CreateEntry(
                "ShowStatusInfo",
                true,
                "Show Status Info",
                "Show relationship status (Friend / Player), spirit/downed state, and zone/coordinates.",
                false,
                false,
                null);

            EnableDebugLogging = _category.CreateEntry(
                "EnableDebugLogging",
                false,
                "Enable Debug Logging",
                "Emit verbose [MapPinInspector] diagnostic logs for board attachment, matching, and taps.",
                false,
                false,
                null);

            MelonPreferences.Save();
        }
    }
}
