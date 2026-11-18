using AimLab.Configuration;
using AimLab.Simulation;

namespace AimLab.Runtime;

public static class ScenarioFactory
{
    /// <summary>Maps UI training settings to simulation options. Unlisted options keep their simulation defaults.</summary>
    public static SimulationOptions Create(TrainingSettings t) => new()
    {
        Mode = t.Mode,
        Seed = t.Seed,
        Visibility = t.Visibility,
        TargetSpeed = t.TargetSpeed,
        HeadRadius = t.HeadRadius,
        BodyRadius = t.BodyRadius,
        MinDistance = Math.Min(t.MinDistance, t.MaxDistance),
        MaxDistance = Math.Max(t.MinDistance, t.MaxDistance),
        TargetCount = t.TargetCount,
        SpawnYawSpread = t.SpawnYawSpread,
        SpawnPitchSpread = t.SpawnPitchSpread,
        PeekVisibleSeconds = t.PeekVisibleSeconds,
        PeekHiddenSeconds = t.PeekHiddenSeconds,
        FireIntervalSeconds = t.FireIntervalSeconds,
        TrainingRespawnDelaySeconds = t.RespawnDelaySeconds,
    };
}
