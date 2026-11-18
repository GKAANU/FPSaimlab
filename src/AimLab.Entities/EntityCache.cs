namespace AimLab.Entities;

/// <summary>Cached entity plus bookkeeping used by targeting and telemetry.</summary>
public sealed record TrackedEntity(PlayerEntity Entity, long FirstSeenTick, long LastSeenTick, double? VisibleSince);

public interface IEntityCache
{
    LocalPlayerState LocalPlayer { get; }

    /// <summary>Entities present in the most recent valid read.</summary>
    IReadOnlyList<PlayerEntity> Players { get; }

    int InvalidCount { get; }

    int TrackedCount { get; }

    long LastTick { get; }

    void Update(EntityReadResult read);

    TrackedEntity? Get(int id);
}

/// <summary>
/// Keeps entities by id across ticks, tracks when each became visible, and evicts ids
/// that have not appeared for <see cref="StaleTicks"/> ticks.
/// </summary>
public sealed class EntityCache : IEntityCache
{
    public const int StaleTicks = 128;

    private readonly Dictionary<int, TrackedEntity> _tracked = [];
    private readonly List<int> _evict = [];

    public LocalPlayerState LocalPlayer { get; } = new();

    public IReadOnlyList<PlayerEntity> Players { get; private set; } = [];

    public int InvalidCount { get; private set; }

    public int TrackedCount => _tracked.Count;

    public long LastTick { get; private set; }

    public void Update(EntityReadResult read)
    {
        InvalidCount = read.InvalidCount;
        if (!read.HeaderValid)
        {
            // Keep the previous cache: a single bad read should not drop every target.
            return;
        }

        LastTick = read.Tick;
        LocalPlayer.Update(read.LocalPlayer);

        foreach (var player in read.Players)
        {
            if (_tracked.TryGetValue(player.Id, out var previous))
            {
                double? visibleSince = player.IsVisible && player.IsAlive
                    ? previous.VisibleSince ?? read.Time
                    : null;
                _tracked[player.Id] = previous with { Entity = player, LastSeenTick = read.Tick, VisibleSince = visibleSince };
            }
            else
            {
                _tracked[player.Id] = new TrackedEntity(
                    player, read.Tick, read.Tick, player.IsVisible && player.IsAlive ? read.Time : (double?)null);
            }
        }

        _evict.Clear();
        foreach (var (id, tracked) in _tracked)
        {
            if (read.Tick - tracked.LastSeenTick > StaleTicks)
            {
                _evict.Add(id);
            }
        }

        foreach (var id in _evict)
        {
            _tracked.Remove(id);
        }

        Players = read.Players;
    }

    public TrackedEntity? Get(int id) => _tracked.GetValueOrDefault(id);
}
