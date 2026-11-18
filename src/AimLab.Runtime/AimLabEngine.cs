using System.Diagnostics;
using AimLab.AimAssist;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Entities;
using AimLab.Simulation;
using AimLab.Targeting;
using AimLab.Telemetry;
using Serilog;

namespace AimLab.Runtime;

/// <summary>Timing for one engine tick, in milliseconds.</summary>
public sealed record FrameMetrics(double SimulationMs, double MemoryReadMs, double TargetingMs, double TotalMs);

public enum SessionStatus
{
    /// <summary>Untimed; telemetry keeps recording until reset.</summary>
    FreePlay,
    Running,
    Finished,
}

public sealed record SessionState(SessionStatus Status, SessionMetadata? Metadata, double ElapsedSeconds, double DurationSeconds)
{
    public double RemainingSeconds => Status == SessionStatus.Running ? Math.Max(0, DurationSeconds - ElapsedSeconds) : 0;
}

public sealed record SessionResult(SessionMetadata? Metadata, TelemetrySnapshot Snapshot, string Report);

/// <summary>Everything the UI needs to render one tick.</summary>
public sealed record EngineFrame(
    long Tick,
    double Time,
    PlayerEntity? LocalPlayer,
    IReadOnlyList<PlayerEntity> Players,
    int InvalidEntityCount,
    int TrackedEntityCount,
    IReadOnlyList<TargetCandidate> Candidates,
    TargetCandidate? Target,
    AimStep Aim,
    IReadOnlyList<ShotEvent> Shots,
    SessionState Session,
    FrameMetrics Metrics);

/// <summary>
/// One deterministic pipeline step: simulate (manual input and shots), read simulated memory, parse and cache
/// entities, analyse the target, compute the optional assist step, apply it to the simulated world, record telemetry.
/// Thread-safe: <see cref="Tick"/> and the session methods serialize on one lock.
/// </summary>
public sealed class AimLabEngine(
    SimulatedGameProcess process,
    IEntityParser parser,
    IEntityCache cache,
    ITargetSelector selector,
    IAimAssistController aim,
    ITelemetryRecorder telemetry,
    AimSettings settings,
    IInputState input,
    ILogger logger)
{
    private readonly Lock _gate = new();
    private int _lastInvalidCount;
    private int? _lastTargetId;
    private SessionStatus _status = SessionStatus.FreePlay;
    private double _duration;
    private SessionMetadata? _metadata;
    private EngineFrame? _lastFrame;
    private SessionResult? _lastResult;

    public SimulatedGameProcess Process => process;

    public AimSettings Settings => settings;

    public ITelemetryRecorder Telemetry => telemetry;

    public SessionResult? LastResult
    {
        get { lock (_gate) return _lastResult; }
    }

    public SessionState Session
    {
        get { lock (_gate) return CurrentSession(); }
    }

    /// <summary>
    /// Rebuilds the world from <paramref name="options"/>, clears cache and telemetry and starts a session.
    /// A duration of 0 starts untimed free play. Same options + same input = same session.
    /// </summary>
    public void StartSession(SimulationOptions options, double durationSeconds)
    {
        lock (_gate)
        {
            process.Reset(options);
            cache.Reset();
            _metadata = new SessionMetadata(
                options.Mode, options.Seed, options.Visibility, Math.Max(0, durationSeconds), settings.Enabled,
                new Dictionary<string, double>
                {
                    ["TargetSpeed"] = options.TargetSpeed,
                    ["HeadRadius"] = options.HeadRadius,
                    ["BodyRadius"] = options.BodyRadius,
                    ["MinDistance"] = options.MinDistance,
                    ["MaxDistance"] = options.MaxDistance,
                    ["TargetCount"] = options.EffectiveTargetCount,
                    ["SpawnYawSpread"] = options.SpawnYawSpread,
                    ["SpawnPitchSpread"] = options.SpawnPitchSpread,
                    ["PeekVisibleSeconds"] = options.PeekVisibleSeconds,
                    ["PeekHiddenSeconds"] = options.PeekHiddenSeconds,
                    ["FireIntervalSeconds"] = options.FireIntervalSeconds,
                    ["TickRate"] = options.TickRate,
                    ["AssistFov"] = settings.Fov,
                    ["AssistSmoothing"] = settings.Smoothing,
                    ["AcquireThresholdDegrees"] = settings.AcquireThresholdDegrees,
                });
            telemetry.Reset(_metadata);
            _duration = Math.Max(0, durationSeconds);
            _status = _duration > 0 ? SessionStatus.Running : SessionStatus.FreePlay;
            _lastResult = null;
            _lastFrame = null;
            _lastTargetId = null;
            _lastInvalidCount = 0;
            logger.Information("Session started: {Mode} seed {Seed} {Visibility} {Duration}s assist={Assist}",
                options.Mode, options.Seed, options.Visibility, _duration, settings.Enabled);
        }
    }

    /// <summary>Ends a running session early and produces its result.</summary>
    public void StopSession()
    {
        lock (_gate)
        {
            if (_status == SessionStatus.Running)
            {
                Finish();
            }
        }
    }

    /// <summary>Clears telemetry but keeps the world and session metadata.</summary>
    public void ResetTelemetry()
    {
        lock (_gate)
        {
            telemetry.Reset(_metadata);
        }
    }

    public void QueueTurn(float deltaPitch, float deltaYaw) => process.World.QueueTurn(deltaPitch, deltaYaw);

    public void QueueFire()
    {
        if (Session.Status != SessionStatus.Finished)
        {
            process.World.QueueFire();
        }
    }

    public EngineFrame Tick(double dt)
    {
        lock (_gate)
        {
            // A finished session freezes the world so the result stays consistent.
            if (_status == SessionStatus.Finished && _lastFrame is not null)
            {
                return _lastFrame;
            }

            var total = Stopwatch.StartNew();
            var world = process.World;

            process.Step(dt);
            var shots = world.DrainShotEvents();
            var simMs = total.Elapsed.TotalMilliseconds;

            var read = parser.ReadSnapshot();
            cache.Update(read);
            var readMs = total.Elapsed.TotalMilliseconds - simMs;

            if (!read.HeaderValid)
            {
                logger.Warning("Simulated memory header invalid at tick {Tick}", world.Tick);
            }

            if (read.InvalidCount != _lastInvalidCount)
            {
                logger.Information("Invalid entity records: {Count}", read.InvalidCount);
                _lastInvalidCount = read.InvalidCount;
            }

            // Target analysis always runs; the assist only decides whether to move the camera.
            var local = cache.LocalPlayer;
            var candidates = selector.EvaluateCandidates(local, cache.Players, settings);
            var target = candidates.Count > 0 ? candidates[0] : null;
            var step = aim.Update(local, target, settings, input.IsActivationHeld);
            var targetingMs = total.Elapsed.TotalMilliseconds - simMs - readMs;

            if (step.Applied)
            {
                // The only sink is the simulated world; nothing leaves this process.
                world.ApplyView(step.After);
            }

            if (step.TargetId != _lastTargetId)
            {
                logger.Debug("Target changed {Previous} -> {Current}", _lastTargetId, step.TargetId);
                _lastTargetId = step.TargetId;
            }

            if (telemetry is TelemetryRecorder recorder)
            {
                recorder.AcquireThresholdDegrees = settings.AcquireThresholdDegrees;
            }

            foreach (var shot in shots)
            {
                telemetry.RecordShot(shot);
            }

            var visibleSince = step.TargetId is { } id ? cache.Get(id)?.VisibleSince : null;
            telemetry.Record(new TelemetryFrame(
                read.Time, read.Tick, step.After, step.TargetId,
                step.ErrorBefore, step.ErrorAfter, step.Applied, visibleSince));

            if (_status == SessionStatus.Running && read.Time >= _duration - 1e-9)
            {
                Finish();
            }

            total.Stop();
            _lastFrame = new EngineFrame(
                read.Tick, read.Time, local.Entity, cache.Players, cache.InvalidCount, cache.TrackedCount,
                candidates, target, step, shots, CurrentSession(),
                new FrameMetrics(simMs, readMs, targetingMs, total.Elapsed.TotalMilliseconds));
            return _lastFrame;
        }
    }

    private SessionState CurrentSession() => new(_status, _metadata, process.World.Time, _duration);

    private void Finish()
    {
        _status = SessionStatus.Finished;
        var snapshot = telemetry.GetSnapshot();
        _lastResult = new SessionResult(_metadata, snapshot, SessionReport.Format(_metadata, snapshot.Summary));
        logger.Information("Session finished: {Report}", _lastResult.Report);
    }
}
