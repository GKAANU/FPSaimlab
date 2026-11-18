using System.Numerics;
using AimLab.AimAssist;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Mathematics;
using AimLab.Targeting;

namespace AimLab.Tests;

public class TargetingAndAimTests
{
    private static readonly AimSettings Wide = new() { Fov = 180f, VisibilityCheck = true };
    private readonly TargetSelector _selector = new();

    [Fact]
    public void Filters_TeammatesDeadAndHidden()
    {
        var local = TestData.Local(Vector3.Zero, default);
        var players = new[]
        {
            TestData.Player(2, Team.Defenders, new Vector3(500, 0, 0)),
            TestData.Player(3, Team.Attackers, new Vector3(500, 10, 0), health: 0),
            TestData.Player(4, Team.Attackers, new Vector3(500, -10, 0), visible: false),
            TestData.Player(5, Team.Attackers, new Vector3(800, 50, 0)),
        };

        var candidates = _selector.EvaluateCandidates(local, players, Wide);
        Assert.Equal([5], candidates.Select(c => c.Entity.Id));

        var noVisCheck = new AimSettings { Fov = 180f, VisibilityCheck = false };
        Assert.Equal(2, _selector.EvaluateCandidates(local, players, noVisCheck).Count);
    }

    [Fact]
    public void Fov_ExcludesTargetsOutsideCone()
    {
        var local = TestData.Local(new Vector3(0, 0, 0), default);
        // ~45 degrees to the side, eye height cancels out on the head point.
        var side = TestData.Player(2, Team.Attackers, new Vector3(500, 500, 0));
        var narrow = new AimSettings { Fov = 60f };
        Assert.Null(_selector.SelectTarget(local, [side], narrow));
        Assert.NotNull(_selector.SelectTarget(local, [side], new AimSettings { Fov = 100f }));
    }

    [Theory]
    [InlineData(TargetPriority.ClosestToCrosshair, 2)]
    [InlineData(TargetPriority.ClosestDistance, 3)]
    [InlineData(TargetPriority.LowestHealth, 4)]
    public void Priority_SelectsExpectedTarget(TargetPriority priority, int expectedId)
    {
        var local = TestData.Local(Vector3.Zero, default);
        var players = new[]
        {
            TestData.Player(2, Team.Attackers, new Vector3(1000, 0, 0)),
            TestData.Player(3, Team.Attackers, new Vector3(200, 120, 0)),
            TestData.Player(4, Team.Attackers, new Vector3(700, -300, 0), health: 20),
        };

        var settings = new AimSettings { Fov = 180f, Priority = priority };
        Assert.Equal(expectedId, _selector.SelectTarget(local, players, settings)!.Entity.Id);
    }

    [Fact]
    public void Region_ChangesAimPoint()
    {
        var local = TestData.Local(Vector3.Zero, default);
        var enemy = TestData.Player(2, Team.Attackers, new Vector3(500, 0, 0));
        var head = _selector.SelectTarget(local, [enemy], new AimSettings { Fov = 180, Region = BodyRegion.Head })!;
        var body = _selector.SelectTarget(local, [enemy], new AimSettings { Fov = 180, Region = BodyRegion.Body })!;
        Assert.Equal(enemy.HeadPosition, head.AimPoint);
        Assert.Equal(enemy.BodyPosition, body.AimPoint);
        Assert.True(body.AimAngles.Pitch > head.AimAngles.Pitch);
    }

    [Fact]
    public void DeadLocalPlayer_HasNoTargets()
    {
        var local = new AimLab.Entities.LocalPlayerState();
        local.Update(TestData.Player(1, Team.Defenders, Vector3.Zero, health: 0));
        Assert.Empty(_selector.EvaluateCandidates(local, [TestData.Player(2, Team.Attackers, new Vector3(100, 0, 0))], Wide));
    }

    [Fact]
    public void AimAssist_RespectsEnableAndActivationKey()
    {
        var local = TestData.Local(Vector3.Zero, default);
        var enemy = TestData.Player(2, Team.Attackers, new Vector3(500, 200, 0));
        var settings = new AimSettings { Fov = 180, Smoothing = 4, RequireActivationKey = true };
        var target = _selector.SelectTarget(local, [enemy], settings);
        var controller = new AimAssistController();

        Assert.False(controller.Update(local, target, settings, activationHeld: false).Applied);

        var step = controller.Update(local, target, settings, activationHeld: true);
        Assert.True(step.Applied);
        Assert.Equal(target!.AimAngles.Yaw / 4f, step.After.Yaw, 3);
        Assert.True(AngleMath.AngularDistance(step.After, target.AimAngles) < step.ErrorBefore);

        settings.Enabled = false;
        Assert.False(controller.Update(local, target, settings, true).Applied);

        settings.Enabled = true;
        settings.RequireActivationKey = false;
        Assert.True(controller.Update(local, target, settings, false).Applied);
    }

    [Fact]
    public void AimAssist_ConvergesOverTicks()
    {
        var view = new ViewAngles(0, 0);
        var enemy = TestData.Player(2, Team.Attackers, new Vector3(400, 300, 50));
        var settings = new AimSettings { Fov = 180, Smoothing = 5, RequireActivationKey = false };
        var controller = new AimAssistController();

        for (var i = 0; i < 60; i++)
        {
            var local = TestData.Local(Vector3.Zero, view);
            var target = _selector.SelectTarget(local, [enemy], settings);
            view = controller.Update(local, target, settings, false).After;
        }

        var aim = AngleMath.CalculateAngles(new Vector3(0, 0, 64), enemy.HeadPosition);
        Assert.True(AngleMath.AngularDistance(view, aim) < 0.01f);
    }
}
