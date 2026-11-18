using System.Diagnostics;
using Serilog;

namespace AimLab.Runtime;

/// <summary>Runs <see cref="AimLabEngine.Tick"/> on a background thread at a fixed tick rate.</summary>
public sealed class SimulationHost(AimLabEngine engine, ILogger logger) : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile EngineFrame? _latest;
    private double _measuredTickRate;

    public AimLabEngine Engine => engine;

    public EngineFrame? LatestFrame => _latest;

    public double MeasuredTickRate => Volatile.Read(ref _measuredTickRate);

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
        logger.Information("Simulation host started at {TickRate} Hz", engine.Process.Options.TickRate);
    }

    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
        }

        _cts.Dispose();
        _cts = null;
        logger.Information("Simulation host stopped");
    }

    private async Task RunAsync(CancellationToken token)
    {
        var dt = 1.0 / Math.Max(1, engine.Process.Options.TickRate);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(dt));
        var rateClock = Stopwatch.StartNew();
        var ticks = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    _latest = engine.Tick(dt);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Engine tick failed");
                }

                ticks++;
                if (rateClock.Elapsed.TotalSeconds >= 1)
                {
                    Volatile.Write(ref _measuredTickRate, ticks / rateClock.Elapsed.TotalSeconds);
                    ticks = 0;
                    rateClock.Restart();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose() => Stop();
}
