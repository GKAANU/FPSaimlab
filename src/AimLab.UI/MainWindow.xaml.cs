using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AimLab.Mathematics;
using AimLab.Runtime;

namespace AimLab.UI;

public partial class MainWindow : Window
{
    private const float TurnRateDegreesPerSecond = 120f;

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private RawMouseInput? _rawMouse;
    private bool _mouseLook;
    private double _lastTime;
    private int _frames;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Radar.Settings = vm.Settings;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();

        FirstPerson.MouseLeftButtonDown += OnViewMouseDown;
        Deactivated += (_, _) =>
        {
            _vm.Input.IsActivationHeld = false;
            SetMouseLook(false);
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            SetMouseLook(false);
            _rawMouse?.Dispose();
        };
    }

    private AimLabEngine Engine => _vm.Host.Engine;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _rawMouse = new RawMouseInput(this);
        _rawMouse.Moved += OnRawMouseMoved;
    }

    private void OnViewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseLook)
        {
            SetMouseLook(true);
        }
        else
        {
            Engine.QueueFire();
        }

        e.Handled = true;
    }

    private void OnRawMouseMoved(int dx, int dy)
    {
        if (!_mouseLook || !IsActive)
        {
            return;
        }

        var t = _vm.Training;
        var delta = MouseLook.ToViewDelta(dx, dy, t.MouseSensitivity, t.YawDegreesPerCount, t.PitchDegreesPerCount, t.InvertY);
        Engine.QueueTurn(delta.Pitch, delta.Yaw);
    }

    private void SetMouseLook(bool on)
    {
        if (on && (!IsActive || _rawMouse is not { IsAvailable: true } || _vm.ShowRadar))
        {
            return;
        }

        _mouseLook = on;
        Mouse.OverrideCursor = on ? Cursors.None : null;
        if (on)
        {
            var topLeft = FirstPerson.PointToScreen(new Point(0, 0));
            var bottomRight = FirstPerson.PointToScreen(new Point(FirstPerson.ActualWidth, FirstPerson.ActualHeight));
            RawMouseInput.ClipCursorTo(new Rect(topLeft, bottomRight));
        }
        else
        {
            RawMouseInput.ClipCursorTo(null);
        }
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
                Engine.QueueTurn(pitch, yaw);
            }
        }

        var frame = _vm.Host.LatestFrame;
        _vm.Refresh(frame, refreshTelemetry: ++_frames % 15 == 0);
        if (_mouseLook && (_vm.HasResult || _vm.ShowRadar))
        {
            SetMouseLook(false);
        }

        var world = Engine.Process.World;
        if (_vm.ShowRadar)
        {
            Radar.World = world;
            Radar.Frame = frame;
            Radar.ShowCandidates = _vm.ShowCandidates;
            Radar.ShowFovCone = _vm.ShowFovCone;
            Radar.ShowHidden = _vm.ShowHidden;
            Radar.InvalidateVisual();
        }
        else
        {
            FirstPerson.Render(frame, world, _vm.Training.CameraFov);
        }
    }

    private static bool IsActivationPressed(string key) => key switch
    {
        "RightMouse" => Mouse.RightButton == MouseButtonState.Pressed,
        _ => Enum.TryParse<Key>(key, ignoreCase: true, out var k) && Keyboard.IsKeyDown(k),
    };

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _mouseLook)
        {
            SetMouseLook(false);
            e.Handled = true;
        }

        // Keep arrow keys from moving focus between controls; they steer the camera instead.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && e.OriginalSource is not System.Windows.Controls.Primitives.TextBoxBase)
        {
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }
}
