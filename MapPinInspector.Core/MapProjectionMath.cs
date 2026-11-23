using System;

namespace MapPinInspector.Core
{
    /// <summary>
    /// Pure coordinate projection and VR billboarding math for ATT Map Boards.
    /// </summary>
    public static class MapProjectionMath
    {
        /// <summary>
        /// Converts MapBoard.WorldToMapPosition output and MapBoard.MapOffsetScale (x, y)
        /// into [0, 1]^2 RenderTexture UV space matching MapBoardDirectRenderer.RenderMap.
        /// </summary>
        public static Vec2 MapPositionToUv(Vec2 mapPosition, Vec2 mapOffset)
        {
            return new Vec2(0.5f + mapPosition.X + mapOffset.X, 0.5f + mapPosition.Y + mapOffset.Y);
        }

        /// <summary>
        /// Returns true when the marker UV falls inside the visible board surface within the given margin.
        /// </summary>
        public static bool IsUvInsideBoard(Vec2 uv, float edgeMargin = 0.01f)
        {
            float clampedMargin = edgeMargin < 0f ? 0f : (edgeMargin > 0.45f ? 0.45f : edgeMargin);
            return uv.X >= clampedMargin &&
                   uv.X <= 1f - clampedMargin &&
                   uv.Y >= clampedMargin &&
                   uv.Y <= 1f - clampedMargin;
        }

        /// <summary>
        /// Computes barycentric coordinates (w0, w1, w2) of point p inside 2D triangle (a, b, c).
        /// Returns false if the triangle is degenerate in UV space.
        /// </summary>
        public static bool TryComputeBarycentric(
            Vec2 p,
            Vec2 a,
            Vec2 b,
            Vec2 c,
            out float w0,
            out float w1,
            out float w2)
        {
            float v0x = b.X - a.X;
            float v0y = b.Y - a.Y;
            float v1x = c.X - a.X;
            float v1y = c.Y - a.Y;
            float denom = v0x * v1y - v1x * v0y;

            if (Math.Abs(denom) <= 1e-8f)
            {
                w0 = w1 = w2 = 0f;
                return false;
            }

            float v2x = p.X - a.X;
            float v2y = p.Y - a.Y;
            float invDenom = 1f / denom;

            w1 = (v2x * v1y - v1x * v2y) * invDenom;
            w2 = (v0x * v2y - v2x * v0y) * invDenom;
            w0 = 1f - w1 - w2;
            return true;
        }

        /// <summary>
        /// Projects a UV coordinate onto a mesh's local 3D surface using exact triangle barycentrics.
        /// Selects the triangle containing the UV, or the closest non-degenerate triangle if slightly outside.
        /// </summary>
        public static bool TryProjectUvToMeshLocal(
            Vec2 uv,
            Vec2[] meshUvs,
            Vec3[] meshVertices,
            int[] triangles,
            out Vec3 localPoint,
            out Vec3 localNormal)
        {
            localPoint = Vec3.Zero;
            localNormal = Vec3.Forward;

            if (meshUvs == null || meshVertices == null || triangles == null)
                return false;
            if (meshUvs.Length != meshVertices.Length || triangles.Length < 3)
                return false;

            float bestError = float.MaxValue;
            bool found = false;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                if ((uint)i0 >= (uint)meshVertices.Length ||
                    (uint)i1 >= (uint)meshVertices.Length ||
                    (uint)i2 >= (uint)meshVertices.Length)
                {
                    continue;
                }

                if (!TryComputeBarycentric(
                        uv,
                        meshUvs[i0],
                        meshUvs[i1],
                        meshUvs[i2],
                        out float w0,
                        out float w1,
                        out float w2))
                {
                    continue;
                }

                Vec3 p0 = meshVertices[i0];
                Vec3 p1 = meshVertices[i1];
                Vec3 p2 = meshVertices[i2];
                Vec3 triNormal = Vec3.Cross(p1 - p0, p2 - p0).Normalized();
                if (triNormal.LengthSquared <= 1e-8f)
                    continue;

                float error = 0f;
                if (w0 < 0f) error += -w0;
                if (w1 < 0f) error += -w1;
                if (w2 < 0f) error += -w2;
                if (w0 > 1f) error += w0 - 1f;
                if (w1 > 1f) error += w1 - 1f;
                if (w2 > 1f) error += w2 - 1f;

                if (!found || error < bestError)
                {
                    found = true;
                    bestError = error;
                    localPoint = p0 * w0 + p1 * w1 + p2 * w2;
                    localNormal = triNormal;

                    if (error <= 1e-5f)
                        break;
                }
            }

            return found;
        }

        /// <summary>
        /// Computes the canvas +Z forward vector so a Unity WorldSpace Canvas (whose front faces -Z)
        /// faces toward the player's VR camera with clamped vertical pitch for upright readability.
        /// </summary>
        public static Vec3 ComputeBillboardCanvasForward(Vec3 cardPos, Vec3 cameraPos, float maxPitchDegrees = 35f)
        {
            Vec3 toCard = cardPos - cameraPos;
            float horizSq = toCard.X * toCard.X + toCard.Z * toCard.Z;

            if (horizSq <= 1e-8f)
                return Vec3.Forward;

            float horizLen = (float)Math.Sqrt(horizSq);
            float clampedMaxPitch = maxPitchDegrees < 0f ? 0f : (maxPitchDegrees > 80f ? 80f : maxPitchDegrees);
            float maxY = horizLen * (float)Math.Tan(clampedMaxPitch * (Math.PI / 180.0));
            float clampedY = toCard.Y;
            if (clampedY > maxY) clampedY = maxY;
            else if (clampedY < -maxY) clampedY = -maxY;

            return new Vec3(toCard.X, clampedY, toCard.Z).Normalized();
        }
    }
}
