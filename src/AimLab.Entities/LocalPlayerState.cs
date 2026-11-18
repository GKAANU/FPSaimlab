using System.Numerics;
using AimLab.Core;

namespace AimLab.Entities;

/// <summary>Latest known state of the local (camera) player.</summary>
public sealed class LocalPlayerState
{
    public PlayerEntity? Entity { get; private set; }

    public bool IsValid => Entity is not null;

    public bool IsAlive => Entity?.IsAlive ?? false;

    public Team Team => Entity?.Team ?? Team.None;

    public Vector3 EyePosition => Entity?.HeadPosition ?? Vector3.Zero;

    public ViewAngles ViewAngles => Entity?.ViewAngles ?? ViewAngles.Zero;

    public void Update(PlayerEntity? entity) => Entity = entity;
}
