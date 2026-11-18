namespace AimLab.Core;

/// <summary>Camera orientation in degrees. Pitch is positive when looking down; yaw is measured from +X toward +Y.</summary>
public readonly record struct ViewAngles(float Pitch, float Yaw)
{
    public static ViewAngles Zero => default;

    public override string ToString() => $"({Pitch:F2}, {Yaw:F2})";
}
