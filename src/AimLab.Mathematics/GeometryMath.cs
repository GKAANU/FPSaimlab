using System.Numerics;
using AimLab.Core;

namespace AimLab.Mathematics;

public static class GeometryMath
{
    /// <summary>Nearest non-negative distance along a unit ray to a sphere, or false when the ray misses.</summary>
    public static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float distance)
    {
        var oc = origin - center;
        var b = Vector3.Dot(oc, direction);
        var c = Vector3.Dot(oc, oc) - radius * radius;
        var disc = b * b - c;
        distance = 0;
        if (disc < 0)
        {
            return false;
        }

        var sq = MathF.Sqrt(disc);
        var t = -b - sq;
        if (t < 0)
        {
            t = -b + sq;
        }

        if (t < 0)
        {
            return false;
        }

        distance = t;
        return true;
    }
}

/// <summary>Circular-sector outline of a horizontal field of view, for top-down drawing.</summary>
public static class FovSector
{
    /// <summary>
    /// World-XY offsets (from the viewer) of the sector's first edge, centre of the arc and second edge.
    /// <paramref name="fovDegrees"/> is clamped to [1, 180]; the arc runs counter-clockwise from start to end.
    /// Draw it as two arcs (start→mid, mid→end) so a 180° sector stays unambiguous.
    /// </summary>
    public static (Vector2 Start, Vector2 Mid, Vector2 End) Compute(float yawDegrees, float fovDegrees, float radius)
    {
        var fov = Math.Clamp(float.IsFinite(fovDegrees) ? fovDegrees : 1f, 1f, 180f);
        return (Point(yawDegrees - fov / 2, radius), Point(yawDegrees, radius), Point(yawDegrees + fov / 2, radius));
    }

    private static Vector2 Point(float degrees, float radius)
    {
        var r = degrees * MathF.PI / 180f;
        return new Vector2(MathF.Cos(r), MathF.Sin(r)) * radius;
    }
}

/// <summary>Converts raw mouse counts to a camera rotation.</summary>
public static class MouseLook
{
    /// <summary>
    /// Mouse right (positive X) turns right (yaw decreases); mouse down (positive Y) looks down (pitch increases).
    /// Degrees-per-count values are user-configurable simulation parameters.
    /// </summary>
    public static ViewAngles ToViewDelta(
        double countsX, double countsY, float sensitivity, float yawDegreesPerCount, float pitchDegreesPerCount, bool invertY)
    {
        var yaw = -countsX * sensitivity * yawDegreesPerCount;
        var pitch = countsY * sensitivity * pitchDegreesPerCount * (invertY ? -1 : 1);
        return new ViewAngles((float)pitch, (float)yaw);
    }
}
