using System.Text.Json;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Telemetry;

namespace AimLab.Tests;

public class TelemetryAndConfigTests
{
    [Fact]
    public void Recorder_ComputesVelocityAccelerationAndSwitches()
    {
        var r = new TelemetryRecorder(acquireThresholdDegrees: 1f);
        r.Record(new TelemetryFrame(0.0, 0, new ViewAngles(0, 0), null, null, false, null));
        r.Record(new TelemetryFrame(0.1, 1, new ViewAngles(0, 10), 5, 8f, true, 0.0));   // react 0.1s, 100 deg/s
        r.Record(new TelemetryFrame(0.2, 2, new ViewAngles(0, 15), 5, 0.5f, true, 0.0));  // acquired after 0.1s
        r.Record(new TelemetryFrame(0.3, 3, new ViewAngles(0, 15), 5, 2f, true, 0.0));    // tracked, off target
        r.Record(new TelemetryFrame(0.4, 4, new ViewAngles(0, 15), 7, 3f, false, null));  // switch

        var samples = r.GetSamples();
        Assert.Equal(100f, samples[1].AngularVelocity, 2);
        Assert.Equal(1000f, samples[1].AngularAcceleration, 1);
        Assert.Equal(50f, samples[2].AngularVelocity, 2);

        var s = r.GetSummary();
        Assert.Equal(5, s.SampleCount);
        Assert.Equal(0.4, s.DurationSeconds, 6);
        Assert.Equal(2, s.TargetAcquisitions);
        Assert.Equal(1, s.TargetSwitches);
        Assert.Equal(2.5, s.TargetSwitchesPerSecond, 6);
        Assert.Equal(0.1, s.MeanReactionTimeSeconds, 6);
        Assert.Equal(0.1, s.MeanAcquisitionTimeSeconds, 6);
        Assert.Equal(0.5, s.TrackingAccuracy, 6);
        Assert.Equal(100, s.MaxAngularVelocity, 2);

        r.Reset();
        Assert.Equal(0, r.GetSummary().SampleCount);
    }

    [Fact]
    public void Recorder_DropsOldestWhenFull()
    {
        var r = new TelemetryRecorder(maxSamples: 100);
        for (var i = 0; i < 250; i++)
        {
            r.Record(new TelemetryFrame(i / 64.0, i, default, null, null, false, null));
        }

        var samples = r.GetSamples();
        Assert.True(samples.Count <= 100);
        Assert.Equal(249, samples[^1].Tick);
    }

    [Fact]
    public void Exporter_WritesJsonAndCsv()
    {
        var r = new TelemetryRecorder();
        r.Record(new TelemetryFrame(0.0, 0, new ViewAngles(1.5f, 2.25f), 3, 0.5f, true, 0));
        r.Record(new TelemetryFrame(0.5, 1, new ViewAngles(1.5f, 3.25f), null, null, false, null));
        var dir = Path.Combine(Path.GetTempPath(), "aimlab-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            var json = Path.Combine(dir, "t.json");
            var csv = Path.Combine(dir, "t.csv");
            TelemetryExporter.ExportJson(json, r.GetSummary(), r.GetSamples());
            TelemetryExporter.ExportCsv(csv, r.GetSamples());

            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            Assert.Equal(2, doc.RootElement.GetProperty("Summary").GetProperty("SampleCount").GetInt32());
            Assert.Equal(2, doc.RootElement.GetProperty("Trajectory").GetArrayLength());

            var lines = File.ReadAllLines(csv);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("time,tick,pitch,yaw", lines[0]);
            Assert.Equal("0.0000,0,1.5000,2.2500,3,0.5000,1,0.0000,0.0000", lines[1]);
            Assert.Equal("0.5000,1,1.5000,3.2500,,,0,2.0000,4.0000", lines[2]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Settings_ClampInvalidValues()
    {
        var s = new AimSettings { Fov = 1000, Smoothing = -5, AcquireThresholdDegrees = float.NaN, ActivationKey = " " };
        Assert.Equal(AimSettings.MaxFov, s.Fov);
        Assert.Equal(AimSettings.MinSmoothing, s.Smoothing);
        Assert.Equal(1f, s.AcquireThresholdDegrees);
        Assert.Equal("LeftShift", s.ActivationKey);
        s.Priority = (TargetPriority)99;
        Assert.Equal(TargetPriority.ClosestToCrosshair, s.Priority);
    }

    [Fact]
    public void SettingsStore_RoundTripsAndFallsBack()
    {
        var path = Path.Combine(Path.GetTempPath(), "aimlab-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var original = new AimSettings { Fov = 42, Smoothing = 12, Priority = TargetPriority.LowestHealth, Region = BodyRegion.Body, VisibilityCheck = false };
            SettingsStore.Save(original, path);
            Assert.Contains("\"LowestHealth\"", File.ReadAllText(path));

            var loaded = SettingsStore.Load(path);
            Assert.Equal(42, loaded.Fov);
            Assert.Equal(12, loaded.Smoothing);
            Assert.Equal(TargetPriority.LowestHealth, loaded.Priority);
            Assert.Equal(BodyRegion.Body, loaded.Region);
            Assert.False(loaded.VisibilityCheck);

            File.WriteAllText(path, "{ not json");
            Assert.Equal(30, SettingsStore.Load(path).Fov);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
