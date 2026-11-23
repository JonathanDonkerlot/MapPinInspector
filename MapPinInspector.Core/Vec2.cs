using System;

namespace MapPinInspector.Core
{
    /// <summary>
    /// Lightweight immutable 2D vector for engine-agnostic coordinate math and unit tests.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(LengthSquared);

        public static float Distance(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static float DistanceSquared(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:F3}, {Y:F3})";
    }
}
