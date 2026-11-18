using AimLab.Mathematics;

namespace AimLab.Telemetry;

public interface ITelemetryRecorder
{
    void Record(in TelemetryFrame frame);

    TelemetrySummary GetSummary();

    IReadOnlyList<TelemetrySample> GetSamples();

    void Reset();
}

/// <summary>
/// Thread-safe recorder. Definitions:
/// <list type="bullet">
/// <item>Acquisition time: from a target first being selected to the angular error dropping under the threshold.</item>
/// <item>Reaction time: from the target becoming visible to the first applied aim movement toward it.</item>
/// <item>Target switch: the selected target changes from one entity to a different one.</item>
/// <item>Tracking accuracy: share of aim-applied ticks, after acquisition, with error under the threshold.</item>
/// </list>
/// </summary>
public sealed class TelemetryRecorder(float acquireThresholdDegrees = 1f, int maxSamples = 200_000) : ITelemetryRecorder
{
    private readonly Lock _gate = new();
    private readonly List<TelemetrySample> _samples = [];
    private readonly List<double> _acquisitionTimes = [];
    private readonly List<double> _reactionTimes = [];

    private TelemetrySample? _previous;
    private int? _lastTargetId;
    private double _targetSelectedAt;
    private bool _acquired;
    private bool _reacted;
    private int _switches;
    private int _acquisitions;
    private double _startTime = double.NaN;
    private double _sumVelocity, _maxVelocity, _sumAbsAccel, _maxAbsAccel;
    private int _trackedTicks, _onTargetTicks;

    public float AcquireThresholdDegrees { get; set; } = acquireThresholdDegrees;

    public void Record(in TelemetryFrame frame)
    {
        lock (_gate)
        {
            if (double.IsNaN(_startTime))
            {
                _startTime = frame.Time;
            }

            float velocity = 0, acceleration = 0;
            if (_previous is { } prev && frame.Time > prev.Time)
            {
                var dt = (float)(frame.Time - prev.Time);
                velocity = AngleMath.AngularDistance(new(prev.Pitch, prev.Yaw), frame.View) / dt;
                acceleration = (velocity - prev.AngularVelocity) / dt;
            }

            TrackTarget(frame);

            var sample = new TelemetrySample(
                frame.Time, frame.Tick, frame.View.Pitch, frame.View.Yaw,
                frame.TargetId, frame.AngularError, frame.AimApplied, velocity, acceleration);

            // shortcut: drops the oldest 10% when full instead of streaming to disk; upgrade if sessions exceed ~50 min at 64 Hz.
            if (_samples.Count >= maxSamples)
            {
                _samples.RemoveRange(0, Math.Max(1, maxSamples / 10));
            }

            _samples.Add(sample);
            _previous = sample;

            _sumVelocity += velocity;
            _maxVelocity = Math.Max(_maxVelocity, velocity);
            _sumAbsAccel += Math.Abs(acceleration);
            _maxAbsAccel = Math.Max(_maxAbsAccel, Math.Abs(acceleration));
        }
    }

    private void TrackTarget(in TelemetryFrame frame)
    {
        if (frame.TargetId != _lastTargetId)
        {
            if (frame.TargetId is not null && _lastTargetId is not null)
            {
                _switches++;
            }

            if (frame.TargetId is not null)
            {
                _acquisitions++;
                _targetSelectedAt = frame.Time;
                _acquired = false;
                _reacted = false;
            }

            _lastTargetId = frame.TargetId;
        }

        if (frame.TargetId is null)
        {
            return;
        }

        if (!_reacted && frame.AimApplied)
        {
            _reacted = true;
            var visibleSince = frame.TargetVisibleSince ?? _targetSelectedAt;
            _reactionTimes.Add(Math.Max(0, frame.Time - visibleSince));
        }

        var onTarget = frame.AngularError is { } e && e <= AcquireThresholdDegrees;
        if (!_acquired && onTarget)
        {
            _acquired = true;
            _acquisitionTimes.Add(frame.Time - _targetSelectedAt);
        }

        if (_acquired && frame.AimApplied)
        {
            _trackedTicks++;
            if (onTarget)
            {
                _onTargetTicks++;
            }
        }
    }

    public TelemetrySummary GetSummary()
    {
        lock (_gate)
        {
            var count = _samples.Count;
            var duration = count > 0 ? _samples[^1].Time - _startTime : 0;
            return new TelemetrySummary(
                duration,
                count,
                _acquisitions,
                Mean(_acquisitionTimes),
                Mean(_reactionTimes),
                _switches,
                duration > 0 ? _switches / duration : 0,
                count > 0 ? _sumVelocity / count : 0,
                _maxVelocity,
                count > 0 ? _sumAbsAccel / count : 0,
                _maxAbsAccel,
                _trackedTicks > 0 ? (double)_onTargetTicks / _trackedTicks : 0,
                [.. _acquisitionTimes],
                [.. _reactionTimes]);
        }
    }

    public IReadOnlyList<TelemetrySample> GetSamples()
    {
        lock (_gate)
        {
            return [.. _samples];
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _samples.Clear();
            _acquisitionTimes.Clear();
            _reactionTimes.Clear();
            _previous = null;
            _lastTargetId = null;
            _acquired = _reacted = false;
            _switches = _acquisitions = _trackedTicks = _onTargetTicks = 0;
            _startTime = double.NaN;
            _sumVelocity = _maxVelocity = _sumAbsAccel = _maxAbsAccel = 0;
        }
    }

    private static double Mean(List<double> values) => values.Count > 0 ? values.Average() : 0;
}
