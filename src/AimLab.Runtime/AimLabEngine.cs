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
    FrameMetrics Metrics);

/// <summary>
/// One deterministic pipeline step: simulate, read simulated memory, parse and cache entities,
/// select a target, compute the aim step, apply it to the simulated world, record telemetry.
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
    private int _lastInvalidCount;
    private int? _lastTargetId;

    public SimulatedGameProcess Process => process;

    public AimSettings Settings => settings;

    public ITelemetryRecorder Telemetry => telemetry;

    public EngineFrame Tick(double dt)
    {
        var total = Stopwatch.StartNew();

        process.Step(dt);
        var simMs = total.Elapsed.TotalMilliseconds;

        var read = parser.ReadSnapshot();
        cache.Update(read);
        var readMs = total.Elapsed.TotalMilliseconds - simMs;

        if (!read.HeaderValid)
        {
            logger.Warning("Simulated memory header invalid at tick {Tick}", process.World.Tick);
        }

        if (read.InvalidCount != _lastInvalidCount)
        {
            logger.Information("Invalid entity records: {Count}", read.InvalidCount);
            _lastInvalidCount = read.InvalidCount;
        }

        var local = cache.LocalPlayer;
        var candidates = selector.EvaluateCandidates(local, cache.Players, settings);
        var target = candidates.Count > 0 ? candidates[0] : null;
        var step = aim.Update(local, target, settings, input.IsActivationHeld);
        var targetingMs = total.Elapsed.TotalMilliseconds - simMs - readMs;

        if (step.Applied)
        {
            // The only sink is the simulated world; nothing leaves this process.
            process.World.ApplyView(step.After);
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

        var visibleSince = step.TargetId is { } id ? cache.Get(id)?.VisibleSince : null;
        telemetry.Record(new TelemetryFrame(
            read.Time, read.Tick, step.After, step.TargetId,
            step.ErrorBefore, step.Applied, visibleSince));

        total.Stop();
        return new EngineFrame(
            read.Tick, read.Time, local.Entity, cache.Players, cache.InvalidCount, cache.TrackedCount,
            candidates, target, step,
            new FrameMetrics(simMs, readMs, targetingMs, total.Elapsed.TotalMilliseconds));
    }
}
