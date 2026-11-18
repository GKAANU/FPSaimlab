using System.Text.Json;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Telemetry;

namespace AimLab.Tests;

public class TelemetryAndConfigTests
{
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
