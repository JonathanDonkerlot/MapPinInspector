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

        public PlayerCandidate(
            int playerId,
            string displayName,
            Vec2 worldXZ,
            bool hasWorldXZ,
            bool isFriend,
            bool isLocalPlayer,
            bool isDowned,
            bool isSpirit,
            uint landmarkIndex)
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
        public readonly PlayerCandidate Player;

        public MatchedPinResult(uint markerIndex, bool matched, PlayerCandidate player)
        {
            MarkerIndex = markerIndex;
            Matched = matched;
            Player = player;
        }
    }

    /// <summary>
    /// Correlates ClientLandmarkManager MarkerInfo pins to Player candidates using exact LandmarkIndex first,
    /// then greedy minimum-distance XZ + friend-state matching (handling 11-bit network position quantization).
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
            float maxMatchDistanceMeters = 32f)
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
                        results[markerIdx] = new MatchedPinResult(markerIdx, true, players[j]);
                        break;
                    }
                }
            }

            // Tier 2: Greedy spatial + friend-parity correlation for remote client markers.
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
                        // Local player is never a "friend" of themselves; treat local player as non-friend icon.
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
                results[markerIdx] = new MatchedPinResult(markerIdx, true, players[pair.PlayerIdx]);
            }

            // Fill remaining unmatched pins gracefully.
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
    }
}
