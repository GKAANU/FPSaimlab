using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AimLab.Core;
using AimLab.Mathematics;
using AimLab.Runtime;
using AimLab.Simulation;

namespace AimLab.UI;

/// <summary>
/// Simple first-person render of the simulation with WPF 3D. Targets are drawn as their exact hit spheres
/// (head + body), walls as boxes. Not a CS2 renderer; it shows what the hit test uses.
/// </summary>
public sealed class FirstPersonView : Grid
{
    private static readonly Material WallMaterial = Freeze(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x5E))));
    private static readonly Material GroundMaterial = Freeze(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x28))));
    private static readonly Material TargetMaterial = Freeze(Mat(Color.FromRgb(0xFF, 0x5A, 0x5F)));
    private static readonly Material FocusMaterial = Freeze(Mat(Color.FromRgb(0xFF, 0xC8, 0x4A)));
    private static readonly MeshGeometry3D UnitSphere = BuildSphere(16, 12);

    private readonly PerspectiveCamera _camera = new() { UpDirection = new Vector3D(0, 0, 1), NearPlaneDistance = 1, FarPlaneDistance = 20000 };
    private readonly Model3DGroup _static = new();
    private readonly Model3DGroup _targets = new();
    private GameWorld? _builtFor;

    public FirstPersonView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x1A));
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(0x70, 0x70, 0x78)));
        scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.4, 0.3, -1)));
        scene.Children.Add(_static);
        scene.Children.Add(_targets);

        var viewport = new Viewport3D { Camera = _camera, IsHitTestVisible = false, ClipToBounds = true };
        viewport.Children.Add(new ModelVisual3D { Content = scene });
        Children.Add(viewport);
        Children.Add(new Crosshair { IsHitTestVisible = false });
    }

    public void Render(EngineFrame? frame, GameWorld world, float horizontalFov)
    {
        if (!ReferenceEquals(world, _builtFor))
        {
            RebuildStatic(world);
            _builtFor = world;
        }

        _targets.Children.Clear();
        if (frame?.LocalPlayer is not { } local)
        {
            return;
        }

        _camera.Position = P(local.HeadPosition);
        _camera.LookDirection = V(AngleMath.Forward(frame.Aim.After));
        _camera.FieldOfView = horizontalFov;

        var options = world.Options;
        foreach (var p in frame.Players)
        {
            if (!p.IsAlive || !p.IsVisible || p.Team == local.Team)
            {
                continue;
            }

            var material = frame.Target?.Entity.Id == p.Id ? FocusMaterial : TargetMaterial;
            _targets.Children.Add(Sphere(p.HeadPosition, options.HeadRadius, material));
            _targets.Children.Add(Sphere(p.BodyPosition, options.BodyRadius, material));
        }
    }

    private void RebuildStatic(GameWorld world)
    {
        _static.Children.Clear();
        var h = world.ArenaHalfSize * 2;
        _static.Children.Add(Box(new Vector3(-h, -h, -2), new Vector3(h, h, 0), GroundMaterial));
        foreach (var wall in world.Walls)
        {
            _static.Children.Add(Box(wall.Min, wall.Max, WallMaterial));
        }
    }

    private static GeometryModel3D Sphere(Vector3 center, float radius, Material material) => new(UnitSphere, material)
    {
        Transform = new Transform3DGroup
        {
            Children =
            {
                new ScaleTransform3D(radius, radius, radius),
                new TranslateTransform3D(center.X, center.Y, center.Z),
            },
        },
    };

    private static GeometryModel3D Box(Vector3 min, Vector3 max, Material material)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i < 8; i++)
        {
            mesh.Positions.Add(new Point3D((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z));
        }

        int[] faces = [0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5];
        foreach (var index in faces)
        {
            mesh.TriangleIndices.Add(index);
        }

        mesh.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static MeshGeometry3D BuildSphere(int slices, int stacks)
    {
        var mesh = new MeshGeometry3D();
        for (var st = 0; st <= stacks; st++)
        {
            var phi = Math.PI * st / stacks;
            for (var sl = 0; sl <= slices; sl++)
            {
                var theta = 2 * Math.PI * sl / slices;
                var n = new Vector3D(Math.Sin(phi) * Math.Cos(theta), Math.Sin(phi) * Math.Sin(theta), Math.Cos(phi));
                mesh.Positions.Add(new Point3D(n.X, n.Y, n.Z));
                mesh.Normals.Add(n);
            }
        }

        for (var st = 0; st < stacks; st++)
        {
            for (var sl = 0; sl < slices; sl++)
            {
                var a = st * (slices + 1) + sl;
                var b = a + slices + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
            }
        }

        mesh.Freeze();
        return mesh;
    }

    private static Material Mat(Color color) => new MaterialGroup
    {
        Children = { new DiffuseMaterial(new SolidColorBrush(color)), new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0x60, color.R, color.G, color.B))) },
    };

    private static Point3D P(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3D V(Vector3 v) => new(v.X, v.Y, v.Z);

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private sealed class Crosshair : FrameworkElement
    {
        private static readonly Pen Line = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x84)), 2));

        protected override void OnRender(DrawingContext dc)
        {
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            const double gap = 4, len = 9;
            dc.DrawLine(Line, new Point(c.X - gap - len, c.Y), new Point(c.X - gap, c.Y));
            dc.DrawLine(Line, new Point(c.X + gap, c.Y), new Point(c.X + gap + len, c.Y));
            dc.DrawLine(Line, new Point(c.X, c.Y - gap - len), new Point(c.X, c.Y - gap));
            dc.DrawLine(Line, new Point(c.X, c.Y + gap), new Point(c.X, c.Y + gap + len));
        }
    }
}
