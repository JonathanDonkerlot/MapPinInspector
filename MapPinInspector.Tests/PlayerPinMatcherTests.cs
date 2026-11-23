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
        public void MatchAll_ReturnsUnmatchedWhenPlayersOutOfRangeOrNoPosition()
        {
            var pins = new List<PinCorrelationInput>
            {
                new PinCorrelationInput(200, new Vec2(0f, 0f), false)
            };
            var players = new List<PlayerCandidate>
            {
                new PlayerCandidate(1, "FarAway", new Vec2(500f, 500f), true, false, false, false, false, 0),
                new PlayerCandidate(2, "NoPos", Vec2.Zero, false, false, false, false, false, 0)
            };

            Dictionary<uint, MatchedPinResult> results = PlayerPinMatcher.MatchAll(pins, players);
            Assert.False(results[200].Matched);
        }
    }
}
