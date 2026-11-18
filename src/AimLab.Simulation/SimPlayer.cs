using System.Numerics;
using AimLab.Core;

namespace AimLab.Simulation;

/// <summary>Mutable player state owned by the simulation.</summary>
public sealed class SimPlayer(int id, Team team, float eyeHeight = 64f, float bodyHeight = 38f)
{
    public int Id { get; } = id;
    public Team Team { get; } = team;
    public float EyeHeight { get; } = eyeHeight;
    public float BodyHeight { get; } = bodyHeight;
    public int Health { get; set; } = 100;
    public Vector3 Origin { get; set; }
    public ViewAngles View { get; set; }
    public Vector3 Waypoint { get; set; }
    public double? RespawnAt { get; set; }
    public bool IsVisibleToLocal { get; set; }

    /// <summary>False while a peek-scenario target is hidden; hidden targets cannot be seen or hit.</summary>
    public bool IsExposed { get; set; } = true;

    /// <summary>Increments on every spawn; (Id, Life) identifies one target instance.</summary>
    public int Life { get; set; }

    public double SpawnTime { get; set; }

    /// <summary>First time in this life the target became visible to the local player.</summary>
    public double? FirstVisibleAt { get; set; }

    public bool HitThisLife { get; set; }

    internal Random? Motion { get; set; }
    internal float StrafeSign { get; set; } = 1f;
    internal double NextStrafeFlip { get; set; }
    internal double PeekOffset { get; set; }

    public bool IsAlive => Health > 0;
    public Vector3 Head => Origin + new Vector3(0, 0, EyeHeight);
    public Vector3 Body => Origin + new Vector3(0, 0, BodyHeight);
}
