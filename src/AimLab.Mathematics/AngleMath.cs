using System.Numerics;
using AimLab.Core;

namespace AimLab.Mathematics;

/// <summary>Angle math for a Z-up world with Source-style pitch (positive = down).</summary>
public static class AngleMath
{
    public const float MaxPitch = 89f;
    private const float RadToDeg = 180f / MathF.PI;
    private const float DegToRad = MathF.PI / 180f;

    /// <summary>Wraps yaw into the range (-180, 180].</summary>
    public static float NormalizeYaw(float yaw)
    {
        if (!float.IsFinite(yaw))
        {
            return 0f;
        }

        yaw %= 360f;
        if (yaw > 180f)
        {
            yaw -= 360f;
        }
        else if (yaw <= -180f)
        {
            yaw += 360f;
        }

        return yaw;
    }

    public static float ClampPitch(float pitch) =>
        float.IsFinite(pitch) ? Math.Clamp(pitch, -MaxPitch, MaxPitch) : 0f;

    public static ViewAngles Normalize(ViewAngles angles) =>
        new(ClampPitch(angles.Pitch), NormalizeYaw(angles.Yaw));

    /// <summary>Angles that point a camera at <paramref name="from"/> toward <paramref name="to"/>.</summary>
    public static ViewAngles CalculateAngles(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var horizontal = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        var pitch = -MathF.Atan2(d.Z, horizontal) * RadToDeg;
        var yaw = MathF.Atan2(d.Y, d.X) * RadToDeg;
        return Normalize(new ViewAngles(pitch, yaw));
    }

    /// <summary>Shortest signed rotation from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static ViewAngles Delta(ViewAngles from, ViewAngles to) =>
        new(to.Pitch - from.Pitch, NormalizeYaw(to.Yaw - from.Yaw));

    /// <summary>Euclidean angular distance in degrees using the shortest yaw rotation.</summary>
    public static float AngularDistance(ViewAngles a, ViewAngles b)
    {
        var d = Delta(a, b);
        return MathF.Sqrt(d.Pitch * d.Pitch + d.Yaw * d.Yaw);
    }

    /// <summary>
    /// Moves <paramref name="current"/> a 1/<paramref name="smoothing"/> fraction toward <paramref name="target"/>.
    /// A smoothing of 1 or less snaps to the target.
    /// </summary>
    public static ViewAngles Smooth(ViewAngles current, ViewAngles target, float smoothing)
    {
        if (!(smoothing > 1f))
        {
            return Normalize(target);
        }

        var d = Delta(current, target);
        return Normalize(new ViewAngles(current.Pitch + d.Pitch / smoothing, current.Yaw + d.Yaw / smoothing));
    }

    /// <summary>Unit forward vector for the given view angles.</summary>
    public static Vector3 Forward(ViewAngles angles)
    {
        var pitch = angles.Pitch * DegToRad;
        var yaw = angles.Yaw * DegToRad;
        var cosPitch = MathF.Cos(pitch);
        return new Vector3(cosPitch * MathF.Cos(yaw), cosPitch * MathF.Sin(yaw), -MathF.Sin(pitch));
    }
}
