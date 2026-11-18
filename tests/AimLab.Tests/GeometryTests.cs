using System.Numerics;
using AimLab.Mathematics;

namespace AimLab.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(90f)]
    [InlineData(179f)]
    [InlineData(180f)]
    public void FovSector_EdgesSpanTheFullAngle(float fov)
    {
        var (start, mid, end) = FovSector.Compute(0, fov, 100);
        Assert.Equal(100, start.Length(), 3);
        Assert.Equal(new Vector2(100, 0), mid);
        var angle = MathF.Acos(Vector2.Dot(Vector2.Normalize(start), Vector2.Normalize(end))) * 180 / MathF.PI;
        Assert.Equal(fov, angle, 1);
        // Start is clockwise of the view, end counter-clockwise.
        Assert.True(start.Y <= 0 && end.Y >= 0);
    }

    [Fact]
    public void FovSector_At180_IsAHalfDiscNotADegenerateTriangle()
    {
        var (start, mid, end) = FovSector.Compute(90, 180, 50);
        Assert.Equal(50, start.X, 3);
        Assert.Equal(0, start.Y, 3);
        Assert.Equal(0, mid.X, 3);
        Assert.Equal(50, mid.Y, 3);
        Assert.Equal(-50, end.X, 3);
    }

    [Fact]
    public void FovSector_ClampsOutOfRange()
    {
        var (start, _, end) = FovSector.Compute(0, 400, 1);
        Assert.Equal(-1, Vector2.Dot(start, end), 3);
        var (s2, _, e2) = FovSector.Compute(0, 0, 1);
        Assert.True(Vector2.Distance(s2, e2) > 0);
    }

    [Fact]
    public void RaySphere_HitsNearestSurfaceAndMisses()
    {
        Assert.True(GeometryMath.RaySphere(Vector3.Zero, Vector3.UnitX, new Vector3(100, 0, 0), 10, out var t));
        Assert.Equal(90, t, 3);
        Assert.False(GeometryMath.RaySphere(Vector3.Zero, Vector3.UnitX, new Vector3(100, 20, 0), 10, out _));
        Assert.False(GeometryMath.RaySphere(Vector3.Zero, Vector3.UnitX, new Vector3(-100, 0, 0), 10, out _));
        Assert.True(GeometryMath.RaySphere(Vector3.Zero, Vector3.UnitX, Vector3.Zero, 10, out t));
        Assert.Equal(10, t, 3);
    }

    [Fact]
    public void MouseLook_ConvertsCountsWithSensitivity()
    {
        var d = MouseLook.ToViewDelta(100, -50, 2f, 0.022f, 0.022f, invertY: false);
        Assert.Equal(-4.4f, d.Yaw, 4);   // right = turn right
        Assert.Equal(-2.2f, d.Pitch, 4); // up = look up
        Assert.Equal(2.2f, MouseLook.ToViewDelta(0, -50, 2f, 0.022f, 0.022f, invertY: true).Pitch, 4);
    }
}
