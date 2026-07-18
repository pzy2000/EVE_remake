using System;

namespace Starfall.Domain
{
    /// <summary>
    /// Double-precision simulation position on the gameplay X/Z plane.
    /// It intentionally has no dependency on UnityEngine.Vector types.
    /// </summary>
    public readonly struct SimVec2 : IEquatable<SimVec2>
    {
        public static readonly SimVec2 Zero = new SimVec2(0d, 0d);

        public SimVec2(double x, double z)
        {
            X = x;
            Z = z;
        }

        public double X { get; }
        public double Z { get; }

        public double SqrMagnitude => X * X + Z * Z;
        public double Magnitude => Math.Sqrt(SqrMagnitude);

        public static double Distance(in SimVec2 a, in SimVec2 b)
        {
            var dx = a.X - b.X;
            var dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        public static SimVec2 operator +(SimVec2 a, SimVec2 b) => new SimVec2(a.X + b.X, a.Z + b.Z);
        public static SimVec2 operator -(SimVec2 a, SimVec2 b) => new SimVec2(a.X - b.X, a.Z - b.Z);
        public static SimVec2 operator *(SimVec2 value, double scalar) => new SimVec2(value.X * scalar, value.Z * scalar);

        public bool Equals(SimVec2 other) => X.Equals(other.X) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is SimVec2 other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Z.GetHashCode());
        public override string ToString() => $"({X:R}, {Z:R})";
    }
}
