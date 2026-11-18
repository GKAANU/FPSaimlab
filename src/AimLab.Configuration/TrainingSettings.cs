using System.ComponentModel;
using System.Runtime.CompilerServices;
using AimLab.Core;

namespace AimLab.Configuration;

/// <summary>
/// Training scenario and mouse settings. Every value here is a simulation parameter: none is read from or
/// verified against Counter-Strike 2. Setters clamp to safe ranges. Scenario values apply when a session starts.
/// </summary>
public sealed class TrainingSettings : INotifyPropertyChanged
{
    private TrainingMode _mode = TrainingMode.Flick;
    private int _seed = 1337;
    private double _duration = 60;
    private VisibilityScenario _visibility = VisibilityScenario.AlwaysVisible;
    private float _targetSpeed;
    private float _headRadius = 7f;
    private float _bodyRadius = 14f;
    private float _minDistance = 400f;
    private float _maxDistance = 1200f;
    private int _targetCount = 3;
    private float _spawnYawSpread = 45f;
    private float _spawnPitchSpread = 8f;
    private double _peekVisible = 0.8;
    private double _peekHidden = 1.2;
    private double _fireInterval = 0.1;
    private double _respawnDelay = 0.25;
    private float _cameraFov = 90f;
    private float _sensitivity = 1f;
    private float _yawPerCount = 0.022f;
    private float _pitchPerCount = 0.022f;
    private bool _invertY;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TrainingMode Mode { get => _mode; set => Set(ref _mode, Enum.IsDefined(value) ? value : TrainingMode.Flick); }

    /// <summary>Same seed + same scenario values = same target spawn sequence and motion.</summary>
    public int Seed { get => _seed; set => Set(ref _seed, value); }

    public double DurationSeconds { get => _duration; set => Set(ref _duration, Clamp(value, 10, 600, 60)); }

    public VisibilityScenario Visibility { get => _visibility; set => Set(ref _visibility, Enum.IsDefined(value) ? value : VisibilityScenario.AlwaysVisible); }

    /// <summary>Target strafe speed in simulation units per second.</summary>
    public float TargetSpeed { get => _targetSpeed; set => Set(ref _targetSpeed, Clamp(value, 0f, 600f, 0f)); }

    /// <summary>Head hit-sphere radius in simulation units.</summary>
    public float HeadRadius { get => _headRadius; set => Set(ref _headRadius, Clamp(value, 2f, 40f, 7f)); }

    /// <summary>Body hit-sphere radius in simulation units.</summary>
    public float BodyRadius { get => _bodyRadius; set => Set(ref _bodyRadius, Clamp(value, 4f, 60f, 14f)); }

    public float MinDistance { get => _minDistance; set => Set(ref _minDistance, Clamp(value, 100f, 3000f, 400f)); }

    public float MaxDistance { get => _maxDistance; set => Set(ref _maxDistance, Clamp(value, 100f, 3000f, 1200f)); }

    /// <summary>Simultaneous targets in target-switch mode.</summary>
    public int TargetCount { get => _targetCount; set => Set(ref _targetCount, Math.Clamp(value, 1, 8)); }

    /// <summary>Targets spawn within ± this yaw (degrees) of the starting view direction.</summary>
    public float SpawnYawSpread { get => _spawnYawSpread; set => Set(ref _spawnYawSpread, Clamp(value, 5f, 90f, 45f)); }

    public float SpawnPitchSpread { get => _spawnPitchSpread; set => Set(ref _spawnPitchSpread, Clamp(value, 0f, 30f, 8f)); }

    public double PeekVisibleSeconds { get => _peekVisible; set => Set(ref _peekVisible, Clamp(value, 0.1, 5, 0.8)); }

    public double PeekHiddenSeconds { get => _peekHidden; set => Set(ref _peekHidden, Clamp(value, 0.1, 5, 1.2)); }

    /// <summary>Minimum time between shots. Not a CS2 weapon fire rate.</summary>
    public double FireIntervalSeconds { get => _fireInterval; set => Set(ref _fireInterval, Clamp(value, 0.02, 2, 0.1)); }

    public double RespawnDelaySeconds { get => _respawnDelay; set => Set(ref _respawnDelay, Clamp(value, 0, 5, 0.25)); }

    /// <summary>Horizontal field of view of the first-person camera, in degrees.</summary>
    public float CameraFov { get => _cameraFov; set => Set(ref _cameraFov, Clamp(value, 60f, 130f, 90f)); }

    public float MouseSensitivity { get => _sensitivity; set => Set(ref _sensitivity, Clamp(value, 0.01f, 20f, 1f)); }

    /// <summary>Degrees of yaw per raw mouse count at sensitivity 1. Default is a Source-style value; verify against your own setup.</summary>
    public float YawDegreesPerCount { get => _yawPerCount; set => Set(ref _yawPerCount, Clamp(value, 0.001f, 1f, 0.022f)); }

    public float PitchDegreesPerCount { get => _pitchPerCount; set => Set(ref _pitchPerCount, Clamp(value, 0.001f, 1f, 0.022f)); }

    public bool InvertY { get => _invertY; set => Set(ref _invertY, value); }

    private static float Clamp(float v, float min, float max, float fallback) => float.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    private static double Clamp(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
