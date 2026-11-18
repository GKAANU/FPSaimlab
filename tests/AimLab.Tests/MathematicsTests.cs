using System.Numerics;
using AimLab.Core;
using AimLab.Mathematics;

namespace AimLab.Tests;

public class MathematicsTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(180f, 180f)]
    [InlineData(-180f, 180f)]
    [InlineData(190f, -170f)]
    [InlineData(-190f, 170f)]
    [InlineData(720f + 45f, 45f)]
    [InlineData(float.NaN, 0f)]
    public void NormalizeYaw_WrapsIntoHalfOpenRange(float input, float expected) =>
        Assert.Equal(expected, AngleMath.NormalizeYaw(input), 3);

    [Fact]
    public void ClampPitch_LimitsTo89Degrees()
    {
        Assert.Equal(89f, AngleMath.ClampPitch(120f));
        Assert.Equal(-89f, AngleMath.ClampPitch(-120f));
        Assert.Equal(0f, AngleMath.ClampPitch(float.PositiveInfinity));
    }

    [Fact]
    public void CalculateAngles_MatchesAxes()
    {
        var forward = AngleMath.CalculateAngles(Vector3.Zero, new Vector3(100, 0, 0));
        Assert.Equal(0f, forward.Pitch, 3);
        Assert.Equal(0f, forward.Yaw, 3);

        var left = AngleMath.CalculateAngles(Vector3.Zero, new Vector3(0, 100, 0));
        Assert.Equal(90f, left.Yaw, 3);

        var up = AngleMath.CalculateAngles(Vector3.Zero, new Vector3(100, 0, 100));
        Assert.Equal(-45f, up.Pitch, 3);

        var down = AngleMath.CalculateAngles(Vector3.Zero, new Vector3(100, 0, -100));
        Assert.Equal(45f, down.Pitch, 3);
    }

    [Fact]
    public void Forward_IsInverseOfCalculateAngles()
    {
        var target = new Vector3(120, -340, 55);
        var angles = AngleMath.CalculateAngles(Vector3.Zero, target);
        var dir = AngleMath.Forward(angles);
        var expected = Vector3.Normalize(target);
        Assert.Equal(expected.X, dir.X, 3);
        Assert.Equal(expected.Y, dir.Y, 3);
        Assert.Equal(expected.Z, dir.Z, 3);
    }

    [Fact]
    public void Delta_TakesShortestYawRotation()
    {
        var d = AngleMath.Delta(new ViewAngles(0, 170), new ViewAngles(0, -170));
        Assert.Equal(20f, d.Yaw, 3);
        Assert.Equal(20f, AngleMath.AngularDistance(new ViewAngles(0, 170), new ViewAngles(0, -170)), 3);
    }

    [Fact]
    public void Smooth_MovesFractionOfDelta()
    {
        var next = AngleMath.Smooth(new ViewAngles(0, 0), new ViewAngles(10, 40), 4f);
        Assert.Equal(2.5f, next.Pitch, 3);
        Assert.Equal(10f, next.Yaw, 3);
    }

    [Fact]
    public void Smooth_OneSnapsAndCrossesYawSeam()
    {
        Assert.Equal(new ViewAngles(5, 40), AngleMath.Smooth(default, new ViewAngles(5, 40), 1f));

        var next = AngleMath.Smooth(new ViewAngles(0, 175), new ViewAngles(0, -175), 2f);
        Assert.Equal(180f, next.Yaw, 3);
    }

    [Fact]
    public void Aabb_SegmentIntersection()
    {
        var box = new Aabb(new Vector3(-10, -10, 0), new Vector3(10, 10, 100));
        Assert.True(box.IntersectsSegment(new Vector3(-50, 0, 50), new Vector3(50, 0, 50)));
        Assert.False(box.IntersectsSegment(new Vector3(-50, 20, 50), new Vector3(50, 20, 50)));
        Assert.False(box.IntersectsSegment(new Vector3(-50, 0, 50), new Vector3(-20, 0, 50)));
        Assert.True(box.Contains(new Vector3(0, 0, 1)));
    }
}
