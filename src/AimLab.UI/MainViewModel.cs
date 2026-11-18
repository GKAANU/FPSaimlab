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
    private string _sessionHud = "-";
    private string _status = "Ready";
    private string? _resultText;
    private SessionResult? _shownResult;
    private bool _showRadar;
    private bool _showCandidates = true;
    private bool _showFovCone = true;
    private bool _showHidden = true;

    public MainViewModel(SimulationHost host, AimSettings settings, TrainingSettings training, ManualInputState input, ILogger logger)
    {
        _host = host;
        _logger = logger;
        Settings = settings;
        Training = training;
        Input = input;

        StartSessionCommand = new RelayCommand(() => StartSession(timed: true));
        FreePlayCommand = new RelayCommand(() => StartSession(timed: false));
        StopSessionCommand = new RelayCommand(() => host.Engine.StopSession());
        CloseResultCommand = new RelayCommand(() => ResultText = null);
        ExportJsonCommand = new RelayCommand(() => Export(json: true));
        ExportCsvCommand = new RelayCommand(() => Export(json: false));
        ResetTelemetryCommand = new RelayCommand(() =>
        {
            host.Engine.ResetTelemetry();
            Status = "Telemetry reset";
        });
        SaveSettingsCommand = new RelayCommand(SaveSettings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AimSettings Settings { get; }

    public TrainingSettings Training { get; }

    public ManualInputState Input { get; }

    public SimulationHost Host => _host;

    public IReadOnlyList<TargetPriority> Priorities { get; } = Enum.GetValues<TargetPriority>();

    public IReadOnlyList<BodyRegion> Regions { get; } = Enum.GetValues<BodyRegion>();

    public IReadOnlyList<TrainingMode> Modes { get; } = Enum.GetValues<TrainingMode>();

    public IReadOnlyList<VisibilityScenario> Visibilities { get; } = Enum.GetValues<VisibilityScenario>();

    public IReadOnlyList<string> ActivationKeys { get; } =
        ["LeftShift", "LeftCtrl", "LeftAlt", "CapsLock", "E", "Q", "X", "RightMouse"];

    public ICommand StartSessionCommand { get; }
    public ICommand FreePlayCommand { get; }
    public ICommand StopSessionCommand { get; }
    public ICommand CloseResultCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ResetTelemetryCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public string EntitySummary { get => _entitySummary; private set => Set(ref _entitySummary, value); }
    public string Performance { get => _performance; private set => Set(ref _performance, value); }
    public string TargetInfo { get => _targetInfo; private set => Set(ref _targetInfo, value); }
    public string TelemetryInfo { get => _telemetryInfo; private set => Set(ref _telemetryInfo, value); }
    public string SessionHud { get => _sessionHud; private set => Set(ref _sessionHud, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>End-of-session summary; null hides the result panel.</summary>
    public string? ResultText
    {
        get => _resultText;
        private set
        {
            Set(ref _resultText, value);
            OnPropertyChanged(nameof(HasResult));
        }
    }

    public bool HasResult => _resultText is not null;

    public bool ShowRadar { get => _showRadar; set => Set(ref _showRadar, value); }
    public bool ShowCandidates { get => _showCandidates; set => Set(ref _showCandidates, value); }
    public bool ShowFovCone { get => _showFovCone; set => Set(ref _showFovCone, value); }
    public bool ShowHidden { get => _showHidden; set => Set(ref _showHidden, value); }

    public void StartSession(bool timed)
    {
        ResultText = null;
        var options = ScenarioFactory.Create(Training);
        _host.Engine.StartSession(options, timed ? Training.DurationSeconds : 0);
        Status = timed
            ? $"Session started: {Training.Mode}, {Training.DurationSeconds:F0} s, seed {Training.Seed}, assist {(Settings.Enabled ? "ON" : "OFF")}"
            : $"Free play: {Training.Mode}, seed {Training.Seed}";
    }

    /// <summary>Called by the window's refresh timer.</summary>
    public void Refresh(EngineFrame? frame, bool refreshTelemetry)
    {
        var result = _host.Engine.LastResult;
        if (result is not null && !ReferenceEquals(result, _shownResult))
        {
            _shownResult = result;
            ResultText = result.Report;
        }

        if (frame is null)
        {
            return;
        }

        var ci = CultureInfo.InvariantCulture;
        var session = frame.Session;
        var shots = _host.Engine.Telemetry.GetSummary().Shots;
        var clock = session.Status switch
        {
            SessionStatus.Running => string.Create(ci, $"{session.RemainingSeconds:F1} s left"),
            SessionStatus.Finished => "finished",
            _ => string.Create(ci, $"free play {session.ElapsedSeconds:F0} s"),
        };
        SessionHud = string.Create(ci,
            $"{session.Metadata?.Mode} · {clock} · shots {shots.Shots} · hits {shots.Hits} · {shots.Accuracy:P0} · assist {(Settings.Enabled ? "ON" : "OFF")}");

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
                $"#{t.Entity.Id} · HP {t.Entity.Health} · {t.Distance:F0} u\n" +
                $"error before {frame.Aim.ErrorBefore:F2}° · after {frame.Aim.ErrorAfter:F2}°\n" +
                $"assist {(frame.Aim.Applied ? "MOVING" : "idle")} · view {frame.Aim.After}")
            : "No target in analysis FOV";

        if (refreshTelemetry)
        {
            var s = _host.Engine.Telemetry.GetSummary();
            TelemetryInfo = string.Create(ci,
                $"session {s.Session.SampleCount} ticks · {s.Session.DurationSeconds:F1} s (window {s.Window.SampleCount})\n" +
                $"acquisitions {s.TargetAcquisitions} · mean {s.MeanAcquisitionTimeSeconds * 1000:F0} ms\n" +
                $"reaction {s.MeanReactionTimeSeconds * 1000:F0} ms · switches {s.TargetSwitchesPerSecond:F2}/s\n" +
                $"tracking {s.TrackingAccuracy:P1} · assist active {s.AssistActiveShare:P0}\n" +
                $"ang. vel {s.Session.MeanAngularVelocity:F1} / max {s.Session.MaxAngularVelocity:F1} °/s\n" +
                $"ang. accel {s.Session.MeanAbsAngularAcceleration:F0} / max {s.Session.MaxAbsAngularAcceleration:F0} °/s²\n" +
                $"first hit {(s.Shots.FirstHitAtSeconds is { } f ? f.ToString("F2", ci) + " s" : "-")} · to-first-hit {s.Shots.MeanTimeToFirstHitSeconds * 1000:F0} ms\n" +
                $"switch time {s.Shots.MeanSwitchTimeSeconds * 1000:F0} ms · HS {s.Shots.HeadshotRate:P0}");
        }
    }

    private void Export(bool json)
    {
        try
        {
            var snapshot = _host.Engine.Telemetry.GetSnapshot();
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var mode = snapshot.Metadata?.Mode.ToString() ?? "session";
            var path = Path.Combine(App.DataDirectory, "telemetry", $"{mode}-{stamp}.{(json ? "json" : "csv")}");
            if (json)
            {
                TelemetryExporter.ExportJson(path, snapshot);
            }
            else
            {
                TelemetryExporter.ExportCsv(path, snapshot.Samples);
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
            SettingsStore.Save(Training, App.TrainingPath);
            Status = "Settings saved to " + App.DataDirectory;
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
