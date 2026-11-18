using System.Globalization;
using System.Text;

namespace AimLab.Telemetry;

/// <summary>Human-readable end-of-session summary.</summary>
public static class SessionReport
{
    public static string Format(SessionMetadata? metadata, TelemetrySummary s)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        if (metadata is not null)
        {
            sb.AppendLine(string.Create(ci, $"Mode: {metadata.Mode} · Visibility: {metadata.Visibility} · Seed: {metadata.Seed}"));
            sb.AppendLine(metadata.AssistEnabled ? "Aim assist: ON (assisted session)" : "Aim assist: OFF (manual session)");
        }

        var shots = s.Shots;
        sb.AppendLine(string.Create(ci, $"Duration: {s.Session.DurationSeconds:F1} s · {s.Session.SampleCount} ticks"));
        sb.AppendLine(string.Create(ci, $"Shots: {shots.Shots} · Hits: {shots.Hits} · Misses: {shots.Misses} · Accuracy: {shots.Accuracy:P1}"));
        sb.AppendLine(string.Create(ci, $"Headshots: {shots.Headshots} ({shots.HeadshotRate:P0} of hits) · Eliminations: {shots.Kills}"));
        sb.AppendLine(shots.FirstHitAtSeconds is { } first
            ? string.Create(ci, $"First hit at: {first:F2} s · Mean time to first hit per target: {shots.MeanTimeToFirstHitSeconds * 1000:F0} ms")
            : "First hit: none");
        sb.AppendLine(string.Create(ci, $"Mean target switch time (hit to hit): {shots.MeanSwitchTimeSeconds * 1000:F0} ms over {shots.SwitchTimes.Count} switches"));
        sb.AppendLine(string.Create(ci, $"Acquisitions: {s.TargetAcquisitions} · Mean acquisition: {s.MeanAcquisitionTimeSeconds * 1000:F0} ms · Mean reaction: {s.MeanReactionTimeSeconds * 1000:F0} ms"));
        sb.AppendLine(string.Create(ci, $"Tracking accuracy: {s.TrackingAccuracy:P1} over {s.TrackingTicks} ticks · Assist active on {s.AssistActiveShare:P0} of target ticks"));
        sb.Append(string.Create(ci, $"Angular velocity mean/max: {s.Session.MeanAngularVelocity:F1} / {s.Session.MaxAngularVelocity:F1} °/s"));
        return sb.ToString();
    }
}
