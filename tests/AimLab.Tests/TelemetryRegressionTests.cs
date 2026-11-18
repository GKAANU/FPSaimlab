using System.Text.Json;
using AimLab.Core;
using AimLab.Telemetry;

namespace AimLab.Tests;

public class TelemetryRegressionTests
{
    private static TelemetryFrame F(double t, float yaw, int? target, float? before, float? after, bool applied = false, double? visible = null) =>
        new(t, (long)Math.Round(t * 10), new ViewAngles(0, yaw), target, before, after, applied, visible);

    [Fact]
    public void Velocity_IsNullOnFirstSample_AccelerationNeedsTwoVelocities()
    {
        var r = new TelemetryRecorder();
        r.Record(F(0.0, 0, null, null, null));
        r.Record(F(0.1, 10, null, null, null));
        r.Record(F(0.2, 15, null, null, null));

        var s = r.GetSamples();
        Assert.Null(s[0].AngularVelocity);
        Assert.Null(s[0].AngularAcceleration);
        Assert.Equal(100f, s[1].AngularVelocity!.Value, 2);
        Assert.Null(s[1].AngularAcceleration);
        Assert.Equal(50f, s[2].AngularVelocity!.Value, 2);
        Assert.Equal(-500f, s[2].AngularAcceleration!.Value, 1);

        var stats = r.GetSummary().Session;
        Assert.Equal(3, stats.SampleCount);
        Assert.Equal(2, stats.VelocitySampleCount);
        Assert.Equal(75, stats.MeanAngularVelocity, 3);
        Assert.Equal(1, stats.AccelerationSampleCount);
    }

    [Fact]
    public void ManualTracking_CountsWithoutAssist()
    {
        // Assist never applied: acquisition, reaction and tracking must still be measured from ErrorAfter.
        var r = new TelemetryRecorder(acquireThresholdDegrees: 1f);
        r.Record(F(0.0, 0, 5, 10f, 10f, visible: 0.0));
        r.Record(F(0.1, 5, 5, 5f, 5f, visible: 0.0));     // camera moved, error dropped: reaction 0.1 s
        r.Record(F(0.2, 9.5f, 5, 0.5f, 0.5f, visible: 0.0)); // acquired after 0.2 s
        r.Record(F(0.3, 9.5f, 5, 0.8f, 0.8f, visible: 0.0)); // on target
        r.Record(F(0.4, 9.5f, 5, 3f, 3f, visible: 0.0));     // off target

        var s = r.GetSummary();
        Assert.Equal(1, s.TargetAcquisitions);
        Assert.Equal(0.2, s.MeanAcquisitionTimeSeconds, 6);
        Assert.Equal(0.1, s.MeanReactionTimeSeconds, 6);
        Assert.Equal(3, s.TrackingTicks);
        Assert.Equal(2.0 / 3.0, s.TrackingAccuracy, 6);
        Assert.Equal(0, s.AssistActiveShare);
        Assert.All(r.GetSamples(), x => Assert.False(x.AimApplied));
    }

    [Fact]
    public void InstantSnap_IsAcquiredInTheSelectingTick()
    {
        var r = new TelemetryRecorder(acquireThresholdDegrees: 1f);
        r.Record(F(0.0, 0, null, null, null));
        r.Record(F(0.1, 30, 7, 30f, 0f, applied: true, visible: 0.05));

        var s = r.GetSummary();
        Assert.Equal([0.0], s.AcquisitionTimes);
        Assert.Equal(0.05, s.MeanReactionTimeSeconds, 6);
        Assert.Equal(1.0, s.TrackingAccuracy);
        Assert.Equal(1.0, s.AssistActiveShare);
        var sample = r.GetSamples()[1];
        Assert.Equal(30f, sample.ErrorBefore);
        Assert.Equal(0f, sample.ErrorAfter);
    }

    [Fact]
    public void FullBuffer_KeepsSessionStatsExactAndWindowConsistent()
    {
        const int max = 100;
        var r = new TelemetryRecorder(maxSamples: max);
        // Constant 64 deg/s: mean must stay 64 whatever is trimmed.
        for (var i = 0; i < 1000; i++)
        {
            r.Record(new TelemetryFrame(i / 64.0, i, new ViewAngles(0, AimLab.Mathematics.AngleMath.NormalizeYaw(i)), null, null, null, false, null));
        }

        var snap = r.GetSnapshot();
        var session = snap.Summary.Session;
        var window = snap.Summary.Window;

        Assert.Equal(1000, session.SampleCount);
        Assert.Equal(999, session.VelocitySampleCount);
        Assert.Equal(999 / 64.0, session.DurationSeconds, 6);
        Assert.Equal(64, session.MeanAngularVelocity, 2);

        Assert.True(snap.Samples.Count <= max);
        Assert.Equal(snap.Samples.Count, window.SampleCount);
        Assert.Equal(snap.Samples[^1].Time - snap.Samples[0].Time, window.DurationSeconds, 9);
        Assert.Equal(64, window.MeanAngularVelocity, 2);
        Assert.Equal(999, snap.Samples[^1].Tick);
    }

    [Fact]
    public void Shots_MeasureHitsMissesFirstHitAndSwitchTime()
    {
        var r = new TelemetryRecorder();
        r.RecordShot(new ShotEvent(1.0, 64, default, null, 0, false, false, null, false));
        r.RecordShot(new ShotEvent(1.5, 96, default, 2, 1, true, true, 1.2, true));
        r.RecordShot(new ShotEvent(2.0, 128, default, 3, 1, false, true, 0.5, true));
        r.RecordShot(new ShotEvent(2.1, 134, default, 3, 1, false, false, 0.5, false)); // same target, no switch
        r.RecordShot(new ShotEvent(2.6, 166, default, 2, 2, false, true, 2.4, true));   // respawned target 2 = new instance

        var shots = r.GetSummary().Shots;
        Assert.Equal(5, shots.Shots);
        Assert.Equal(4, shots.Hits);
        Assert.Equal(1, shots.Misses);
        Assert.Equal(1, shots.Headshots);
        Assert.Equal(3, shots.Kills);
        Assert.Equal(0.8, shots.Accuracy, 6);
        Assert.Equal(1.5, shots.FirstHitAtSeconds);
        Assert.Equal([0.3, 1.5, 0.2], shots.TimesToFirstHit.Select(x => Math.Round(x, 6)));
        Assert.Equal([0.5, 0.5], shots.SwitchTimes.Select(x => Math.Round(x, 6)));
    }

    [Fact]
    public void Reset_ClearsEverythingAndStoresMetadata()
    {
        var r = new TelemetryRecorder();
        r.Record(F(0.0, 0, 5, 3f, 3f));
        r.Record(F(0.1, 4, 5, 1f, 0.5f, applied: true));
        r.RecordShot(new ShotEvent(0.1, 1, default, 5, 1, true, true, 0, true));

        var meta = new SessionMetadata(TrainingMode.Flick, 9, VisibilityScenario.Peek, 30, false, new Dictionary<string, double>());
        r.Reset(meta);

        var snap = r.GetSnapshot();
        Assert.Same(meta, snap.Metadata);
        Assert.Empty(snap.Samples);
        Assert.Empty(snap.Shots);
        Assert.Equal(0, snap.Summary.Session.SampleCount);
        Assert.Equal(0, snap.Summary.TargetAcquisitions);
        Assert.Equal(0, snap.Summary.Shots.Shots);
        Assert.Null(snap.Summary.Shots.FirstHitAtSeconds);

        // First sample after reset has no velocity relative to pre-reset data.
        r.Record(F(5.0, 90, null, null, null));
        Assert.Null(r.GetSamples()[0].AngularVelocity);
    }

    [Fact]
    public void Exporter_WritesSnapshotJsonAndCsv()
    {
        var r = new TelemetryRecorder();
        r.Reset(new SessionMetadata(TrainingMode.Tracking, 1, VisibilityScenario.AlwaysVisible, 10, true, new Dictionary<string, double> { ["TargetSpeed"] = 150 }));
        r.Record(new TelemetryFrame(0.0, 0, new ViewAngles(1.5f, 2.25f), 3, 0.9f, 0.5f, true, 0));
        r.Record(new TelemetryFrame(0.5, 1, new ViewAngles(1.5f, 3.25f), null, null, null, false, null));
        r.RecordShot(new ShotEvent(0.5, 1, default, null, 0, false, false, null, false));
        var dir = Path.Combine(Path.GetTempPath(), "aimlab-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            var json = Path.Combine(dir, "t.json");
            var csv = Path.Combine(dir, "t.csv");
            var snap = r.GetSnapshot();
            TelemetryExporter.ExportJson(json, snap);
            TelemetryExporter.ExportCsv(csv, snap.Samples);

            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            var root = doc.RootElement;
            Assert.Equal("Tracking", root.GetProperty("Metadata").GetProperty("Mode").GetString());
            Assert.Equal(2, root.GetProperty("Summary").GetProperty("Window").GetProperty("SampleCount").GetInt32());
            Assert.Equal(2, root.GetProperty("Trajectory").GetArrayLength());
            Assert.Equal(1, root.GetProperty("Shots").GetArrayLength());

            var lines = File.ReadAllLines(csv);
            Assert.Equal(3, lines.Length);
            Assert.Equal("time,tick,pitch,yaw,target_id,error_before,error_after,aim_applied,angular_velocity,angular_acceleration", lines[0]);
            Assert.Equal("0.0000,0,1.5000,2.2500,3,0.9000,0.5000,1,,", lines[1]);
            Assert.Equal("0.5000,1,1.5000,3.2500,,,,0,2.0000,", lines[2]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SessionReport_ContainsKeyResults()
    {
        var r = new TelemetryRecorder();
        r.RecordShot(new ShotEvent(1, 1, default, 2, 1, true, true, 0.5, true));
        var text = SessionReport.Format(
            new SessionMetadata(TrainingMode.Flick, 42, VisibilityScenario.AlwaysVisible, 60, false, new Dictionary<string, double>()),
            r.GetSummary());
        Assert.Contains("Mode: Flick", text);
        Assert.Contains("manual session", text);
        Assert.Contains("Shots: 1 · Hits: 1", text);
    }
}
