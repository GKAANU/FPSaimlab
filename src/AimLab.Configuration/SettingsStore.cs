using System.Text.Json;
using System.Text.Json.Serialization;

namespace AimLab.Configuration;

/// <summary>Loads and saves <see cref="AimSettings"/> as JSON.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Returns defaults when the file is missing or unreadable.</summary>
    public static AimSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AimSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AimSettings>(File.ReadAllText(path), JsonOptions) ?? new AimSettings();
        }
        catch (JsonException)
        {
            return new AimSettings();
        }
    }

    public static void Save(AimSettings settings, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    /// <summary>Copies every setting from <paramref name="source"/> onto an existing instance (keeps UI bindings alive).</summary>
    public static void CopyTo(AimSettings source, AimSettings target)
    {
        target.Enabled = source.Enabled;
        target.Fov = source.Fov;
        target.Smoothing = source.Smoothing;
        target.Priority = source.Priority;
        target.Region = source.Region;
        target.VisibilityCheck = source.VisibilityCheck;
        target.RequireActivationKey = source.RequireActivationKey;
        target.ActivationKey = source.ActivationKey;
        target.AcquireThresholdDegrees = source.AcquireThresholdDegrees;
    }
}
