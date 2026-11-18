using System.Numerics;
using AimLab.Core;
using AimLab.Mathematics;

namespace AimLab.Simulation;

/// <summary>
/// Deterministic top-down arena: bots wander between waypoints, take random damage, and respawn.
/// The local player is a fixed camera whose view is the only thing the aim assist can change.
/// </summary>
public sealed class GameWorld : IAimSink
{
    private readonly SimulationOptions _options;
    private readonly Random _random;
    private readonly List<SimPlayer> _players = [];
    private readonly Lock _viewGate = new();
    private ViewAngles _pendingTurn;

    public GameWorld(SimulationOptions options)
    {
        _options = options;
        _random = new Random(options.Seed);
        var h = options.ArenaHalfSize;

        Walls =
        [
            new Aabb(new(-h * 0.45f, -h * 0.55f, 0), new(-h * 0.35f, -h * 0.05f, 200)),
            new Aabb(new(-h * 0.45f, h * 0.10f, 0), new(-h * 0.35f, h * 0.55f, 200)),
            new Aabb(new(h * 0.05f, -h * 0.15f, 0), new(h * 0.25f, h * 0.15f, 200)),
            new Aabb(new(h * 0.45f, -h * 0.80f, 0), new(h * 0.55f, -h * 0.30f, 200)),
            new Aabb(new(h * 0.45f, h * 0.30f, 0), new(h * 0.55f, h * 0.80f, 200)),
        ];

        LocalPlayer = new SimPlayer(1, Team.Defenders)
        {
            Origin = new Vector3(-h * 0.85f, 0, 0),
            View = new ViewAngles(0, 0),
        };
        _players.Add(LocalPlayer);

        var nextId = 2;
        for (var i = 0; i < options.BotsPerTeam - 1; i++)
        {
            _players.Add(Spawn(new SimPlayer(nextId++, Team.Defenders)));
        }

        for (var i = 0; i < options.BotsPerTeam; i++)
        {
            _players.Add(Spawn(new SimPlayer(nextId++, Team.Attackers)));
        }

        UpdateVisibility();
    }

    public IReadOnlyList<Aabb> Walls { get; }

    public IReadOnlyList<SimPlayer> Players => _players;

    public SimPlayer LocalPlayer { get; }

    public float ArenaHalfSize => _options.ArenaHalfSize;

    public double Time { get; private set; }

    public long Tick { get; private set; }

    public ViewAngles CurrentView
    {
        get
        {
            lock (_viewGate)
            {
                return LocalPlayer.View;
            }
        }
    }

    public void ApplyView(ViewAngles view)
    {
        lock (_viewGate)
        {
            LocalPlayer.View = AngleMath.Normalize(view);
        }
    }

    /// <summary>Queues a manual camera rotation (from the UI); applied at the start of the next step.</summary>
    public void QueueTurn(float deltaPitch, float deltaYaw)
    {
        lock (_viewGate)
        {
            _pendingTurn = new ViewAngles(_pendingTurn.Pitch + deltaPitch, _pendingTurn.Yaw + deltaYaw);
        }
    }

    public void Step(double dt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dt);
        Time += dt;
        Tick++;

        lock (_viewGate)
        {
            var v = LocalPlayer.View;
            LocalPlayer.View = AngleMath.Normalize(new ViewAngles(v.Pitch + _pendingTurn.Pitch, v.Yaw + _pendingTurn.Yaw));
            _pendingTurn = default;
        }

        foreach (var bot in _players)
        {
            if (ReferenceEquals(bot, LocalPlayer))
            {
                continue;
            }

            if (!bot.IsAlive)
            {
                if (bot.RespawnAt is { } at && Time >= at)
                {
                    Spawn(bot);
                }

                continue;
            }

            Move(bot, (float)dt);

            if (_random.NextDouble() < _options.DamageEventsPerSecond * dt)
            {
                bot.Health = Math.Max(0, bot.Health - _random.Next(10, 45));
                if (!bot.IsAlive)
                {
                    bot.RespawnAt = Time + _options.RespawnDelaySeconds;
                }
            }
        }

        UpdateVisibility();
    }

    /// <summary>True when no wall blocks the segment between two points.</summary>
    public bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        foreach (var wall in Walls)
        {
            if (wall.IntersectsSegment(from, to))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateVisibility()
    {
        var eye = LocalPlayer.Head;
        foreach (var p in _players)
        {
            p.IsVisibleToLocal = !ReferenceEquals(p, LocalPlayer) && p.IsAlive &&
                (HasLineOfSight(eye, p.Head) || HasLineOfSight(eye, p.Body));
        }
    }

    private void Move(SimPlayer bot, float dt)
    {
        var toWaypoint = bot.Waypoint - bot.Origin;
        var distance = toWaypoint.Length();
        if (distance < 10f)
        {
            bot.Waypoint = RandomOpenPoint();
            return;
        }

        var step = Vector3.Normalize(toWaypoint) * MathF.Min(_options.BotSpeed * dt, distance);
        var next = bot.Origin + step;
        if (IsBlocked(next))
        {
            bot.Waypoint = RandomOpenPoint();
            return;
        }

        bot.Origin = next;
        bot.View = new ViewAngles(0, AngleMath.CalculateAngles(bot.Origin, bot.Waypoint).Yaw);
    }

    private SimPlayer Spawn(SimPlayer bot)
    {
        bot.Health = 100;
        bot.RespawnAt = null;
        bot.Origin = RandomOpenPoint();
        bot.Waypoint = RandomOpenPoint();
        return bot;
    }

    private Vector3 RandomOpenPoint()
    {
        var h = _options.ArenaHalfSize * 0.95f;
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var p = new Vector3(
                (float)(_random.NextDouble() * 2 - 1) * h,
                (float)(_random.NextDouble() * 2 - 1) * h,
                0);
            if (!IsBlocked(p))
            {
                return p;
            }
        }

        return Vector3.Zero;
    }

    private bool IsBlocked(Vector3 p)
    {
        var probe = p + new Vector3(0, 0, 1);
        foreach (var wall in Walls)
        {
            if (wall.Contains(probe))
            {
                return true;
            }
        }

        return false;
    }
}
