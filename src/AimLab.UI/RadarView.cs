using System.Numerics;
using System.Windows;
using System.Windows.Media;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Mathematics;
using AimLab.Runtime;
using AimLab.Simulation;
using Point = System.Windows.Point;

namespace AimLab.UI;

/// <summary>Top-down debug view of the simulated arena drawn with immediate-mode rendering.</summary>
public sealed class RadarView : FrameworkElement
{
    private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x12)));
    private static readonly Brush WallFill = Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x31, 0x3D)));
    private static readonly Brush Enemy = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5F)));
    private static readonly Brush Friendly = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)));
    private static readonly Brush LocalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x84)));
    private static readonly Brush Dead = Freeze(new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x66)));
    private static readonly Brush ConeFill = Freeze(new SolidColorBrush(Color.FromArgb(0x22, 0x3D, 0xDC, 0x84)));
    private static readonly Pen Grid = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x20))), 1));
    private static readonly Pen ArenaPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2C, 0x31, 0x3D))), 1.5));
    private static readonly Pen ViewPen = Freeze(new Pen(LocalBrush, 1.5));
    private static readonly Pen TargetPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x4A))), 2));
    private static readonly Pen CandidatePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xC8, 0x4A))), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen HiddenPen = Freeze(new Pen(Enemy, 1.2));

    public GameWorld? World { get; set; }
    public AimSettings? Settings { get; set; }
    public EngineFrame? Frame { get; set; }
    public bool ShowCandidates { get; set; } = true;
    public bool ShowFovCone { get; set; } = true;
    public bool ShowHidden { get; set; } = true;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        if (World is null || w < 10 || h < 10)
        {
            return;
        }

        var half = World.ArenaHalfSize;
        var scale = Math.Min(w, h) / (2.0 * half * 1.05);
        var center = new Point(w / 2, h / 2);
        Point Map(Vector3 v) => new(center.X + v.X * scale, center.Y - v.Y * scale);

        for (var g = -half; g <= half; g += 250)
        {
            dc.DrawLine(Grid, Map(new(g, -half, 0)), Map(new(g, half, 0)));
            dc.DrawLine(Grid, Map(new(-half, g, 0)), Map(new(half, g, 0)));
        }

        dc.DrawRectangle(null, ArenaPen, new Rect(Map(new(-half, half, 0)), Map(new(half, -half, 0))));
        foreach (var wall in World.Walls)
        {
            dc.DrawRectangle(WallFill, null, new Rect(Map(new(wall.Min.X, wall.Max.Y, 0)), Map(new(wall.Max.X, wall.Min.Y, 0))));
        }

        var frame = Frame;
        if (frame?.LocalPlayer is not { } local)
        {
            return;
        }

        var origin = Map(local.Origin);
        var yaw = frame.Aim.After.Yaw;
        var reach = half * 2.2f;

        if (ShowFovCone && Settings is { } s)
        {
            // Circular sector drawn as two arcs so 180° stays a half disc instead of collapsing.
            var (start, mid, end) = FovSector.Compute(yaw, s.Fov, reach);
            var r = reach * scale;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(origin, true, true);
                ctx.LineTo(Map(local.Origin + new Vector3(start, 0)), false, false);
                ctx.ArcTo(Map(local.Origin + new Vector3(mid, 0)), new Size(r, r), 0, false, SweepDirection.Counterclockwise, false, false);
                ctx.ArcTo(Map(local.Origin + new Vector3(end, 0)), new Size(r, r), 0, false, SweepDirection.Counterclockwise, false, false);
            }

            geometry.Freeze();
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
            dc.DrawGeometry(ConeFill, null, geometry);
            dc.Pop();
        }

        var yawRad = yaw * MathF.PI / 180;
        dc.DrawLine(ViewPen, origin, Map(local.Origin + new Vector3(MathF.Cos(yawRad), MathF.Sin(yawRad), 0) * 220));

        if (ShowCandidates)
        {
            foreach (var c in frame.Candidates.Skip(1))
            {
                dc.DrawLine(CandidatePen, origin, Map(c.Entity.Origin));
            }
        }

        foreach (var p in frame.Players)
        {
            var pt = Map(p.Origin);
            var isEnemy = p.Team != local.Team;
            if (!p.IsAlive)
            {
                dc.DrawEllipse(Dead, null, pt, 3, 3);
                continue;
            }

            if (isEnemy && !p.IsVisible)
            {
                if (ShowHidden)
                {
                    dc.DrawEllipse(null, HiddenPen, pt, 5, 5);
                }

                continue;
            }

            dc.DrawEllipse(isEnemy ? Enemy : Friendly, null, pt, 5, 5);
        }

        if (frame.Target is { } t)
        {
            var tp = Map(t.Entity.Origin);
            dc.DrawLine(TargetPen, origin, tp);
            dc.DrawEllipse(null, TargetPen, tp, 10, 10);
        }

        dc.DrawEllipse(LocalBrush, null, origin, 6, 6);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
