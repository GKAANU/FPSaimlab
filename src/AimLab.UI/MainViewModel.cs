using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Runtime;
using AimLab.Telemetry;
using Serilog;

namespace AimLab.UI;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SimulationHost _host;
    private readonly ILogger _logger;
    private string _entitySummary = "-";
    private string _performance = "-";
    private string _targetInfo = "-";
    private string _telemetryInfo = "-";
    private string _status = "Ready";
    private bool _showCandidates = true;
    private bool _showFovCone = true;
    private bool _showHidden = true;

    public MainViewModel(SimulationHost host, AimSettings settings, ManualInputState input, ILogger logger)
    {
        _host = host;
        _logger = logger;
        Settings = settings;
        Input = input;

        ExportJsonCommand = new RelayCommand(() => Export(json: true));
        ExportCsvCommand = new RelayCommand(() => Export(json: false));
        ResetTelemetryCommand = new RelayCommand(() =>
        {
            host.Engine.Telemetry.Reset();
            Status = "Telemetry reset";
        });
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ToggleRunningCommand = new RelayCommand(() =>
        {
            if (host.IsRunning) host.Stop(); else host.Start();
            OnPropertyChanged(nameof(RunLabel));
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AimSettings Settings { get; }

    public ManualInputState Input { get; }

    public SimulationHost Host => _host;

    public IReadOnlyList<TargetPriority> Priorities { get; } = Enum.GetValues<TargetPriority>();

    public IReadOnlyList<BodyRegion> Regions { get; } = Enum.GetValues<BodyRegion>();

    public IReadOnlyList<string> ActivationKeys { get; } =
        ["LeftShift", "LeftCtrl", "LeftAlt", "CapsLock", "E", "Q", "X", "LeftMouse", "RightMouse"];

    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ResetTelemetryCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ToggleRunningCommand { get; }

    public string RunLabel => _host.IsRunning ? "Pause simulation" : "Resume simulation";

    public string EntitySummary { get => _entitySummary; private set => Set(ref _entitySummary, value); }
    public string Performance { get => _performance; private set => Set(ref _performance, value); }
    public string TargetInfo { get => _targetInfo; private set => Set(ref _targetInfo, value); }
    public string TelemetryInfo { get => _telemetryInfo; private set => Set(ref _telemetryInfo, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool ShowCandidates { get => _showCandidates; set => Set(ref _showCandidates, value); }
    public bool ShowFovCone { get => _showFovCone; set => Set(ref _showFovCone, value); }
    public bool ShowHidden { get => _showHidden; set => Set(ref _showHidden, value); }

    /// <summary>Called by the window's refresh timer.</summary>
    public void Refresh(EngineFrame? frame, bool refreshTelemetry)
    {
        if (frame is null)
        {
            return;
        }

        var ci = CultureInfo.InvariantCulture;
        var enemies = frame.Players.Count(p => p.Team != (frame.LocalPlayer?.Team ?? Team.None));
        var alive = frame.Players.Count(p => p.IsAlive);
        var visible = frame.Players.Count(p => p.IsVisible);
        EntitySummary = string.Create(ci,
            $"{frame.Players.Count + (frame.LocalPlayer is null ? 0 : 1)} valid · {frame.InvalidEntityCount} invalid · {frame.TrackedEntityCount} cached\n" +
            $"{enemies} enemies · {alive} alive · {visible} visible · {frame.Candidates.Count} in FOV");

        var m = frame.Metrics;
        Performance = string.Create(ci,
            $"tick {frame.Tick} · {_host.MeasuredTickRate:F1} Hz\n" +
            $"frame {m.TotalMs:F3} ms (sim {m.SimulationMs:F3} · read {m.MemoryReadMs:F3} · target {m.TargetingMs:F3})");

        TargetInfo = frame.Target is { } t
            ? string.Create(ci,
                $"#{t.Entity.Id} {t.Entity.Team} · HP {t.Entity.Health} · {t.Distance:F0} u\n" +
                $"error {frame.Aim.ErrorBefore:F2}° · aim {(frame.Aim.Applied ? "ACTIVE" : "idle")} · view {frame.Aim.After}")
            : "No target in FOV";

        if (refreshTelemetry)
        {
            var s = _host.Engine.Telemetry.GetSummary();
            TelemetryInfo = string.Create(ci,
                $"samples {s.SampleCount} · {s.DurationSeconds:F1} s\n" +
                $"acquisitions {s.TargetAcquisitions} · mean acquire {s.MeanAcquisitionTimeSeconds * 1000:F0} ms\n" +
                $"reaction {s.MeanReactionTimeSeconds * 1000:F0} ms · switches {s.TargetSwitchesPerSecond:F2}/s\n" +
                $"ang. vel mean {s.MeanAngularVelocity:F1} / max {s.MaxAngularVelocity:F1} °/s\n" +
                $"ang. accel mean {s.MeanAbsAngularAcceleration:F0} / max {s.MaxAbsAngularAcceleration:F0} °/s²\n" +
                $"tracking accuracy {s.TrackingAccuracy:P1}");
        }
    }

    private void Export(bool json)
    {
        try
        {
            var telemetry = _host.Engine.Telemetry;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(App.DataDirectory, "telemetry", $"telemetry-{stamp}.{(json ? "json" : "csv")}");
            if (json)
            {
                TelemetryExporter.ExportJson(path, telemetry.GetSummary(), telemetry.GetSamples());
            }
            else
            {
                TelemetryExporter.ExportCsv(path, telemetry.GetSamples());
            }

            _logger.Information("Telemetry exported to {Path}", path);
            Status = "Exported " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error(ex, "Telemetry export failed");
            Status = "Export failed: " + ex.Message;
        }
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings, App.SettingsPath);
            Status = "Settings saved to " + App.SettingsPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error(ex, "Saving settings failed");
            Status = "Save failed: " + ex.Message;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
