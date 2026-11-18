namespace AimLab.Simulation;

public sealed class SimulationOptions
{
    public int Seed { get; set; } = 1337;
    public int BotsPerTeam { get; set; } = 5;
    public float ArenaHalfSize { get; set; } = 1500f;
    public float BotSpeed { get; set; } = 180f;
    public double RespawnDelaySeconds { get; set; } = 3.0;

    /// <summary>Expected hits per bot per second from simulated cross-fire, so bots die and respawn.</summary>
    public float DamageEventsPerSecond { get; set; } = 0.08f;

    public int TickRate { get; set; } = 64;
}
