using AimLab.Core;

namespace AimLab.Telemetry;

/// <summary>
/// One engine tick. <see cref="ErrorBefore"/>: error of the view after manual input but before the assist step.
/// <see cref="ErrorAfter"/>: error of the recorded view (<see cref="Pitch"/>/<see cref="Yaw"/>) at the end of the tick.
/// Velocity is null on the first sample; acceleration is null until two velocities exist.
/// </summary>
public sealed record TelemetrySample(
    double Time,
    long Tick,
    float Pitch,
    float Yaw,
    int? TargetId,
    float? ErrorBefore,
    float? ErrorAfter,
    bool AimApplied,
    float? AngularVelocity,
    float? AngularAcceleration);

/// <summary>Motion statistics over a set of samples. Means divide by the matching count.</summary>
public sealed record TelemetryStats(
    int SampleCount,
    double DurationSeconds,
    int VelocitySampleCount,
    double MeanAngularVelocity,
    double MaxAngularVelocity,
    int AccelerationSampleCount,
    double MeanAbsAngularAcceleration,
    double MaxAbsAngularAcceleration)
{
    public static TelemetryStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>Shot results for the whole session.</summary>
public sealed record ShotStats(
    int Shots,
    int Hits,
    int Misses,
    int Headshots,
    int Kills,
    double Accuracy,
    double HeadshotRate,
    double? FirstHitAtSeconds,
    double MeanTimeToFirstHitSeconds,
    double MeanSwitchTimeSeconds,
    IReadOnlyList<double> TimesToFirstHit,
    IReadOnlyList<double> SwitchTimes);

/// <summary>
/// <see cref="Session"/> covers every sample recorded since the last reset; <see cref="Window"/> covers only
/// the samples still kept in memory (and exported). Target and shot metrics are always session-wide.
/// </summary>
public sealed record TelemetrySummary(
    TelemetryStats Session,
    TelemetryStats Window,
    int TargetAcquisitions,
    double MeanAcquisitionTimeSeconds,
    double MeanReactionTimeSeconds,
    int TargetSwitches,
    double TargetSwitchesPerSecond,
    int TargetTicks,
    int TrackingTicks,
    double TrackingAccuracy,
    double AssistActiveShare,
    ShotStats Shots,
    IReadOnlyList<double> AcquisitionTimes,
    IReadOnlyList<double> ReactionTimes);

/// <summary>Describes how a session was configured, so manual and assisted runs can be compared.</summary>
public sealed record SessionMetadata(
    TrainingMode Mode,
    int Seed,
    VisibilityScenario Visibility,
    double PlannedDurationSeconds,
    bool AssistEnabled,
    IReadOnlyDictionary<string, double> Parameters);

/// <summary>Summary, samples and shots taken under one lock, so they always agree.</summary>
public sealed record TelemetrySnapshot(
    SessionMetadata? Metadata,
    TelemetrySummary Summary,
    IReadOnlyList<TelemetrySample> Samples,
    IReadOnlyList<ShotEvent> Shots);

/// <summary>Input for <see cref="ITelemetryRecorder.Record"/>.</summary>
public readonly record struct TelemetryFrame(
    double Time,
    long Tick,
    ViewAngles View,
    int? TargetId,
    float? ErrorBefore,
    float? ErrorAfter,
    bool AimApplied,
    double? TargetVisibleSince);
