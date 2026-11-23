using System.Collections.Generic;
using MapPinInspector.Core;
using Xunit;

namespace MapPinInspector.Tests
{
    public class PlayerPinMatcherTests
    {
        [Fact]
        public void MatchAll_PrefersExactLandmarkIndex()
        {
            var pins = new List<PinCorrelationInput>
            {
                new PinCorrelationInput(500, new Vec2(10f, 20f), true)
            };
            var players = new List<PlayerCandidate>
            {
                new PlayerCandidate(1, "Decoy", new Vec2(10f, 20f), true, true, false, false, false, 999),
                new PlayerCandidate(2, "ExactMatch", new Vec2(50f, 50f), true, true, false, false, false, 500)
            };

            Dictionary<uint, MatchedPinResult> results = PlayerPinMatcher.MatchAll(pins, players);
            Assert.True(results[500].Matched);
            Assert.True(results[500].IsHighConfidence);
            Assert.Equal("ExactMatch", results[500].Player.DisplayName);
        }

        [Fact]
        public void MatchAll_CorrelatesRemotePinsByFriendParityAndClosestDistance()
        {
            var pins = new List<PinCorrelationInput>
            {
                new PinCorrelationInput(100, new Vec2(100f, 100f), isFriendIcon: true),
                new PinCorrelationInput(101, new Vec2(101f, 100f), isFriendIcon: false)
            };
            var players = new List<PlayerCandidate>
            {
                new PlayerCandidate(10, "RedPlayer", new Vec2(100.5f, 100f), true, isFriend: false, isLocalPlayer: false, false, false, 0),
                new PlayerCandidate(11, "GreenFriend", new Vec2(101.2f, 100f), true, isFriend: true, isLocalPlayer: false, false, false, 0)
            };

            Dictionary<uint, MatchedPinResult> results = PlayerPinMatcher.MatchAll(pins, players);
            Assert.True(results[100].Matched);
            Assert.Equal("GreenFriend", results[100].Player.DisplayName);
            Assert.True(results[101].Matched);
            Assert.Equal("RedPlayer", results[101].Player.DisplayName);
        }

        [Fact]
        public void MatchAll_ResolvesOutOfChunkRemotePlayerViaFriendBucketElimination()
        {
            // Local player ("thethinker") is in loaded chunks and matches Pin 1001 spatially;
            // Remote player ("Robin") is across the map with unloaded PlayerController (HasWorldXZ = false).
            var pins = new List<PinCorrelationInput>
            {
                new PinCorrelationInput(1001, new Vec2(10f, 10f), isFriendIcon: false),
                new PinCorrelationInput(1002, new Vec2(450f, -320f), isFriendIcon: false)
            };
            var players = new List<PlayerCandidate>
            {
                new PlayerCandidate(101, "thethinker", new Vec2(10.4f, 9.8f), hasWorldXZ: true, isFriend: false, isLocalPlayer: true, false, false, 0, networkEntityId: 50),
                new PlayerCandidate(102, "Robin", Vec2.Zero, hasWorldXZ: false, isFriend: false, isLocalPlayer: false, false, false, 0, networkEntityId: 80)
            };

            Dictionary<uint, MatchedPinResult> results = PlayerPinMatcher.MatchAll(pins, players);
            Assert.True(results[1001].Matched);
            Assert.Equal("thethinker", results[1001].Player.DisplayName);
            Assert.True(results[1002].Matched);
            Assert.Equal("Robin", results[1002].Player.DisplayName);
        }

        [Fact]
        public void MatchAll_PreservesSessionBindingAfterPlayerWalksOutOfChunkRange()
        {
            var pins = new List<PinCorrelationInput>
            {
                new PinCorrelationInput(2001, new Vec2(400f, 400f), isFriendIcon: true),
                new PinCorrelationInput(2002, new Vec2(-400f, -400f), isFriendIcon: true)
            };
            // Reverse join order vs marker order, resolved via previously captured spatial binding.
            var players = new List<PlayerCandidate>
            {
                new PlayerCandidate(1, "Alice", Vec2.Zero, hasWorldXZ: false, isFriend: true, isLocalPlayer: false, false, false, 0, networkEntityId: 10),
                new PlayerCandidate(2, "Bob", Vec2.Zero, hasWorldXZ: false, isFriend: true, isLocalPlayer: false, false, false, 0, networkEntityId: 20)
            };
            var knownBindings = new Dictionary<uint, int>
            {
                { 2001, 2 },
                { 2002, 1 }
            };

            Dictionary<uint, MatchedPinResult> results = PlayerPinMatcher.MatchAll(pins, players, 35f, knownBindings);
            Assert.Equal("Bob", results[2001].Player.DisplayName);
            Assert.Equal("Alice", results[2002].Player.DisplayName);
        }

        [Fact]
        public void MatchAll_PairsMultipleOutOfChunkPlayersByFriendBucketAndJoinOrder()
        {
            var pins = new List<PinCorrelationInput>
            {
                new PinCorrelationInput(300, new Vec2(500f, 500f), isFriendIcon: false),
                new PinCorrelationInput(301, new Vec2(600f, 600f), isFriendIcon: true),
                new PinCorrelationInput(302, new Vec2(700f, 700f), isFriendIcon: false)
            };
            var players = new List<PlayerCandidate>
            {
                new PlayerCandidate(10, "LocalUser", new Vec2(0f, 0f), hasWorldXZ: true, isFriend: false, isLocalPlayer: true, false, false, 0, networkEntityId: 1),
                new PlayerCandidate(20, "RedFirst", Vec2.Zero, hasWorldXZ: false, isFriend: false, isLocalPlayer: false, false, false, 0, networkEntityId: 40),
                new PlayerCandidate(30, "GreenFriend", Vec2.Zero, hasWorldXZ: false, isFriend: true, isLocalPlayer: false, false, false, 0, networkEntityId: 30),
                new PlayerCandidate(40, "RedSecond", Vec2.Zero, hasWorldXZ: false, isFriend: false, isLocalPlayer: false, false, false, 0, networkEntityId: 50)
            };

            Dictionary<uint, MatchedPinResult> results = PlayerPinMatcher.MatchAll(pins, players);
            Assert.Equal("RedFirst", results[300].Player.DisplayName);
            Assert.Equal("GreenFriend", results[301].Player.DisplayName);
            Assert.Equal("RedSecond", results[302].Player.DisplayName);
        }

        [Fact]
        public void MatchAll_ReturnsUnmatchedWhenNoRemotePlayersExist()
        {
            var pins = new List<PinCorrelationInput>
            {
                new PinCorrelationInput(200, new Vec2(500f, 500f), false)
            };
            var players = new List<PlayerCandidate>
            {
                new PlayerCandidate(1, "OnlyLocal", new Vec2(0f, 0f), true, false, isLocalPlayer: true, false, false, 0)
            };

            Dictionary<uint, MatchedPinResult> results = PlayerPinMatcher.MatchAll(pins, players);
            Assert.False(results[200].Matched);
        }
    }
}
