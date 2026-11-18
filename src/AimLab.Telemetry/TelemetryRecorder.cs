using AimLab.Core;
using AimLab.Mathematics;

namespace AimLab.Telemetry;

public interface ITelemetryRecorder
{
    void Record(in TelemetryFrame frame);

    void RecordShot(ShotEvent shot);

    TelemetrySnapshot GetSnapshot();

    TelemetrySummary GetSummary();

    IReadOnlyList<TelemetrySample> GetSamples();

    void Reset(SessionMetadata? metadata = null);
}

/// <summary>
/// Thread-safe recorder. All target metrics use <see cref="TelemetrySample.ErrorAfter"/> (the error of the
/// recorded view), so manual and assisted ticks are measured the same way:
/// <list type="bullet">
/// <item>Acquisition time: from a target being selected to the first tick whose ErrorAfter is within the threshold (0 when reached in the selecting tick).</item>
/// <item>Reaction time: from the target becoming visible (or selected, if unknown) to the first tick in which the camera moved and ErrorAfter dropped below the previous error of that target.</item>
/// <item>Target switch: the selected target changes from one entity to a different one.</item>
/// <item>Tracking accuracy: share of ticks after acquisition (same target) with ErrorAfter within the threshold, whether or not the assist was active.</item>
/// </list>
/// </summary>
public sealed class TelemetryRecorder(float acquireThresholdDegrees = 1f, int maxSamples = 200_000) : ITelemetryRecorder
{
    private const float MovementEpsilonDegrees = 1e-4f;

    private readonly Lock _gate = new();
    private readonly List<TelemetrySample> _samples = [];
    private readonly List<ShotEvent> _shots = [];
    private readonly List<double> _acquisitionTimes = [];
    private readonly List<double> _reactionTimes = [];
    private readonly List<double> _timesToFirstHit = [];
    private readonly List<double> _switchTimes = [];

    private SessionMetadata? _metadata;
    private TelemetrySample? _previous;
    private int? _lastTargetId;
    private double _targetSelectedAt;
    private float? _referenceError;
    private bool _acquired;
    private bool _reacted;
    private int _switches;
    private int _acquisitions;
    private int _targetTicks, _assistTicks, _trackingTicks, _onTargetTicks;
    private Accumulator _session;

    private int _shotCount, _hits, _headshots, _kills;
    private double? _firstHitAt;
    private (int Id, int Life)? _lastHitKey;
    private double _lastHitTime;

    public float AcquireThresholdDegrees { get; set; } = acquireThresholdDegrees;

    public void Record(in TelemetryFrame frame)
    {
        lock (_gate)
        {
            float? velocity = null, acceleration = null;
            var moved = false;
            if (_previous is { } prev)
            {
                var step = AngleMath.AngularDistance(new ViewAngles(prev.Pitch, prev.Yaw), frame.View);
                moved = step > MovementEpsilonDegrees;
                if (frame.Time > prev.Time)
                {
                    var dt = (float)(frame.Time - prev.Time);
                    velocity = step / dt;
                    if (prev.AngularVelocity is { } pv)
                    {
                        acceleration = (velocity - pv) / dt;
                    }
                }
            }

            TrackTarget(frame, moved);

            var sample = new TelemetrySample(
                frame.Time, frame.Tick, frame.View.Pitch, frame.View.Yaw, frame.TargetId,
                frame.ErrorBefore, frame.ErrorAfter, frame.AimApplied, velocity, acceleration);

            // shortcut: drops the oldest 10% of the window when full instead of streaming to disk; session stats stay exact.
            if (_samples.Count >= maxSamples)
            {
                _samples.RemoveRange(0, Math.Max(1, maxSamples / 10));
            }

            _samples.Add(sample);
            _previous = sample;
            _session.Add(sample);
        }
    }

    public void RecordShot(ShotEvent shot)
    {
        lock (_gate)
        {
            _shots.Add(shot);
            _shotCount++;
            if (shot.TargetId is not { } id)
            {
                return;
            }

            _hits++;
            if (shot.Headshot) _headshots++;
            if (shot.Killed) _kills++;
            _firstHitAt ??= shot.Time;

            if (shot.FirstHitOnTargetLife && shot.TargetFirstVisibleAt is { } visibleAt)
            {
                _timesToFirstHit.Add(Math.Max(0, shot.Time - visibleAt));
            }

            var key = (id, shot.TargetLife);
            if (_lastHitKey is { } last && last != key)
            {
                _switchTimes.Add(shot.Time - _lastHitTime);
            }

            _lastHitKey = key;
            _lastHitTime = shot.Time;
        }
    }

    private void TrackTarget(in TelemetryFrame frame, bool moved)
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
                _referenceError = frame.ErrorBefore;
            }

            _lastTargetId = frame.TargetId;
        }

        if (frame.TargetId is null)
        {
            return;
        }

        _targetTicks++;
        if (frame.AimApplied)
        {
            _assistTicks++;
        }

        var error = frame.ErrorAfter;
        if (!_reacted && moved && error is { } e && _referenceError is { } reference && e < reference)
        {
            _reacted = true;
            _reactionTimes.Add(Math.Max(0, frame.Time - (frame.TargetVisibleSince ?? _targetSelectedAt)));
        }

        var onTarget = error is { } err && err <= AcquireThresholdDegrees;
        if (!_acquired && onTarget)
        {
            _acquired = true;
            _acquisitionTimes.Add(frame.Time - _targetSelectedAt);
        }

        if (_acquired)
        {
            _trackingTicks++;
            if (onTarget)
            {
                _onTargetTicks++;
            }
        }

        _referenceError = error;
    }

    public TelemetrySnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var window = new Accumulator();
            foreach (var s in _samples)
            {
                window.Add(s);
            }

            var sessionStats = _session.ToStats();
            var summary = new TelemetrySummary(
                sessionStats,
                window.ToStats(),
                _acquisitions,
                Mean(_acquisitionTimes),
                Mean(_reactionTimes),
                _switches,
                sessionStats.DurationSeconds > 0 ? _switches / sessionStats.DurationSeconds : 0,
                _targetTicks,
                _trackingTicks,
                _trackingTicks > 0 ? (double)_onTargetTicks / _trackingTicks : 0,
                _targetTicks > 0 ? (double)_assistTicks / _targetTicks : 0,
                new ShotStats(
                    _shotCount, _hits, _shotCount - _hits, _headshots, _kills,
                    _shotCount > 0 ? (double)_hits / _shotCount : 0,
                    _hits > 0 ? (double)_headshots / _hits : 0,
                    _firstHitAt,
                    Mean(_timesToFirstHit),
                    Mean(_switchTimes),
                    [.. _timesToFirstHit],
                    [.. _switchTimes]),
                [.. _acquisitionTimes],
                [.. _reactionTimes]);

            return new TelemetrySnapshot(_metadata, summary, [.. _samples], [.. _shots]);
        }
    }

    public TelemetrySummary GetSummary() => GetSnapshot().Summary;

    public IReadOnlyList<TelemetrySample> GetSamples()
    {
        lock (_gate)
        {
            return [.. _samples];
        }
    }

    public void Reset(SessionMetadata? metadata = null)
    {
        lock (_gate)
        {
            _metadata = metadata;
            _samples.Clear();
            _shots.Clear();
            _acquisitionTimes.Clear();
            _reactionTimes.Clear();
            _timesToFirstHit.Clear();
            _switchTimes.Clear();
            _previous = null;
            _lastTargetId = null;
            _referenceError = null;
            _acquired = _reacted = false;
            _switches = _acquisitions = 0;
            _targetTicks = _assistTicks = _trackingTicks = _onTargetTicks = 0;
            _session = default;
            _shotCount = _hits = _headshots = _kills = 0;
            _firstHitAt = null;
            _lastHitKey = null;
            _lastHitTime = 0;
        }
    }

    private static double Mean(List<double> values) => values.Count > 0 ? values.Average() : 0;

    /// <summary>Running sums for one set of samples; count, duration and means always come from the same samples.</summary>
    private struct Accumulator
    {
        private int _count, _velocityCount, _accelerationCount;
        private double _first, _last, _sumVelocity, _maxVelocity, _sumAbsAccel, _maxAbsAccel;

        public void Add(TelemetrySample s)
        {
            if (_count == 0)
            {
                _first = s.Time;
            }

            _count++;
            _last = s.Time;
            if (s.AngularVelocity is { } v)
            {
                _velocityCount++;
                _sumVelocity += v;
                _maxVelocity = Math.Max(_maxVelocity, v);
            }

            if (s.AngularAcceleration is { } a)
            {
                _accelerationCount++;
                _sumAbsAccel += Math.Abs(a);
                _maxAbsAccel = Math.Max(_maxAbsAccel, Math.Abs(a));
            }
        }

        public readonly TelemetryStats ToStats() => new(
            _count,
            _count > 0 ? _last - _first : 0,
            _velocityCount,
            _velocityCount > 0 ? _sumVelocity / _velocityCount : 0,
            _maxVelocity,
            _accelerationCount,
            _accelerationCount > 0 ? _sumAbsAccel / _accelerationCount : 0,
            _maxAbsAccel);
    }
}
