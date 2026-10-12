using System;

namespace PVDesigner.Core.Math
{
    /// <summary>
    /// Engine independent 3D vector. Axis convention matches Unity:
    /// X = East, Y = Up, Z = North (metres).
    /// </summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public double X;
        public double Y;
        public double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 Up = new Vec3(0, 1, 0);
        public static readonly Vec3 East = new Vec3(1, 0, 0);
        public static readonly Vec3 North = new Vec3(0, 0, 1);

        public double Length => System.Math.Sqrt(X * X + Y * Y + Z * Z);
        public double LengthSquared => X * X + Y * Y + Z * Z;

        public Vec3 Normalized
        {
            get
            {
                double l = Length;
                return l > 1e-12 ? new Vec3(X / l, Y / l, Z / l) : Zero;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, double s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        /// <summary>Unit vector pointing to a direction given by compass azimuth (0=N, 90=E) and elevation, both in degrees.</summary>
        public static Vec3 FromAzimuthElevation(double azimuthDeg, double elevationDeg)
        {
            double az = azimuthDeg * MathUtil.Deg2Rad;
            double el = elevationDeg * MathUtil.Deg2Rad;
            double c = System.Math.Cos(el);
            return new Vec3(System.Math.Sin(az) * c, System.Math.Sin(el), System.Math.Cos(az) * c);
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>Horizontal 2D point. X = East, Y = North (metres).</summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public double X;
        public double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double Length => System.Math.Sqrt(X * X + Y * Y);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);

        public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

        /// <summary>Rotates the point counter-clockwise (seen from above) by the given angle.</summary>
        public Vec2 Rotate(double angleRad)
        {
            double c = System.Math.Cos(angleRad), s = System.Math.Sin(angleRad);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }
}
