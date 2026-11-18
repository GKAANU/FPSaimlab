using System.Numerics;
using AimLab.AimAssist;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Mathematics;
using AimLab.Runtime;
using AimLab.Simulation;
using AimLab.Targeting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AimLab.Tests;

public class TrainingTests
{
    private const double Dt = 1.0 / 64;

    private static (ServiceProvider Sp, AimLabEngine Engine) Build(AimSettings settings, SimulationOptions options, double duration = 0)
    {
        var sp = new ServiceCollection()
            .AddAimLab(new SimulationOptions(), settings, new LoggerConfiguration().CreateLogger())
            .BuildServiceProvider();
        var engine = sp.GetRequiredService<AimLabEngine>();
        engine.StartSession(options, duration);
        return (sp, engine);
    }

    private static SimulationOptions Flick(int seed = 5) => new() { Mode = TrainingMode.Flick, Seed = seed, TrainingRespawnDelaySeconds = 0 };

    /// <summary>Points the simulated camera at a target's head, as a perfect manual flick would.</summary>
    private static void LookAt(AimLabEngine engine, SimPlayer target)
    {
        var world = engine.Process.World;
        world.ApplyView(AngleMath.CalculateAngles(world.LocalPlayer.Head, target.Head));
    }

    private static SimPlayer FirstTarget(AimLabEngine e) => e.Process.World.Players.First(p => p.Team == Team.Attackers && p.IsAlive);

    [Fact]
    public void AimAssist_Disabled_StillReportsTargetAndErrors()
    {
        var local = TestData.Local(Vector3.Zero, default);
        var enemy = TestData.Player(2, Team.Attackers, new Vector3(500, 200, 0));
        var settings = new AimSettings { Enabled = false, Fov = 180 };
        var target = new TargetSelector().SelectTarget(local, [enemy], settings);

        var step = new AimAssistController().Update(local, target, settings, activationHeld: true);

        Assert.False(step.Applied);
        Assert.Equal(2, step.TargetId);
        Assert.NotNull(step.ErrorBefore);
        Assert.Equal(step.ErrorBefore, step.ErrorAfter);
        Assert.Equal(step.Before, step.After);
    }

    [Fact]
    public void ManualSession_RecordsTargetErrorAndAcquisition()
    {
        var settings = new AimSettings { Enabled = false, Fov = 180, VisibilityCheck = false };
        var (sp, engine) = Build(settings, Flick());
        using var _ = sp;

        engine.Tick(Dt);
        var target = FirstTarget(engine);
        var before = engine.Tick(Dt);
        Assert.Equal(target.Id, before.Aim.TargetId);
        Assert.True(before.Aim.ErrorAfter > 1f);

        // Manual input: rotate onto the target through the queued-turn path the UI uses.
        var aim = AngleMath.CalculateAngles(engine.Process.World.LocalPlayer.Head, target.Head);
        var delta = AngleMath.Delta(engine.Process.World.CurrentView, aim);
        engine.QueueTurn(delta.Pitch, delta.Yaw);
        var after = engine.Tick(Dt);

        Assert.False(after.Aim.Applied);
        Assert.True(after.Aim.ErrorAfter < 0.01f);

        var summary = engine.Telemetry.GetSummary();
        Assert.Equal(1, summary.TargetAcquisitions);
        Assert.Single(summary.AcquisitionTimes);
        Assert.Single(summary.ReactionTimes);
        Assert.Equal(1.0, summary.TrackingAccuracy);
        Assert.Equal(0, summary.AssistActiveShare);
    }

    [Fact]
    public void SmoothingOne_SnapsAndIsAcquiredInSameTick()
    {
        var settings = new AimSettings { Fov = 180, Smoothing = 1, RequireActivationKey = false, VisibilityCheck = false };
        var (sp, engine) = Build(settings, Flick());
        using var _ = sp;

        var frame = engine.Tick(Dt);

        Assert.True(frame.Aim.Applied);
        Assert.True(frame.Aim.ErrorBefore > 1f);
        Assert.True(frame.Aim.ErrorAfter < 1e-3f);
        var sample = engine.Telemetry.GetSamples().Single();
        Assert.Equal(frame.Aim.After, new ViewAngles(sample.Pitch, sample.Yaw));
        Assert.True(sample.ErrorAfter < 1e-3f);
        Assert.Equal([0.0], engine.Telemetry.GetSummary().AcquisitionTimes);
    }

    [Fact]
    public void Shooting_HitsWhenOnTargetAndMissesOtherwise()
    {
        var (sp, engine) = Build(new AimSettings { Enabled = false }, Flick());
        using var _ = sp;

        engine.Tick(Dt);
        var target = FirstTarget(engine);
        var life = target.Life;
        LookAt(engine, target);
        engine.QueueFire();
        var hit = engine.Tick(Dt);

        var shot = Assert.Single(hit.Shots);
        Assert.Equal(target.Id, shot.TargetId);
        Assert.True(shot.Headshot);
        Assert.True(shot.Killed);
        Assert.True(shot.FirstHitOnTargetLife);

        engine.Tick(Dt); // respawn delay 0: new instance spawns
        Assert.True(target.IsAlive);
        Assert.Equal(life + 1, target.Life);

        engine.Process.World.ApplyView(new ViewAngles(0, 170)); // facing away
        engine.Tick(Dt * 7); // pass the fire interval
        engine.QueueFire();
        var miss = engine.Tick(Dt);
        Assert.Null(Assert.Single(miss.Shots).TargetId);

        var stats = engine.Telemetry.GetSummary().Shots;
        Assert.Equal(2, stats.Shots);
        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Single(stats.TimesToFirstHit);
    }

    [Fact]
    public void FireInterval_DropsShotsThatComeTooFast()
    {
        var options = Flick();
        options.FireIntervalSeconds = 0.5;
        var (sp, engine) = Build(new AimSettings { Enabled = false }, options);
        using var _ = sp;

        for (var i = 0; i < 10; i++)
        {
            engine.QueueFire();
            engine.Tick(Dt);
        }

        Assert.Equal(1, engine.Telemetry.GetSummary().Shots.Shots);
    }

    [Fact]
    public void TargetSwitch_MeasuresHitToHitTime()
    {
        var options = new SimulationOptions { Mode = TrainingMode.TargetSwitch, TargetCount = 3, Seed = 11, FireIntervalSeconds = 0.02, TrainingRespawnDelaySeconds = 5 };
        var (sp, engine) = Build(new AimSettings { Enabled = false }, options);
        using var _ = sp;

        engine.Tick(Dt);
        // Nearest first: a nearer target on the same line of fire would otherwise take the shot (correct hitscan behaviour).
        var eye = engine.Process.World.LocalPlayer.Head;
        var targets = engine.Process.World.Players.Where(p => p.Team == Team.Attackers).OrderBy(p => Vector3.Distance(eye, p.Head)).ToList();
        Assert.Equal(3, targets.Count);

        foreach (var t in targets)
        {
            LookAt(engine, t);
            engine.QueueFire();
            engine.Tick(Dt);
            engine.Tick(Dt);
        }

        var shots = engine.Telemetry.GetSummary().Shots;
        Assert.Equal(3, shots.Hits);
        Assert.Equal(2, shots.SwitchTimes.Count);
        Assert.All(shots.SwitchTimes, s => Assert.Equal(2 * Dt, s, 6));
    }

    [Fact]
    public void Tracking_TargetMovesAndIsNeverEliminated()
    {
        var options = new SimulationOptions { Mode = TrainingMode.Tracking, TargetSpeed = 250, Seed = 3, FireIntervalSeconds = 0.02 };
        var (sp, engine) = Build(new AimSettings { Enabled = false }, options);
        using var _ = sp;

        var target = FirstTarget(engine);
        var start = target.Origin;
        var eye = engine.Process.World.LocalPlayer.Head;
        var startRadius = new Vector2(start.X - eye.X, start.Y - eye.Y).Length();

        for (var i = 0; i < 128; i++)
        {
            LookAt(engine, target);
            engine.QueueFire();
            engine.Tick(Dt);
        }

        Assert.True(target.IsAlive);
        Assert.NotEqual(start, target.Origin);
        Assert.Equal(startRadius, new Vector2(target.Origin.X - eye.X, target.Origin.Y - eye.Y).Length(), 1);
        Assert.Equal(0, engine.Telemetry.GetSummary().Shots.Kills);
        Assert.True(engine.Telemetry.GetSummary().Shots.Hits > 10);
    }

    [Fact]
    public void Spawns_RespectDistanceAndYawSpread()
    {
        var options = new SimulationOptions { Mode = TrainingMode.TargetSwitch, TargetCount = 8, MinDistance = 600, MaxDistance = 900, SpawnYawSpread = 20, SpawnPitchSpread = 0, Seed = 21 };
        var world = new GameWorld(options);
        var eye = world.LocalPlayer.Head;

        foreach (var p in world.Players.Where(p => p.Team == Team.Attackers))
        {
            var d = Vector3.Distance(eye, p.Head);
            Assert.InRange(d, 599.9f, 900.1f);
            Assert.InRange(MathF.Abs(AngleMath.CalculateAngles(eye, p.Head).Yaw), 0, 20.01f);
        }
    }

    [Fact]
    public void Peek_TargetsHideAndCannotBeHitWhileHidden()
    {
        var options = new SimulationOptions { Mode = TrainingMode.Flick, Visibility = VisibilityScenario.Peek, PeekVisibleSeconds = 0.5, PeekHiddenSeconds = 0.5, Seed = 8, FireIntervalSeconds = 0.02 };
        var (sp, engine) = Build(new AimSettings { Enabled = false }, options);
        using var _ = sp;

        var target = FirstTarget(engine);
        bool sawHidden = false, sawVisible = false;
        for (var i = 0; i < 128 && !sawHidden; i++)
        {
            var frame = engine.Tick(Dt);
            var entity = frame.Players.Single(p => p.Id == target.Id);
            sawVisible |= entity.IsVisible;
            sawHidden = !entity.IsVisible;
        }

        Assert.True(sawHidden);
        LookAt(engine, target);
        engine.QueueFire();
        var shot = Assert.Single(engine.Tick(Dt).Shots);
        Assert.True(sawVisible);
        Assert.False(target.IsExposed); // hidden phase lasts 32 ticks, so the next tick is still hidden
        Assert.Null(shot.TargetId);
    }

    [Fact]
    public void WallOcclusion_BlocksVisibilityAndShots()
    {
        var options = new SimulationOptions { Mode = TrainingMode.Flick, Visibility = VisibilityScenario.WallOcclusion, Seed = 1 };
        var world = new GameWorld(options);
        Assert.NotEmpty(world.Walls);
        Assert.Empty(new GameWorld(new SimulationOptions { Mode = TrainingMode.Flick }).Walls);

        // Put the target straight behind the first wall and shoot it.
        var target = world.Players.First(p => p.Team == Team.Attackers);
        var wall = world.Walls[0];
        var eye = world.LocalPlayer.Head;
        var behind = new Vector3(wall.Max.X + 300, (wall.Min.Y + wall.Max.Y) / 2, 0);
        target.Origin = behind;
        world.ApplyView(AngleMath.CalculateAngles(eye, target.Head));
        world.QueueFire();
        world.Step(Dt);

        Assert.False(target.IsVisibleToLocal);
        Assert.Null(Assert.Single(world.DrainShotEvents()).TargetId);
    }

    [Fact]
    public void SameSeedAndScenario_ReproducesSession()
    {
        static IReadOnlyList<AimLab.Telemetry.TelemetrySample> Run(int seed)
        {
            var settings = new AimSettings { Fov = 180, Smoothing = 6, RequireActivationKey = false, VisibilityCheck = false };
            var options = new SimulationOptions { Mode = TrainingMode.TargetSwitch, TargetCount = 4, TargetSpeed = 120, Seed = seed, FireIntervalSeconds = 0.1 };
            var (sp, engine) = Build(settings, options);
            using var _ = sp;
            for (var i = 0; i < 256; i++)
            {
                if (i % 16 == 0) engine.QueueFire();
                engine.Tick(Dt);
            }

            return engine.Telemetry.GetSamples();
        }

        var a = Run(77);
        var b = Run(77);
        var c = Run(78);
        Assert.Equal(a, b);
        Assert.NotEqual(a.Select(s => s.Yaw), c.Select(s => s.Yaw));
    }

    [Fact]
    public void TimedSession_FinishesWithReportAndFreezes()
    {
        var (sp, engine) = Build(new AimSettings { Enabled = false }, Flick(), duration: 1.0);
        using var _ = sp;
        Assert.Equal(SessionStatus.Running, engine.Session.Status);

        for (var i = 0; i < 100; i++)
        {
            engine.Tick(Dt);
        }

        Assert.Equal(SessionStatus.Finished, engine.Session.Status);
        var result = engine.LastResult;
        Assert.NotNull(result);
        Assert.Equal(64, result!.Snapshot.Summary.Session.SampleCount);
        Assert.Equal(64, engine.Telemetry.GetSummary().Session.SampleCount);
        Assert.Contains("Mode: Flick", result.Report);
        Assert.Equal(1.0, engine.Process.World.Time, 6);

        // Starting a new session resets world, cache and telemetry.
        engine.StartSession(Flick(), 0);
        Assert.Equal(SessionStatus.FreePlay, engine.Session.Status);
        Assert.Equal(0, engine.Telemetry.GetSummary().Session.SampleCount);
        Assert.Equal(0, engine.Process.World.Tick);
        Assert.Null(engine.LastResult);
        engine.Tick(Dt);
        Assert.Equal(1, engine.Telemetry.GetSummary().Session.SampleCount);
    }

    [Fact]
    public void StopSession_EndsEarly()
    {
        var (sp, engine) = Build(new AimSettings(), Flick(), duration: 60);
        using var _ = sp;
        engine.Tick(Dt);
        engine.StopSession();
        Assert.Equal(SessionStatus.Finished, engine.Session.Status);
        Assert.Equal(1, engine.LastResult!.Snapshot.Summary.Session.SampleCount);
    }

    [Fact]
    public void ScenarioFactory_MapsAndOrdersDistances()
    {
        var t = new TrainingSettings { Mode = TrainingMode.Tracking, Seed = 9, MinDistance = 1500, MaxDistance = 500, TargetSpeed = 99, HeadRadius = 10 };
        var o = ScenarioFactory.Create(t);
        Assert.Equal(TrainingMode.Tracking, o.Mode);
        Assert.Equal(9, o.Seed);
        Assert.Equal(500, o.MinDistance);
        Assert.Equal(1500, o.MaxDistance);
        Assert.Equal(99, o.TargetSpeed);
        Assert.Equal(10, o.HeadRadius);
    }

    [Fact]
    public void TrainingSettings_ClampAndPersist()
    {
        var t = new TrainingSettings { DurationSeconds = 1, MouseSensitivity = -3, CameraFov = 500, TargetCount = 99, Mode = (TrainingMode)42 };
        Assert.Equal(10, t.DurationSeconds);
        Assert.Equal(0.01f, t.MouseSensitivity);
        Assert.Equal(130f, t.CameraFov);
        Assert.Equal(8, t.TargetCount);
        Assert.Equal(TrainingMode.Flick, t.Mode);

        var path = Path.Combine(Path.GetTempPath(), "aimlab-training-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AimLab.Configuration.SettingsStore.Save(new TrainingSettings { Mode = TrainingMode.TargetSwitch, Seed = 5 }, path);
            var loaded = AimLab.Configuration.SettingsStore.Load<TrainingSettings>(path);
            Assert.Equal(TrainingMode.TargetSwitch, loaded.Mode);
            Assert.Equal(5, loaded.Seed);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
