using System.IO;
using System.Windows;
using AimLab.Configuration;
using AimLab.Runtime;
using AimLab.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AimLab.UI;

public partial class App : Application
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AimLab");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(DataDirectory, "logs", "aimlab-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show(args.Exception.Message, "AimLab", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var settings = SettingsStore.Load(SettingsPath);
        _services = new ServiceCollection()
            .AddAimLab(new SimulationOptions(), settings, Log.Logger)
            .AddSingleton<MainViewModel>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();

        Log.Information("AimLab starting; settings from {Path}", SettingsPath);
        _services.GetRequiredService<SimulationHost>().Start();
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
