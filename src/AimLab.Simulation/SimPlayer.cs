using System.Numerics;
using AimLab.Core;

namespace AimLab.Simulation;

/// <summary>Mutable player state owned by the simulation.</summary>
public sealed class SimPlayer(int id, Team team)
{
    public const float EyeHeight = 64f;
    public const float BodyHeight = 38f;

    public int Id { get; } = id;
    public Team Team { get; } = team;
    public int Health { get; set; } = 100;
    public Vector3 Origin { get; set; }
    public ViewAngles View { get; set; }
    public Vector3 Waypoint { get; set; }
    public double? RespawnAt { get; set; }
    public bool IsVisibleToLocal { get; set; }

    public bool IsAlive => Health > 0;
    public Vector3 Head => Origin + new Vector3(0, 0, EyeHeight);
    public Vector3 Body => Origin + new Vector3(0, 0, BodyHeight);
}
