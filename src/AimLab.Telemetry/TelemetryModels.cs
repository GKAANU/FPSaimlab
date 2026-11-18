namespace AimLab.Telemetry;

/// <summary>One engine tick of aim data.</summary>
public sealed record TelemetrySample(
    double Time,
    long Tick,
    float Pitch,
    float Yaw,
    int? TargetId,
    float? AngularError,
    bool AimApplied,
    float AngularVelocity,
    float AngularAcceleration);

/// <summary>Aggregated metrics for anti-cheat research.</summary>
public sealed record TelemetrySummary(
    double DurationSeconds,
    int SampleCount,
    int TargetAcquisitions,
    double MeanAcquisitionTimeSeconds,
    double MeanReactionTimeSeconds,
    int TargetSwitches,
    double TargetSwitchesPerSecond,
    double MeanAngularVelocity,
    double MaxAngularVelocity,
    double MeanAbsAngularAcceleration,
    double MaxAbsAngularAcceleration,
    double TrackingAccuracy,
    IReadOnlyList<double> AcquisitionTimes,
    IReadOnlyList<double> ReactionTimes);

/// <summary>Input for <see cref="ITelemetryRecorder.Record"/>.</summary>
public readonly record struct TelemetryFrame(
    double Time,
    long Tick,
    AimLab.Core.ViewAngles View,
    int? TargetId,
    float? AngularError,
    bool AimApplied,
    double? TargetVisibleSince);
