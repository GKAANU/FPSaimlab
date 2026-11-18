using System.ComponentModel;
using System.Runtime.CompilerServices;
using AimLab.Core;

namespace AimLab.Configuration;

/// <summary>
/// Live aim assist configuration. Setters clamp to safe ranges so values loaded from disk
/// or typed in the UI can never put the engine in an invalid state.
/// </summary>
public sealed class AimSettings : INotifyPropertyChanged
{
    public const float MinFov = 1f, MaxFov = 180f;
    public const float MinSmoothing = 1f, MaxSmoothing = 50f;

    private bool _enabled = true;
    private float _fov = 30f;
    private float _smoothing = 8f;
    private TargetPriority _priority = TargetPriority.ClosestToCrosshair;
    private BodyRegion _region = BodyRegion.Head;
    private bool _visibilityCheck = true;
    private bool _requireActivationKey = true;
    private string _activationKey = "LeftShift";
    private float _acquireThreshold = 1.0f;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>Full cone angle in degrees; a target qualifies when within half of it from the crosshair.</summary>
    public float Fov { get => _fov; set => Set(ref _fov, Clamp(value, MinFov, MaxFov, 30f)); }

    /// <summary>Divisor applied to the angle delta each tick. 1 = instant snap.</summary>
    public float Smoothing { get => _smoothing; set => Set(ref _smoothing, Clamp(value, MinSmoothing, MaxSmoothing, 8f)); }

    public TargetPriority Priority { get => _priority; set => Set(ref _priority, Enum.IsDefined(value) ? value : TargetPriority.ClosestToCrosshair); }

    public BodyRegion Region { get => _region; set => Set(ref _region, Enum.IsDefined(value) ? value : BodyRegion.Head); }

    public bool VisibilityCheck { get => _visibilityCheck; set => Set(ref _visibilityCheck, value); }

    public bool RequireActivationKey { get => _requireActivationKey; set => Set(ref _requireActivationKey, value); }

    /// <summary>Key name interpreted by the UI (a WPF Key name, or "LeftMouse"/"RightMouse").</summary>
    public string ActivationKey { get => _activationKey; set => Set(ref _activationKey, string.IsNullOrWhiteSpace(value) ? "LeftShift" : value.Trim()); }

    /// <summary>Angular error in degrees under which a target counts as acquired / tracked.</summary>
    public float AcquireThresholdDegrees { get => _acquireThreshold; set => Set(ref _acquireThreshold, Clamp(value, 0.1f, 10f, 1f)); }

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

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
