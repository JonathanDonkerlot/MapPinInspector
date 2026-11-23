using System;
using System.Collections.Generic;
using Alta.Chunks;
using Alta.Map;
using Alta.Networking.Scripts.Player;
using HarmonyLib;
using MapPinInspector.Core;
using UnityEngine;

namespace MapPinInspector
{
    public readonly struct ResolvedPinMetadata
    {
        public readonly bool IsPlayerPin;
        public readonly bool IsFriend;
        public readonly bool IsDowned;
        public readonly bool IsSpirit;
        public readonly bool IsLocalPlayer;
        public readonly string DisplayName;
        public readonly string StatusLabel;

        public ResolvedPinMetadata(
            bool isPlayerPin,
            bool isFriend,
            bool isDowned,
            bool isSpirit,
            bool isLocalPlayer,
            string displayName,
            string statusLabel)
        {
            IsPlayerPin = isPlayerPin;
            IsFriend = isFriend;
            IsDowned = isDowned;
            IsSpirit = isSpirit;
            IsLocalPlayer = isLocalPlayer;
            DisplayName = displayName;
            StatusLabel = statusLabel;
        }
    }

    /// <summary>
    /// Identifies player pins among MapBoard markers and correlates each pin with live Player metadata.
    /// </summary>
    public sealed class PlayerMetadataResolver
    {
        private static readonly AccessTools.FieldRef<PlayerLandmark, LandmarkIcon> FriendIconRef =
            CreateFriendIconRef();

        private LandmarkIcon _cachedFriendIcon;
        private LandmarkIcon _cachedNonFriendIcon;
        private float _nextIconScanTime;

        private readonly Dictionary<uint, PlayerLandmark> _landmarksByIndex = new Dictionary<uint, PlayerLandmark>();
        private readonly List<PinCorrelationInput> _correlationInputs = new List<PinCorrelationInput>();
        private readonly List<PlayerCandidate> _playerCandidates = new List<PlayerCandidate>();

        private static AccessTools.FieldRef<PlayerLandmark, LandmarkIcon> CreateFriendIconRef()
        {
            try
            {
                return AccessTools.FieldRefAccess<PlayerLandmark, LandmarkIcon>("friendIcon");
            }
            catch
            {
                return null;
            }
        }

        public void RefreshPlayerIconCache(float nowSeconds)
        {
            if (nowSeconds < _nextIconScanTime && _cachedFriendIcon != null && _cachedNonFriendIcon != null)
                return;

            _nextIconScanTime = nowSeconds + 2.0f;
            _landmarksByIndex.Clear();

            try
            {
                PlayerLandmark[] landmarks = UnityEngine.Object.FindObjectsOfType<PlayerLandmark>();
                if (landmarks == null)
                    return;

                for (int i = 0; i < landmarks.Length; i++)
                {
                    PlayerLandmark lm = landmarks[i];
                    if (lm == null)
                        continue;

                    if (lm.Index != 0)
                        _landmarksByIndex[lm.Index] = lm;

                    if (_cachedNonFriendIcon == null && lm.DiscoveredIcon != null)
                        _cachedNonFriendIcon = lm.DiscoveredIcon;

                    if (_cachedFriendIcon == null && FriendIconRef != null)
                    {
                        LandmarkIcon fi = FriendIconRef(lm);
                        if (fi != null)
                            _cachedFriendIcon = fi;
                    }
                }
            }
            catch
            {
                // Ignore transient Unity object errors during scene load/unload.
            }
        }

        public bool ClassifyMarker(MarkerInfo marker, out bool isFriendIcon)
        {
            isFriendIcon = false;
            if (marker == null || marker.DiscoveredIcon == null)
                return false;

            LandmarkIcon icon = marker.DiscoveredIcon;

            if (_cachedFriendIcon != null && icon == _cachedFriendIcon)
            {
                isFriendIcon = true;
                return true;
            }

            if (_cachedNonFriendIcon != null && icon == _cachedNonFriendIcon)
            {
                isFriendIcon = false;
                return true;
            }

            if (marker.Index != 0 && _landmarksByIndex.ContainsKey(marker.Index))
            {
                isFriendIcon = false;
                return true;
            }

            // Secondary name-based check before any PlayerLandmark has spawned in the scene.
            if (_cachedFriendIcon == null && _cachedNonFriendIcon == null)
            {
                string iconName = icon.name ?? string.Empty;
                if (iconName.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isFriendIcon = iconName.IndexOf("friend", StringComparison.OrdinalIgnoreCase) >= 0;
                    return true;
                }
            }

            return false;
        }

        public Dictionary<uint, ResolvedPinMetadata> ResolveBatch(
            IReadOnlyList<MarkerInfo> playerMarkers,
            Vector3 boardWorldCenter,
            bool includeStatusInfo)
        {
            var output = new Dictionary<uint, ResolvedPinMetadata>();
            if (playerMarkers == null || playerMarkers.Count == 0)
                return output;

            _correlationInputs.Clear();
            var iconIsFriendLookup = new Dictionary<uint, bool>(playerMarkers.Count);

            for (int i = 0; i < playerMarkers.Count; i++)
            {
                MarkerInfo marker = playerMarkers[i];
                if (!ClassifyMarker(marker, out bool iconFriend))
                    continue;

                iconIsFriendLookup[marker.Index] = iconFriend;
                _correlationInputs.Add(new PinCorrelationInput(
                    marker.Index,
                    new Vec2(marker.Position.x, marker.Position.y),
                    (_cachedFriendIcon != null || _cachedNonFriendIcon != null) ? (bool?)iconFriend : null));
            }

            CollectPlayerCandidates();

            Dictionary<uint, MatchedPinResult> matched = PlayerPinMatcher.MatchAll(
                _correlationInputs,
                _playerCandidates,
                maxMatchDistanceMeters: 35f);

            for (int i = 0; i < playerMarkers.Count; i++)
            {
                MarkerInfo marker = playerMarkers[i];
                if (!iconIsFriendLookup.TryGetValue(marker.Index, out bool iconFriend))
                    continue;

                if (matched.TryGetValue(marker.Index, out MatchedPinResult res) && res.Matched)
                {
                    PlayerCandidate p = res.Player;
                    bool effectiveFriend = iconFriend || (!p.IsLocalPlayer && p.IsFriend);
                    string displayName = p.DisplayName;
                    string status = includeStatusInfo
                        ? BuildStatusLabel(effectiveFriend, p.IsLocalPlayer, p.IsDowned, p.IsSpirit, marker.Position, boardWorldCenter)
                        : string.Empty;

                    output[marker.Index] = new ResolvedPinMetadata(
                        true,
                        effectiveFriend,
                        p.IsDowned,
                        p.IsSpirit,
                        p.IsLocalPlayer,
                        displayName,
                        status);
                }
                else
                {
                    string fallbackName = iconFriend ? "Friend" : "Traveler";
                    string status = includeStatusInfo
                        ? BuildStatusLabel(iconFriend, false, false, false, marker.Position, boardWorldCenter)
                        : string.Empty;

                    output[marker.Index] = new ResolvedPinMetadata(
                        true,
                        iconFriend,
                        false,
                        false,
                        false,
                        fallbackName,
                        status);
                }
            }

            return output;
        }

        private void CollectPlayerCandidates()
        {
            _playerCandidates.Clear();

            try
            {
                HashSet<IPlayer> allPlayers = Player.AllPlayers;
                if (allPlayers == null)
                    return;

                foreach (IPlayer iPlayer in allPlayers)
                {
                    if (iPlayer == null)
                        continue;

                    string username = iPlayer.UserInfo != null && !string.IsNullOrWhiteSpace(iPlayer.UserInfo.Username)
                        ? iPlayer.UserInfo.Username
                        : iPlayer.UnityObjectName;

                    int id = iPlayer.UserInfo != null ? iPlayer.UserInfo.Identifier : iPlayer.GetHashCode();
                    PlayerController pc = iPlayer.PlayerController;
                    bool isLocal = iPlayer.IsLocalPlayer;
                    bool isFriend = iPlayer.FriendshipManager != null && iPlayer.FriendshipManager.IsFriend;
                    bool isDowned = pc != null && pc.IsDowned;
                    bool isSpirit = pc != null && pc.IsSpirit;

                    uint landmarkIndex = 0;
                    if (pc != null)
                    {
                        PlayerLandmark lm = pc.GetComponent<PlayerLandmark>();
                        if (lm != null && lm.Index != 0)
                            landmarkIndex = lm.Index;
                    }

                    Vec2 worldXZ = Vec2.Zero;
                    bool hasPos = false;

                    if (pc != null)
                    {
                        Vector3 pos = pc.transform.position;
                        worldXZ = new Vec2(pos.x, pos.z);
                        hasPos = true;
                    }
                    else if (iPlayer is Player concretePlayer && concretePlayer.Position.HasValue)
                    {
                        Vector3 pos = concretePlayer.Position.Value;
                        worldXZ = new Vec2(pos.x, pos.z);
                        hasPos = true;
                    }

                    _playerCandidates.Add(new PlayerCandidate(
                        id,
                        username,
                        worldXZ,
                        hasPos,
                        isFriend,
                        isLocal,
                        isDowned,
                        isSpirit,
                        landmarkIndex));
                }
            }
            catch
            {
                // Ignore transient collection modification during player connect/disconnect.
            }
        }

        private static string BuildStatusLabel(
            bool isFriend,
            bool isLocal,
            bool isDowned,
            bool isSpirit,
            Vector2 markerWorldXZ,
            Vector3 boardWorldCenter)
        {
            string role = isLocal ? "Local" : (isFriend ? "Friend" : "Player");

            string state = string.Empty;
            if (isSpirit)
                state = " • Spirit";
            else if (isDowned)
                state = " • Downed";

            string zone = ResolveZoneName(markerWorldXZ);
            float distMeters = Vector2.Distance(
                markerWorldXZ,
                new Vector2(boardWorldCenter.x, boardWorldCenter.z));

            return !string.IsNullOrEmpty(zone)
                ? $"{role}{state} • {zone} ({distMeters:F0}m)"
                : $"{role}{state} • ({markerWorldXZ.x:F0}, {markerWorldXZ.y:F0}) • {distMeters:F0}m";
        }

        private static string ResolveZoneName(Vector2 worldXZ)
        {
            try
            {
                HashSet<LocationChunk> chunks = LocationChunk.AllLocationChunks;
                if (chunks == null || chunks.Count == 0)
                    return string.Empty;

                LocationChunk best = null;
                float bestDistSq = float.MaxValue;

                foreach (LocationChunk chunk in chunks)
                {
                    if (chunk == null)
                        continue;

                    Bounds b = chunk.Bounds;
                    if (worldXZ.x >= b.min.x && worldXZ.x <= b.max.x &&
                        worldXZ.y >= b.min.z && worldXZ.y <= b.max.z)
                    {
                        float dx = b.center.x - worldXZ.x;
                        float dz = b.center.z - worldXZ.y;
                        float dSq = dx * dx + dz * dz;
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            best = chunk;
                        }
                    }
                }

                if (best == null)
                    return string.Empty;

                string raw = best.name ?? string.Empty;
                raw = raw.Replace("(Clone)", string.Empty).Replace("_", " ").Trim();
                return raw.Length > 26 ? raw.Substring(0, 26).TrimEnd() : raw;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
