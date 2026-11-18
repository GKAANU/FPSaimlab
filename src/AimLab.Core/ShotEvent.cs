namespace AimLab.Core;

/// <summary>
/// One resolved shot in the simulation. <see cref="TargetId"/> is null on a miss.
/// <see cref="TargetLife"/> increments every time a target respawns, so (id, life) identifies one target instance.
/// </summary>
public sealed record ShotEvent(
    double Time,
    long Tick,
    ViewAngles View,
    int? TargetId,
    int TargetLife,
    bool Headshot,
    bool Killed,
    double? TargetFirstVisibleAt,
    bool FirstHitOnTargetLife)
{
    public bool IsHit => TargetId is not null;
}
