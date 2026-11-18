using System.Numerics;

namespace AimLab.Mathematics;

/// <summary>Axis-aligned box used for walls and line-of-sight tests.</summary>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;

    /// <summary>True when the segment from <paramref name="a"/> to <paramref name="b"/> touches the box (slab method).</summary>
    public bool IntersectsSegment(Vector3 a, Vector3 b)
    {
        var dir = b - a;
        float tMin = 0f, tMax = 1f;
        return Slab(a.X, dir.X, Min.X, Max.X, ref tMin, ref tMax)
            && Slab(a.Y, dir.Y, Min.Y, Max.Y, ref tMin, ref tMax)
            && Slab(a.Z, dir.Z, Min.Z, Max.Z, ref tMin, ref tMax);
    }

    private static bool Slab(float origin, float dir, float min, float max, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(dir) < 1e-6f)
        {
            return origin >= min && origin <= max;
        }

        var t1 = (min - origin) / dir;
        var t2 = (max - origin) / dir;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        tMin = MathF.Max(tMin, t1);
        tMax = MathF.Min(tMax, t2);
        return tMin <= tMax;
    }
}
