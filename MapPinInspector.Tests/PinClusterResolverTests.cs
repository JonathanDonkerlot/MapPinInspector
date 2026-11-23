using System.Collections.Generic;
using MapPinInspector.Core;
using Xunit;

namespace MapPinInspector.Tests
{
    public class PinClusterResolverTests
    {
        private static PinSample MakePin(uint id, float x, float y, string name, bool isFriend = true)
        {
            return new PinSample(
                id,
                new Vec3(x, y, 0f),
                new Vec2(x * 100f, y * 100f),
                isFriend,
                isDowned: false,
                isSpirit: false,
                isLocalPlayer: false,
                name,
                isFriend ? "Friend" : "Player");
        }

        [Fact]
        public void SinglePin_TapOpensThenSecondTapDismisses()
        {
            var pins = new List<PinSample>
            {
                MakePin(10, 0f, 0f, "Alice"),
                MakePin(11, 0.4f, 0f, "Bob")
            };

            TapOutcome openOutcome = PinClusterResolver.ResolveTap(null, 10, pins, 0.06f, out ClusterSelection sel1);
            Assert.Equal(TapOutcome.Opened, openOutcome);
            Assert.NotNull(sel1);
            Assert.Single(sel1.Members);
            Assert.Equal("Alice", sel1.FocusedMember.DisplayName);

            TapOutcome closeOutcome = PinClusterResolver.ResolveTap(sel1, 10, pins, 0.06f, out ClusterSelection sel2);
            Assert.Equal(TapOutcome.Dismissed, closeOutcome);
            Assert.Null(sel2);
        }

        [Fact]
        public void TappingDifferentPin_SwitchesFocusDirectly()
        {
            var pins = new List<PinSample>
            {
                MakePin(10, 0f, 0f, "Alice"),
                MakePin(11, 0.4f, 0f, "Bob")
            };

            PinClusterResolver.ResolveTap(null, 10, pins, 0.06f, out ClusterSelection sel1);
            TapOutcome switchOutcome = PinClusterResolver.ResolveTap(sel1, 11, pins, 0.06f, out ClusterSelection sel2);

            Assert.Equal(TapOutcome.SwitchedPin, switchOutcome);
            Assert.NotNull(sel2);
            Assert.Equal(11u, sel2.PrimaryMarkerId);
            Assert.Equal("Bob", sel2.FocusedMember.DisplayName);
        }

        [Fact]
        public void OverlappingCluster_IncludesAllNearbyPinsAndCyclesThroughThenDismisses()
        {
            var pins = new List<PinSample>
            {
                MakePin(10, 0.00f, 0.00f, "Alice", true),
                MakePin(11, 0.02f, 0.01f, "Bob", false),
                MakePin(12, 0.04f, 0.00f, "Cara", true),
                MakePin(13, 0.30f, 0.00f, "DistantDan", true)
            };

            TapOutcome o1 = PinClusterResolver.ResolveTap(null, 10, pins, 0.06f, out ClusterSelection s1);
            Assert.Equal(TapOutcome.Opened, o1);
            Assert.Equal(3, s1.Members.Count);
            Assert.Equal("Alice", s1.FocusedMember.DisplayName);

            TapOutcome o2 = PinClusterResolver.ResolveTap(s1, 10, pins, 0.06f, out ClusterSelection s2);
            Assert.Equal(TapOutcome.CycledCluster, o2);
            Assert.Equal(1, s2.ActiveFocusIndex);
            Assert.Equal("Bob", s2.FocusedMember.DisplayName);

            TapOutcome o3 = PinClusterResolver.ResolveTap(s2, 10, pins, 0.06f, out ClusterSelection s3);
            Assert.Equal(TapOutcome.CycledCluster, o3);
            Assert.Equal(2, s3.ActiveFocusIndex);
            Assert.Equal("Cara", s3.FocusedMember.DisplayName);

            TapOutcome o4 = PinClusterResolver.ResolveTap(s3, 10, pins, 0.06f, out ClusterSelection s4);
            Assert.Equal(TapOutcome.Dismissed, o4);
            Assert.Null(s4);
        }

        [Fact]
        public void RefreshActiveCluster_DropsRemovedPlayerAndDismissesWhenEmpty()
        {
            var pins = new List<PinSample> { MakePin(10, 0f, 0f, "Alice") };
            PinClusterResolver.ResolveTap(null, 10, pins, 0.06f, out ClusterSelection s1);

            ClusterSelection afterMove = PinClusterResolver.RefreshActiveCluster(
                s1,
                new List<PinSample> { MakePin(10, 0.1f, 0.1f, "Alice") },
                0.06f);
            Assert.NotNull(afterMove);
            Assert.Equal(0.1f, afterMove.FocusedMember.SurfaceWorldPos.X, 4);

            ClusterSelection afterDisconnect = PinClusterResolver.RefreshActiveCluster(
                afterMove,
                new List<PinSample>(),
                0.06f);
            Assert.Null(afterDisconnect);
        }
    }
}
