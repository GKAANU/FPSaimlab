using System.Numerics;
using AimLab.Core;

namespace AimLab.Entities;

/// <summary>Immutable snapshot of one player parsed from simulated memory.</summary>
public sealed record PlayerEntity(
    int Id,
    int Slot,
    Team Team,
    int Health,
    bool IsAlive,
    bool IsVisible,
    Vector3 Origin,
    Vector3 HeadPosition,
    Vector3 BodyPosition,
    ViewAngles ViewAngles)
{
    public Vector3 GetAimPoint(BodyRegion region) => region == BodyRegion.Head ? HeadPosition : BodyPosition;

    public float DistanceTo(PlayerEntity other) => Vector3.Distance(Origin, other.Origin);

    public float DistanceTo(Vector3 point) => Vector3.Distance(Origin, point);
}

/// <summary>Result of reading every entity slot once.</summary>
public sealed record EntityReadResult(
    bool HeaderValid,
    long Tick,
    double Time,
    PlayerEntity? LocalPlayer,
    IReadOnlyList<PlayerEntity> Players,
    int InvalidCount)
{
    public static EntityReadResult Invalid { get; } = new(false, 0, 0, null, [], 0);
}
