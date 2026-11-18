using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AimLab.Telemetry;

/// <summary>Writes telemetry to JSON (summary + trajectory) and CSV (one row per sample).</summary>
public static class TelemetryExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void ExportJson(string path, TelemetrySummary summary, IReadOnlyList<TelemetrySample> samples)
    {
        EnsureDirectory(path);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, new { ExportedAtUtc = DateTime.UtcNow, Summary = summary, Trajectory = samples }, JsonOptions);
    }

    public static void ExportCsv(string path, IReadOnlyList<TelemetrySample> samples)
    {
        EnsureDirectory(path);
        File.WriteAllText(path, ToCsv(samples), Encoding.UTF8);
    }

    public static string ToCsv(IReadOnlyList<TelemetrySample> samples)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("time,tick,pitch,yaw,target_id,angular_error,aim_applied,angular_velocity,angular_acceleration\n");
        foreach (var s in samples)
        {
            sb.Append(s.Time.ToString("F4", ci)).Append(',')
              .Append(s.Tick.ToString(ci)).Append(',')
              .Append(s.Pitch.ToString("F4", ci)).Append(',')
              .Append(s.Yaw.ToString("F4", ci)).Append(',')
              .Append(s.TargetId?.ToString(ci)).Append(',')
              .Append(s.AngularError?.ToString("F4", ci)).Append(',')
              .Append(s.AimApplied ? '1' : '0').Append(',')
              .Append(s.AngularVelocity.ToString("F4", ci)).Append(',')
              .Append(s.AngularAcceleration.ToString("F4", ci)).Append('\n');
        }

        return sb.ToString();
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
