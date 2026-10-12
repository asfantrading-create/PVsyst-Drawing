using PVDesigner.Core.Math;

namespace PVDesigner.Core.Solar
{
    /// <summary>
    /// Horizontal single axis tracker kinematics with optional backtracking
    /// (same formulation as pvlib.tracking.singleaxis with zero cross-axis slope).
    /// </summary>
    public static class SingleAxisTracker
    {
        /// <summary>Horizontal unit vector along the rotation axis.</summary>
        public static Vec3 AxisDirection(double axisCompassAzimuthDeg) =>
            new Vec3(MathUtil.SinD(axisCompassAzimuthDeg), 0, MathUtil.CosD(axisCompassAzimuthDeg));

        /// <summary>Horizontal unit vector perpendicular to the axis (positive rotation tilts the normal towards it).</summary>
        public static Vec3 CrossAxisDirection(double axisCompassAzimuthDeg) =>
            new Vec3(MathUtil.CosD(axisCompassAzimuthDeg), 0, -MathUtil.SinD(axisCompassAzimuthDeg));

        /// <summary>
        /// Rotation angle in degrees for a given sun position. Returns 0 (stow) when the sun is down.
        /// </summary>
        public static double RotationAngle(SunPosition sun, double axisCompassAzimuthDeg, double maxAngleDeg, bool backtracking, double gcr)
        {
            if (!sun.IsUp) return 0;
            Vec3 s = sun.Direction;
            Vec3 p = CrossAxisDirection(axisCompassAzimuthDeg);
            double ideal = System.Math.Atan2(Vec3.Dot(s, p), s.Y) * MathUtil.Rad2Deg;
            double angle = ideal;
            if (backtracking && gcr > 0)
            {
                double temp = MathUtil.CosD(ideal) / gcr;
                if (temp < 1.0)
                {
                    double correction = -System.Math.Sign(ideal) * System.Math.Acos(MathUtil.Clamp(temp, -1, 1)) * MathUtil.Rad2Deg;
                    angle = ideal + correction;
                }
            }
            return MathUtil.Clamp(angle, -maxAngleDeg, maxAngleDeg);
        }

        public static Vec3 PlaneNormal(double rotationDeg, double axisCompassAzimuthDeg)
        {
            Vec3 p = CrossAxisDirection(axisCompassAzimuthDeg);
            return (Vec3.Up * MathUtil.CosD(rotationDeg) + p * MathUtil.SinD(rotationDeg)).Normalized;
        }
    }
}
