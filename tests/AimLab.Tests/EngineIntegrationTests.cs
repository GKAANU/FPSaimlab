using AimLab.Configuration;
using AimLab.Core;
using AimLab.Mathematics;
using AimLab.Runtime;
using AimLab.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AimLab.Tests;

public class EngineIntegrationTests
{
    private static ServiceProvider Build(AimSettings settings, int seed = 7) =>
        new ServiceCollection()
            .AddAimLab(new SimulationOptions { Seed = seed }, settings, new LoggerConfiguration().CreateLogger())
            .BuildServiceProvider();

    [Fact]
    public void Pipeline_AcquiresTargetsAndRecordsTelemetry()
    {
        var settings = new AimSettings { Fov = 180, Smoothing = 4, VisibilityCheck = false, RequireActivationKey = false };
        using var sp = Build(settings);
        var engine = sp.GetRequiredService<AimLabEngine>();

        EngineFrame? last = null;
        var applied = 0;
        for (var i = 0; i < 64 * 10; i++)
        {
            last = engine.Tick(1.0 / 64);
            if (last.Aim.Applied) applied++;
        }

        Assert.NotNull(last);
        Assert.Equal(640, last!.Tick);
        Assert.Equal(9, last.Players.Count);
        Assert.Equal(0, last.InvalidEntityCount);
        Assert.True(applied > 0);

        // The world view is exactly what the aim step produced.
        Assert.Equal(last.Aim.After, engine.Process.World.CurrentView);

        var summary = engine.Telemetry.GetSummary();
        Assert.Equal(640, summary.Session.SampleCount);
        Assert.True(summary.TargetAcquisitions > 0);
        Assert.NotEmpty(summary.AcquisitionTimes);
    }

    [Fact]
    public void Pipeline_DoesNotMoveViewWithoutActivation()
    {
        var settings = new AimSettings { Fov = 180, VisibilityCheck = false, RequireActivationKey = true };
        using var sp = Build(settings);
        var engine = sp.GetRequiredService<AimLabEngine>();
        var start = engine.Process.World.CurrentView;

        for (var i = 0; i < 128; i++)
        {
            Assert.False(engine.Tick(1.0 / 64).Aim.Applied);
        }

        Assert.Equal(start, engine.Process.World.CurrentView);

        sp.GetRequiredService<ManualInputState>().IsActivationHeld = true;
        var moved = false;
        for (var i = 0; i < 128 && !moved; i++)
        {
            moved = engine.Tick(1.0 / 64).Aim.Applied;
        }

        Assert.True(moved);
    }

    [Fact]
    public void VisibilityCheck_OnlyTargetsVisibleEnemies()
    {
        var settings = new AimSettings { Fov = 180, VisibilityCheck = true, RequireActivationKey = false };
        using var sp = Build(settings, seed: 99);
        var engine = sp.GetRequiredService<AimLabEngine>();

        for (var i = 0; i < 64 * 5; i++)
        {
            var frame = engine.Tick(1.0 / 64);
            Assert.All(frame.Candidates, c => Assert.True(c.Entity.IsVisible && c.Entity.IsAlive && c.Entity.Team == Team.Attackers));
        }
    }

    [Fact]
    public void ManualTurn_IsAppliedAndNormalized()
    {
        using var sp = Build(new AimSettings { Enabled = false });
        var engine = sp.GetRequiredService<AimLabEngine>();
        engine.Process.World.QueueTurn(200, 370);
        engine.Tick(1.0 / 64);
        var v = engine.Process.World.CurrentView;
        Assert.Equal(AngleMath.MaxPitch, v.Pitch);
        Assert.Equal(10f, v.Yaw, 3);
    }

    [Fact]
    public async Task Host_RunsInBackground()
    {
        using var sp = Build(new AimSettings());
        var host = sp.GetRequiredService<SimulationHost>();
        host.Start();
        await Task.Delay(300);
        host.Stop();
        Assert.NotNull(host.LatestFrame);
        Assert.True(host.LatestFrame!.Tick > 0);
    }
}
