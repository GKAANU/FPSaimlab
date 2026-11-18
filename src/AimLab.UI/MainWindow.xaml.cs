using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AimLab.UI;

public partial class MainWindow : Window
{
    private const float TurnRateDegreesPerSecond = 120f;

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private int _frames;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Radar.World = vm.Host.Engine.Process.World;
        Radar.Settings = vm.Settings;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();

        Deactivated += (_, _) => _vm.Input.IsActivationHeld = false;
        Closed += (_, _) => _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Min(0.1, now - _lastTime);
        _lastTime = now;

        // Input is only read while this window is focused; nothing is hooked globally.
        _vm.Input.IsActivationHeld = IsActive && IsActivationPressed(_vm.Settings.ActivationKey);
        if (IsActive)
        {
            float yaw = 0, pitch = 0;
            if (Keyboard.IsKeyDown(Key.Left)) yaw += TurnRateDegreesPerSecond * dt;
            if (Keyboard.IsKeyDown(Key.Right)) yaw -= TurnRateDegreesPerSecond * dt;
            if (Keyboard.IsKeyDown(Key.Up)) pitch -= TurnRateDegreesPerSecond * dt;
            if (Keyboard.IsKeyDown(Key.Down)) pitch += TurnRateDegreesPerSecond * dt;
            if (yaw != 0 || pitch != 0)
            {
                _vm.Host.Engine.Process.World.QueueTurn(pitch, yaw);
            }
        }

        var frame = _vm.Host.LatestFrame;
        _vm.Refresh(frame, refreshTelemetry: ++_frames % 15 == 0);

        Radar.Frame = frame;
        Radar.ShowCandidates = _vm.ShowCandidates;
        Radar.ShowFovCone = _vm.ShowFovCone;
        Radar.ShowHidden = _vm.ShowHidden;
        Radar.InvalidateVisual();
    }

    private static bool IsActivationPressed(string key) => key switch
    {
        "LeftMouse" => Mouse.LeftButton == MouseButtonState.Pressed,
        "RightMouse" => Mouse.RightButton == MouseButtonState.Pressed,
        _ => Enum.TryParse<Key>(key, ignoreCase: true, out var k) && Keyboard.IsKeyDown(k),
    };

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Keep arrow keys from moving focus between controls; they steer the camera instead.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && e.OriginalSource is not System.Windows.Controls.Primitives.TextBoxBase)
        {
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }
}
