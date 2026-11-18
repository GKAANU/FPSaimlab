using AimLab.Core;

namespace AimLab.Simulation;

/// <summary>
/// Simulation parameters. Distances are simulation units; none of these values is taken from or verified
/// against Counter-Strike 2.
/// </summary>
public sealed class SimulationOptions
{
    public int Seed { get; set; } = 1337;
    public TrainingMode Mode { get; set; } = TrainingMode.Arena;

    /// <summary>Training modes only; the arena always uses walls.</summary>
    public VisibilityScenario Visibility { get; set; } = VisibilityScenario.AlwaysVisible;

    public int TickRate { get; set; } = 64;
    public float ArenaHalfSize { get; set; } = 1500f;
    public float EyeHeight { get; set; } = 64f;
    public float BodyHeight { get; set; } = 38f;
    public float HeadRadius { get; set; } = 7f;
    public float BodyRadius { get; set; } = 14f;
    public double FireIntervalSeconds { get; set; } = 0.1;

    // Arena
    public int BotsPerTeam { get; set; } = 5;
    public float BotSpeed { get; set; } = 180f;
    public double RespawnDelaySeconds { get; set; } = 3.0;

    /// <summary>Expected hits per bot per second from simulated cross-fire, so bots die and respawn.</summary>
    public float DamageEventsPerSecond { get; set; } = 0.08f;

    // Training
    public float TargetSpeed { get; set; }
    public float MinDistance { get; set; } = 400f;
    public float MaxDistance { get; set; } = 1200f;
    public int TargetCount { get; set; } = 3;
    public float SpawnYawSpread { get; set; } = 45f;
    public float SpawnPitchSpread { get; set; } = 8f;
    public double PeekVisibleSeconds { get; set; } = 0.8;
    public double PeekHiddenSeconds { get; set; } = 1.2;
    public double TrainingRespawnDelaySeconds { get; set; } = 0.25;

    /// <summary>Number of simultaneous targets for the selected mode.</summary>
    public int EffectiveTargetCount => Mode == TrainingMode.TargetSwitch ? Math.Clamp(TargetCount, 1, 8) : 1;
}
