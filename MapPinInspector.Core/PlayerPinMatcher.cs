using System;
using System.Collections.Generic;

namespace MapPinInspector.Core
{
    public readonly struct PlayerCandidate
    {
        public readonly int PlayerId;
        public readonly string DisplayName;
        public readonly Vec2 WorldXZ;
        public readonly bool HasWorldXZ;
        public readonly bool IsFriend;
        public readonly bool IsLocalPlayer;
        public readonly bool IsDowned;
        public readonly bool IsSpirit;
        public readonly uint LandmarkIndex;
        public readonly uint NetworkEntityId;

        public PlayerCandidate(
            int playerId,
            string displayName,
            Vec2 worldXZ,
            bool hasWorldXZ,
            bool isFriend,
            bool isLocalPlayer,
            bool isDowned,
            bool isSpirit,
            uint landmarkIndex,
            uint networkEntityId = 0)
        {
            PlayerId = playerId;
            DisplayName = displayName ?? string.Empty;
            WorldXZ = worldXZ;
            HasWorldXZ = hasWorldXZ;
            IsFriend = isFriend;
            IsLocalPlayer = isLocalPlayer;
            IsDowned = isDowned;
            IsSpirit = isSpirit;
            LandmarkIndex = landmarkIndex;
            NetworkEntityId = networkEntityId;
        }
    }

    public readonly struct PinCorrelationInput
    {
        public readonly uint MarkerIndex;
        public readonly Vec2 WorldXZ;
        public readonly bool? IsFriendIcon;

        public PinCorrelationInput(uint markerIndex, Vec2 worldXZ, bool? isFriendIcon)
        {
            MarkerIndex = markerIndex;
            WorldXZ = worldXZ;
            IsFriendIcon = isFriendIcon;
        }
    }

    public readonly struct MatchedPinResult
    {
        public readonly uint MarkerIndex;
        public readonly bool Matched;
        public readonly bool IsHighConfidence;
        public readonly PlayerCandidate Player;

        public MatchedPinResult(uint markerIndex, bool matched, PlayerCandidate player, bool isHighConfidence = false)
        {
            MarkerIndex = markerIndex;
            Matched = matched;
            IsHighConfidence = isHighConfidence;
            Player = player;
        }
    }

    /// <summary>
    /// Correlates ClientLandmarkManager MarkerInfo pins to Player candidates using:
    /// 1) Exact Landmark.Index (host/server),
    /// 2) Spatial XZ + friend-parity matching for chunk-loaded players,
    /// 3) Persistent session bindings for players who walked out of chunk range,
    /// 4) Friend-bucket + join-order (NetworkEntityId / MarkerIndex) matching for distant out-of-chunk players.
    /// </summary>
    public static class PlayerPinMatcher
    {
        private readonly struct MatchPair : IComparable<MatchPair>
        {
            public readonly int PinIdx;
            public readonly int PlayerIdx;
            public readonly int FriendMismatchPenalty;
            public readonly float DistanceSq;

            public MatchPair(int pinIdx, int playerIdx, int friendMismatchPenalty, float distanceSq)
            {
                PinIdx = pinIdx;
                PlayerIdx = playerIdx;
                FriendMismatchPenalty = friendMismatchPenalty;
                DistanceSq = distanceSq;
            }

            public int CompareTo(MatchPair other)
            {
                int friendCmp = FriendMismatchPenalty.CompareTo(other.FriendMismatchPenalty);
                if (friendCmp != 0)
                    return friendCmp;

                int distCmp = DistanceSq.CompareTo(other.DistanceSq);
                if (distCmp != 0)
                    return distCmp;

                int pinCmp = PinIdx.CompareTo(other.PinIdx);
                return pinCmp != 0 ? pinCmp : PlayerIdx.CompareTo(other.PlayerIdx);
            }
        }

        public static Dictionary<uint, MatchedPinResult> MatchAll(
            IReadOnlyList<PinCorrelationInput> pins,
            IReadOnlyList<PlayerCandidate> players,
            float maxMatchDistanceMeters = 32f,
            IReadOnlyDictionary<uint, int> knownBindings = null)
        {
            var results = new Dictionary<uint, MatchedPinResult>();
            if (pins == null || pins.Count == 0)
                return results;

            if (players == null || players.Count == 0)
            {
                for (int i = 0; i < pins.Count; i++)
                    results[pins[i].MarkerIndex] = new MatchedPinResult(pins[i].MarkerIndex, false, default);
                return results;
            }

            var pinMatched = new bool[pins.Count];
            var playerMatched = new bool[players.Count];

            // Tier 1: Exact Landmark.Index match (present on host/server or when landmark indices match).
            for (int i = 0; i < pins.Count; i++)
            {
                uint markerIdx = pins[i].MarkerIndex;
                for (int j = 0; j < players.Count; j++)
                {
                    if (playerMatched[j])
                        continue;

                    if (players[j].LandmarkIndex != 0 && players[j].LandmarkIndex == markerIdx)
                    {
                        pinMatched[i] = true;
                        playerMatched[j] = true;
                        results[markerIdx] = new MatchedPinResult(markerIdx, true, players[j], isHighConfidence: true);
                        break;
                    }
                }
            }

            // Tier 2: Greedy spatial + friend-parity correlation for chunk-loaded players.
            float clampedMaxDist = maxMatchDistanceMeters < 1f ? 1f : maxMatchDistanceMeters;
            float maxDistSq = clampedMaxDist * clampedMaxDist;

            var pairs = new List<MatchPair>();
            for (int i = 0; i < pins.Count; i++)
            {
                if (pinMatched[i])
                    continue;

                PinCorrelationInput pin = pins[i];
                for (int j = 0; j < players.Count; j++)
                {
                    if (playerMatched[j] || !players[j].HasWorldXZ)
                        continue;

                    float distSq = Vec2.DistanceSquared(pin.WorldXZ, players[j].WorldXZ);
                    if (distSq > maxDistSq)
                        continue;

                    int penalty = 0;
                    if (pin.IsFriendIcon.HasValue)
                    {
                        bool expectedFriendIcon = !players[j].IsLocalPlayer && players[j].IsFriend;
                        if (pin.IsFriendIcon.Value != expectedFriendIcon)
                            penalty = 1;
                    }

                    pairs.Add(new MatchPair(i, j, penalty, distSq));
                }
            }

            pairs.Sort();

            for (int k = 0; k < pairs.Count; k++)
            {
                MatchPair pair = pairs[k];
                if (pinMatched[pair.PinIdx] || playerMatched[pair.PlayerIdx])
                    continue;

                pinMatched[pair.PinIdx] = true;
                playerMatched[pair.PlayerIdx] = true;
                uint markerIdx = pins[pair.PinIdx].MarkerIndex;
                results[markerIdx] = new MatchedPinResult(markerIdx, true, players[pair.PlayerIdx], isHighConfidence: true);
            }

            // Tier 3: Restore persistent session bindings for players who left loaded chunk range.
            if (knownBindings != null && knownBindings.Count > 0)
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    if (pinMatched[i])
                        continue;

                    uint markerIdx = pins[i].MarkerIndex;
                    if (!knownBindings.TryGetValue(markerIdx, out int boundPlayerId))
                        continue;

                    for (int j = 0; j < players.Count; j++)
                    {
                        if (playerMatched[j] || players[j].PlayerId != boundPlayerId)
                            continue;

                        pinMatched[i] = true;
                        playerMatched[j] = true;
                        results[markerIdx] = new MatchedPinResult(markerIdx, true, players[j], isHighConfidence: true);
                        break;
                    }
                }
            }

            // Tier 4: Match remaining unmatched pins to remaining remote players (out-of-chunk players whose
            // PlayerController is unloaded on the client, so Player.AllPlayers has their name & friend state but no XZ).
            var unmatchedPinIndices = new List<int>();
            for (int i = 0; i < pins.Count; i++)
            {
                if (!pinMatched[i])
                    unmatchedPinIndices.Add(i);
            }

            var unmatchedRemotePlayerIndices = new List<int>();
            for (int j = 0; j < players.Count; j++)
            {
                // Never assign a distant unmatched pin to the local player (local player always has PlayerController).
                if (!playerMatched[j] && !players[j].IsLocalPlayer)
                    unmatchedRemotePlayerIndices.Add(j);
            }

            if (unmatchedPinIndices.Count > 0 && unmatchedRemotePlayerIndices.Count > 0)
            {
                unmatchedPinIndices.Sort((a, b) => pins[a].MarkerIndex.CompareTo(pins[b].MarkerIndex));
                unmatchedRemotePlayerIndices.Sort((a, b) =>
                {
                    int netCmp = players[a].NetworkEntityId.CompareTo(players[b].NetworkEntityId);
                    return netCmp != 0 ? netCmp : players[a].PlayerId.CompareTo(players[b].PlayerId);
                });

                // Pass 4a: Match strictly within matching friend buckets (green icon <-> friend, red icon <-> non-friend).
                PairUnmatchedByOrder(
                    pins,
                    players,
                    unmatchedPinIndices,
                    unmatchedRemotePlayerIndices,
                    pinMatched,
                    playerMatched,
                    results,
                    requireFriendMatch: true);

                // Pass 4b: Fallback across remaining unmatched remote players (e.g. while friend list is syncing).
                PairUnmatchedByOrder(
                    pins,
                    players,
                    unmatchedPinIndices,
                    unmatchedRemotePlayerIndices,
                    pinMatched,
                    playerMatched,
                    results,
                    requireFriendMatch: false);
            }

            // Fill any still-unmatched pins gracefully.
            for (int i = 0; i < pins.Count; i++)
            {
                if (!pinMatched[i])
                {
                    uint markerIdx = pins[i].MarkerIndex;
                    results[markerIdx] = new MatchedPinResult(markerIdx, false, default);
                }
            }

            return results;
        }

        private static void PairUnmatchedByOrder(
            IReadOnlyList<PinCorrelationInput> pins,
            IReadOnlyList<PlayerCandidate> players,
            List<int> unmatchedPinIndices,
            List<int> unmatchedRemotePlayerIndices,
            bool[] pinMatched,
            bool[] playerMatched,
            Dictionary<uint, MatchedPinResult> results,
            bool requireFriendMatch)
        {
            for (int p = 0; p < unmatchedPinIndices.Count; p++)
            {
                int pinIdx = unmatchedPinIndices[p];
                if (pinMatched[pinIdx])
                    continue;

                PinCorrelationInput pin = pins[pinIdx];
                if (requireFriendMatch && !pin.IsFriendIcon.HasValue)
                    continue;

                for (int q = 0; q < unmatchedRemotePlayerIndices.Count; q++)
                {
                    int playerIdx = unmatchedRemotePlayerIndices[q];
                    if (playerMatched[playerIdx])
                        continue;

                    if (requireFriendMatch && pin.IsFriendIcon.Value != players[playerIdx].IsFriend)
                        continue;

                    pinMatched[pinIdx] = true;
                    playerMatched[playerIdx] = true;
                    results[pin.MarkerIndex] = new MatchedPinResult(
                        pin.MarkerIndex,
                        true,
                        players[playerIdx],
                        isHighConfidence: false);
                    break;
                }
            }
        }
    }
}
