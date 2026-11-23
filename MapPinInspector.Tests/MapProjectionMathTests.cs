using MapPinInspector.Core;
using Xunit;

namespace MapPinInspector.Tests
{
    public class MapProjectionMathTests
    {
        [Fact]
        public void MapPositionToUv_AppliesCenterAndOffset()
        {
            Vec2 uv = MapProjectionMath.MapPositionToUv(new Vec2(0.15f, -0.25f), new Vec2(0.05f, 0.10f));
            Assert.Equal(0.70f, uv.X, 4);
            Assert.Equal(0.35f, uv.Y, 4);
        }

        [Fact]
        public void IsUvInsideBoard_RespectsMargin()
        {
            Assert.True(MapProjectionMath.IsUvInsideBoard(new Vec2(0.5f, 0.5f), 0.02f));
            Assert.True(MapProjectionMath.IsUvInsideBoard(new Vec2(0.02f, 0.98f), 0.02f));
            Assert.False(MapProjectionMath.IsUvInsideBoard(new Vec2(0.01f, 0.5f), 0.02f));
            Assert.False(MapProjectionMath.IsUvInsideBoard(new Vec2(0.5f, 1.01f), 0.02f));
        }

        [Fact]
        public void TryProjectUvToMeshLocal_MapsQuadWithExactBarycentrics()
        {
            var uvs = new[]
            {
                new Vec2(0f, 0f),
                new Vec2(1f, 0f),
                new Vec2(1f, 1f),
                new Vec2(0f, 1f)
            };
            var verts = new[]
            {
                new Vec3(-0.5f, -0.4f, 0.02f),
                new Vec3(0.5f, -0.4f, 0.02f),
                new Vec3(0.5f, 0.4f, 0.02f),
                new Vec3(-0.5f, 0.4f, 0.02f)
            };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };

            bool ok = MapProjectionMath.TryProjectUvToMeshLocal(
                new Vec2(0.75f, 0.25f),
                uvs,
                verts,
                tris,
                out Vec3 localPoint,
                out Vec3 localNormal);

            Assert.True(ok);
            Assert.Equal(0.25f, localPoint.X, 4);
            Assert.Equal(-0.20f, localPoint.Y, 4);
            Assert.Equal(0.02f, localPoint.Z, 4);
            Assert.Equal(0f, localNormal.X, 4);
            Assert.Equal(0f, localNormal.Y, 4);
            Assert.Equal(1f, System.Math.Abs(localNormal.Z), 4);
        }

        [Fact]
        public void ComputeBillboardCanvasForward_ClampsExcessiveVerticalPitch()
        {
            Vec3 cardPos = new Vec3(0f, 5f, 0f);
            Vec3 camPos = new Vec3(0f, 0f, -1f);
            Vec3 forward = MapProjectionMath.ComputeBillboardCanvasForward(cardPos, camPos, 30f);

            Assert.Equal(1f, forward.Length, 4);
            // tan(30 deg) ~= 0.57735, so normalized Y <= sin(30 deg) = 0.5
            Assert.True(forward.Y <= 0.501f);
            Assert.True(forward.Z > 0f);
        }
    }
}
