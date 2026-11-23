using System;
using System.Collections.Generic;

namespace MapPinInspector.Core
{
    public enum TapOutcome
    {
        None,
        Opened,
        SwitchedPin,
        CycledCluster,
        Dismissed
    }

    public sealed class PinSample
    {
        public uint MarkerId { get; }
        public Vec3 SurfaceWorldPos { get; }
        public Vec2 WorldXZ { get; }
        public bool IsFriend { get; }
        public bool IsDowned { get; }
        public bool IsSpirit { get; }
        public bool IsLocalPlayer { get; }
        public string DisplayName { get; }
        public string StatusLabel { get; }

        public PinSample(
            uint markerId,
            Vec3 surfaceWorldPos,
            Vec2 worldXZ,
            bool isFriend,
            bool isDowned,
            bool isSpirit,
            bool isLocalPlayer,
            string displayName,
            string statusLabel)
        {
            MarkerId = markerId;
            SurfaceWorldPos = surfaceWorldPos;
            WorldXZ = worldXZ;
            IsFriend = isFriend;
            IsDowned = isDowned;
            IsSpirit = isSpirit;
            IsLocalPlayer = isLocalPlayer;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Unknown Traveler" : displayName;
            StatusLabel = statusLabel ?? string.Empty;
        }
    }

    public sealed class ClusterSelection
    {
        public uint PrimaryMarkerId { get; }
        public IReadOnlyList<PinSample> Members { get; }
        public int ActiveFocusIndex { get; }

        public PinSample FocusedMember =>
            Members.Count == 0
                ? null
                : Members[Math.Max(0, Math.Min(ActiveFocusIndex, Members.Count - 1))];

        public ClusterSelection(uint primaryMarkerId, IReadOnlyList<PinSample> members, int activeFocusIndex)
        {
            PrimaryMarkerId = primaryMarkerId;
            Members = members ?? Array.Empty<PinSample>();
            ActiveFocusIndex = activeFocusIndex;
        }
    }

    /// <summary>
    /// Groups overlapping player pins within ClusterRadius and drives toggle/cycle/switch tap state transitions.
    /// </summary>
    public static class PinClusterResolver
    {
        public static ClusterSelection BuildCluster(
            uint tappedMarkerId,
            IReadOnlyList<PinSample> allPins,
            float clusterRadiusMeters,
            int focusIndex = 0)
        {
            if (allPins == null || allPins.Count == 0)
                return null;

            PinSample tapped = null;
            for (int i = 0; i < allPins.Count; i++)
            {
                if (allPins[i].MarkerId == tappedMarkerId)
                {
                    tapped = allPins[i];
                    break;
                }
            }

            if (tapped == null)
                return null;

            float radius = clusterRadiusMeters < 0f ? 0f : clusterRadiusMeters;
            float radiusSq = radius * radius;

            var scored = new List<(PinSample pin, float distSq)>(allPins.Count);
            for (int i = 0; i < allPins.Count; i++)
            {
                PinSample candidate = allPins[i];
                float distSq = Vec3.DistanceSquared(tapped.SurfaceWorldPos, candidate.SurfaceWorldPos);
                if (candidate.MarkerId == tapped.MarkerId || distSq <= radiusSq)
                    scored.Add((candidate, distSq));
            }

            scored.Sort((a, b) =>
            {
                // Keep tapped pin first, then sort neighbors by distance, then deterministic MarkerId.
                bool aIsTapped = a.pin.MarkerId == tapped.MarkerId;
                bool bIsTapped = b.pin.MarkerId == tapped.MarkerId;
                if (aIsTapped != bIsTapped)
                    return aIsTapped ? -1 : 1;

                int distCmp = a.distSq.CompareTo(b.distSq);
                if (distCmp != 0)
                    return distCmp;

                return a.pin.MarkerId.CompareTo(b.pin.MarkerId);
            });

            var members = new List<PinSample>(scored.Count);
            for (int i = 0; i < scored.Count; i++)
                members.Add(scored[i].pin);

            int clampedFocus = members.Count == 0
                ? 0
                : Math.Max(0, Math.Min(focusIndex, members.Count - 1));

            return new ClusterSelection(tapped.MarkerId, members, clampedFocus);
        }

        public static TapOutcome ResolveTap(
            ClusterSelection currentSelection,
            uint tappedMarkerId,
            IReadOnlyList<PinSample> allPins,
            float clusterRadiusMeters,
            out ClusterSelection nextSelection)
        {
            nextSelection = null;

            if (allPins == null || allPins.Count == 0)
                return TapOutcome.None;

            if (currentSelection == null || currentSelection.Members.Count == 0)
            {
                nextSelection = BuildCluster(tappedMarkerId, allPins, clusterRadiusMeters, 0);
                return nextSelection != null ? TapOutcome.Opened : TapOutcome.None;
            }

            bool tappedIsInsideActiveCluster = false;
            for (int i = 0; i < currentSelection.Members.Count; i++)
            {
                if (currentSelection.Members[i].MarkerId == tappedMarkerId)
                {
                    tappedIsInsideActiveCluster = true;
                    break;
                }
            }

            if (!tappedIsInsideActiveCluster)
            {
                nextSelection = BuildCluster(tappedMarkerId, allPins, clusterRadiusMeters, 0);
                return nextSelection != null ? TapOutcome.SwitchedPin : TapOutcome.None;
            }

            // Refresh cluster membership from current live positions while preserving focus index.
            ClusterSelection refreshed = BuildCluster(
                currentSelection.PrimaryMarkerId,
                allPins,
                clusterRadiusMeters,
                currentSelection.ActiveFocusIndex);

            if (refreshed == null)
            {
                nextSelection = BuildCluster(tappedMarkerId, allPins, clusterRadiusMeters, 0);
                return nextSelection != null ? TapOutcome.SwitchedPin : TapOutcome.None;
            }

            if (refreshed.Members.Count <= 1)
            {
                nextSelection = null;
                return TapOutcome.Dismissed;
            }

            int nextIndex = refreshed.ActiveFocusIndex + 1;
            if (nextIndex >= refreshed.Members.Count)
            {
                nextSelection = null;
                return TapOutcome.Dismissed;
            }

            nextSelection = new ClusterSelection(refreshed.PrimaryMarkerId, refreshed.Members, nextIndex);
            return TapOutcome.CycledCluster;
        }

        public static ClusterSelection RefreshActiveCluster(
            ClusterSelection currentSelection,
            IReadOnlyList<PinSample> allPins,
            float clusterRadiusMeters)
        {
            if (currentSelection == null || allPins == null || allPins.Count == 0)
                return null;

            uint focusedMarkerId = currentSelection.FocusedMember != null
                ? currentSelection.FocusedMember.MarkerId
                : currentSelection.PrimaryMarkerId;

            ClusterSelection rebuilt = BuildCluster(currentSelection.PrimaryMarkerId, allPins, clusterRadiusMeters, 0);
            if (rebuilt == null)
            {
                // Primary disappeared; see if any other cluster member still exists.
                for (int i = 0; i < currentSelection.Members.Count; i++)
                {
                    rebuilt = BuildCluster(currentSelection.Members[i].MarkerId, allPins, clusterRadiusMeters, 0);
                    if (rebuilt != null)
                        break;
                }
            }

            if (rebuilt == null)
                return null;

            int newFocusIndex = 0;
            for (int i = 0; i < rebuilt.Members.Count; i++)
            {
                if (rebuilt.Members[i].MarkerId == focusedMarkerId)
                {
                    newFocusIndex = i;
                    break;
                }
            }

            return new ClusterSelection(rebuilt.PrimaryMarkerId, rebuilt.Members, newFocusIndex);
        }
    }
}
