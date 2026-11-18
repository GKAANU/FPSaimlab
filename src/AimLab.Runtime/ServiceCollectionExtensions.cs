using AimLab.AimAssist;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Entities;
using AimLab.Memory;
using AimLab.Simulation;
using AimLab.Targeting;
using AimLab.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AimLab.Runtime;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the full simulation + aim assist pipeline as singletons.</summary>
    public static IServiceCollection AddAimLab(
        this IServiceCollection services,
        SimulationOptions? simulation = null,
        AimSettings? settings = null,
        ILogger? logger = null)
    {
        services.AddSingleton(simulation ?? new SimulationOptions());
        services.AddSingleton(settings ?? new AimSettings());
        services.AddSingleton(logger ?? Log.Logger);

        services.AddSingleton<SimulatedGameProcess>();
        services.AddSingleton<IMemoryReader>(sp => sp.GetRequiredService<SimulatedGameProcess>().Memory);
        services.AddSingleton<ManualInputState>();
        services.AddSingleton<IInputState>(sp => sp.GetRequiredService<ManualInputState>());

        services.AddSingleton<IEntityParser, MemoryEntityParser>();
        services.AddSingleton<IEntityCache, EntityCache>();
        services.AddSingleton<ITargetSelector, TargetSelector>();
        services.AddSingleton<IAimAssistController, AimAssistController>();
        services.AddSingleton<ITelemetryRecorder>(sp =>
            new TelemetryRecorder(sp.GetRequiredService<AimSettings>().AcquireThresholdDegrees));

        services.AddSingleton<AimLabEngine>();
        services.AddSingleton<SimulationHost>();
        return services;
    }
}
