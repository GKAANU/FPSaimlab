using System.Numerics;
using AimLab.Core;
using AimLab.Mathematics;

namespace AimLab.Simulation;

/// <summary>
/// Deterministic world. In <see cref="TrainingMode.Arena"/> bots wander, take random damage and respawn.
/// In training modes targets spawn in front of the local player from a seeded sequence that does not depend
/// on player performance. The local player is a fixed camera; only its view changes.
/// </summary>
public sealed class GameWorld : IAimSink
{
    private readonly SimulationOptions _options;
    private readonly Random _random;
    private readonly Random _spawnRandom;
    private readonly List<SimPlayer> _players = [];
    private readonly List<ShotEvent> _shotEvents = [];
    private readonly Lock _viewGate = new();
    private ViewAngles _pendingTurn;
    private bool _fireQueued;
    private double _nextFireTime;
    private int _spawnCount;

    public GameWorld(SimulationOptions options)
    {
        _options = options;
        _random = new Random(options.Seed);
        _spawnRandom = new Random(unchecked(options.Seed * 31 + 7));
        var h = options.ArenaHalfSize;
        IsTraining = options.Mode != TrainingMode.Arena;

        Walls = !IsTraining || options.Visibility == VisibilityScenario.WallOcclusion
            ?
            [
                new Aabb(new(-h * 0.45f, -h * 0.55f, 0), new(-h * 0.35f, -h * 0.05f, 200)),
                new Aabb(new(-h * 0.45f, h * 0.10f, 0), new(-h * 0.35f, h * 0.55f, 200)),
                new Aabb(new(h * 0.05f, -h * 0.15f, 0), new(h * 0.25f, h * 0.15f, 200)),
                new Aabb(new(h * 0.45f, -h * 0.80f, 0), new(h * 0.55f, -h * 0.30f, 200)),
                new Aabb(new(h * 0.45f, h * 0.30f, 0), new(h * 0.55f, h * 0.80f, 200)),
            ]
            : [];

        LocalPlayer = NewPlayer(1, Team.Defenders);
        LocalPlayer.Origin = new Vector3(-h * 0.85f, 0, 0);
        _players.Add(LocalPlayer);

        var nextId = 2;
        if (IsTraining)
        {
            for (var i = 0; i < options.EffectiveTargetCount; i++)
            {
                _players.Add(SpawnTraining(NewPlayer(nextId++, Team.Attackers)));
            }
        }
        else
        {
            for (var i = 0; i < options.BotsPerTeam - 1; i++)
            {
                _players.Add(SpawnArena(NewPlayer(nextId++, Team.Defenders)));
            }

            for (var i = 0; i < options.BotsPerTeam; i++)
            {
                _players.Add(SpawnArena(NewPlayer(nextId++, Team.Attackers)));
            }
        }

        UpdateVisibility();
    }

    public bool IsTraining { get; }

    public SimulationOptions Options => _options;

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

    /// <summary>Queues one shot from user input. Extra shots before the fire interval elapses are dropped.</summary>
    public void QueueFire()
    {
        lock (_viewGate)
        {
            _fireQueued = true;
        }
    }

    /// <summary>Shot events resolved since the last call.</summary>
    public IReadOnlyList<ShotEvent> DrainShotEvents()
    {
        if (_shotEvents.Count == 0)
        {
            return [];
        }

        ShotEvent[] events = [.. _shotEvents];
        _shotEvents.Clear();
        return events;
    }

    public void Step(double dt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dt);
        Time += dt;
        Tick++;

        bool fire;
        lock (_viewGate)
        {
            var v = LocalPlayer.View;
            LocalPlayer.View = AngleMath.Normalize(new ViewAngles(v.Pitch + _pendingTurn.Pitch, v.Yaw + _pendingTurn.Yaw));
            _pendingTurn = default;
            fire = _fireQueued;
            _fireQueued = false;
        }

        foreach (var p in _players)
        {
            if (ReferenceEquals(p, LocalPlayer))
            {
                continue;
            }

            if (!p.IsAlive)
            {
                if (p.RespawnAt is { } at && Time >= at)
                {
                    if (IsTraining) SpawnTraining(p); else SpawnArena(p);
                }

                continue;
            }

            if (IsTraining)
            {
                MoveTraining(p, (float)dt);
                UpdatePeek(p);
            }
            else
            {
                MoveArena(p, (float)dt);
                if (_random.NextDouble() < _options.DamageEventsPerSecond * dt)
                {
                    p.Health = Math.Max(0, p.Health - _random.Next(10, 45));
                    if (!p.IsAlive)
                    {
                        p.RespawnAt = Time + _options.RespawnDelaySeconds;
                    }
                }
            }
        }

        UpdateVisibility();

        if (fire && Time >= _nextFireTime)
        {
            ResolveShot();
            _nextFireTime = Time + _options.FireIntervalSeconds;
        }
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

    /// <summary>
    /// Hitscan along the current view against head and body spheres of exposed enemies.
    /// shortcut: spheres, no recoil/spread/movement inaccuracy, every hit eliminates (except in tracking); not CS2 weapon behaviour.
    /// </summary>
    private void ResolveShot()
    {
        var eye = LocalPlayer.Head;
        var view = CurrentView;
        var dir = AngleMath.Forward(view);
        SimPlayer? best = null;
        var bestDistance = float.MaxValue;
        var headshot = false;

        foreach (var p in _players)
        {
            if (ReferenceEquals(p, LocalPlayer) || !p.IsAlive || !p.IsExposed || p.Team == LocalPlayer.Team)
            {
                continue;
            }

            if (GeometryMath.RaySphere(eye, dir, p.Head, _options.HeadRadius, out var t) && t < bestDistance)
            {
                (best, bestDistance, headshot) = (p, t, true);
            }

            if (GeometryMath.RaySphere(eye, dir, p.Body, _options.BodyRadius, out t) && t < bestDistance)
            {
                (best, bestDistance, headshot) = (p, t, false);
            }
        }

        if (best is not null && !HasLineOfSight(eye, eye + dir * bestDistance))
        {
            best = null;
        }

        if (best is null)
        {
            _shotEvents.Add(new ShotEvent(Time, Tick, view, null, 0, false, false, null, false));
            return;
        }

        var firstHit = !best.HitThisLife;
        best.HitThisLife = true;
        var killed = _options.Mode != TrainingMode.Tracking;
        if (killed)
        {
            best.Health = 0;
            best.IsVisibleToLocal = false;
            best.RespawnAt = Time + (IsTraining ? _options.TrainingRespawnDelaySeconds : _options.RespawnDelaySeconds);
        }

        _shotEvents.Add(new ShotEvent(Time, Tick, view, best.Id, best.Life, headshot, killed, best.FirstVisibleAt, firstHit));
    }

    private void UpdateVisibility()
    {
        var eye = LocalPlayer.Head;
        foreach (var p in _players)
        {
            p.IsVisibleToLocal = !ReferenceEquals(p, LocalPlayer) && p.IsAlive && p.IsExposed &&
                (HasLineOfSight(eye, p.Head) || HasLineOfSight(eye, p.Body));
            if (p.IsVisibleToLocal && p.FirstVisibleAt is null)
            {
                p.FirstVisibleAt = Time;
            }
        }
    }

    private SimPlayer NewPlayer(int id, Team team) => new(id, team, _options.EyeHeight, _options.BodyHeight);

    private void ResetLife(SimPlayer p)
    {
        p.Life++;
        p.Health = 100;
        p.RespawnAt = null;
        p.HitThisLife = false;
        p.FirstVisibleAt = null;
        p.IsExposed = true;
        p.SpawnTime = Time;
    }

    private SimPlayer SpawnTraining(SimPlayer p)
    {
        ResetLife(p);
        var index = _spawnCount++;
        p.Motion = new Random(unchecked(_options.Seed * 7919 + index));

        var eye = LocalPlayer.Head;
        var min = MathF.Min(_options.MinDistance, _options.MaxDistance);
        var max = MathF.Max(_options.MinDistance, _options.MaxDistance);
        var origin = Vector3.Zero;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var yaw = (float)(_spawnRandom.NextDouble() * 2 - 1) * _options.SpawnYawSpread;
            var pitch = (float)(_spawnRandom.NextDouble() * 2 - 1) * _options.SpawnPitchSpread;
            var distance = min + (float)_spawnRandom.NextDouble() * (max - min);
            origin = eye + AngleMath.Forward(new ViewAngles(pitch, yaw)) * distance - new Vector3(0, 0, p.EyeHeight);
            if (!IsBlocked(origin) && !IsBlocked(origin + new Vector3(0, 0, p.EyeHeight)))
            {
                break;
            }
        }

        p.Origin = origin;
        p.View = new ViewAngles(0, AngleMath.CalculateAngles(origin, LocalPlayer.Origin).Yaw);
        p.StrafeSign = p.Motion.Next(2) == 0 ? -1f : 1f;
        p.NextStrafeFlip = Time + NextStrafeInterval(p);
        p.PeekOffset = p.Motion.NextDouble() * (_options.PeekVisibleSeconds + _options.PeekHiddenSeconds);
        UpdatePeek(p);
        return p;
    }

    private double NextStrafeInterval(SimPlayer p) => 0.3 + p.Motion!.NextDouble() * 0.9;

    /// <summary>Strafes tangentially around the eye at constant horizontal distance, reversing at random intervals and at the spawn yaw limit.</summary>
    private void MoveTraining(SimPlayer p, float dt)
    {
        if (_options.TargetSpeed <= 0)
        {
            return;
        }

        if (Time >= p.NextStrafeFlip)
        {
            p.StrafeSign = -p.StrafeSign;
            p.NextStrafeFlip = Time + NextStrafeInterval(p);
        }

        var eye = LocalPlayer.Head;
        var rel = new Vector2(p.Origin.X - eye.X, p.Origin.Y - eye.Y);
        var radius = rel.Length();
        if (radius < 1f)
        {
            return;
        }

        var tangent = new Vector2(-rel.Y, rel.X) / radius;
        var moved = rel + tangent * p.StrafeSign * _options.TargetSpeed * dt;
        moved = Vector2.Normalize(moved) * radius;
        var next = new Vector3(eye.X + moved.X, eye.Y + moved.Y, p.Origin.Z);
        var yaw = MathF.Atan2(moved.Y, moved.X) * 180f / MathF.PI;

        if (MathF.Abs(AngleMath.NormalizeYaw(yaw)) > _options.SpawnYawSpread || IsBlocked(next))
        {
            p.StrafeSign = -p.StrafeSign;
            return;
        }

        p.Origin = next;
    }

    private void UpdatePeek(SimPlayer p)
    {
        if (_options.Visibility != VisibilityScenario.Peek)
        {
            p.IsExposed = true;
            return;
        }

        var cycle = _options.PeekVisibleSeconds + _options.PeekHiddenSeconds;
        var phase = (Time - p.SpawnTime + p.PeekOffset) % cycle;
        p.IsExposed = phase < _options.PeekVisibleSeconds;
    }

    private void MoveArena(SimPlayer bot, float dt)
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

    private SimPlayer SpawnArena(SimPlayer bot)
    {
        ResetLife(bot);
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
