using System.Numerics;
using AimLab.Core;
using AimLab.Memory;

namespace AimLab.Entities;

public interface IEntityParser
{
    EntityReadResult ReadSnapshot();
}

/// <summary>Parses the header and entity list of the simulated process, rejecting malformed records.</summary>
public sealed class MemoryEntityParser(IMemoryReader reader) : IEntityParser
{
    private const float MaxCoordinate = 100_000f;

    public EntityReadResult ReadSnapshot()
    {
        if (!reader.TryRead<uint>(MemoryLayout.HeaderMagic, out var magic) || magic != MemoryLayout.Magic ||
            !reader.TryRead<int>(MemoryLayout.HeaderVersion, out var version) || version != MemoryLayout.Version ||
            !reader.TryRead<long>(MemoryLayout.HeaderEntityListPtr, out var listBase) ||
            !reader.TryRead<int>(MemoryLayout.HeaderEntityCapacity, out var capacity) ||
            !reader.TryRead<int>(MemoryLayout.HeaderLocalPlayerIndex, out var localIndex) ||
            !reader.TryRead<long>(MemoryLayout.HeaderTick, out var tick) ||
            !reader.TryRead<double>(MemoryLayout.HeaderTime, out var time))
        {
            return EntityReadResult.Invalid;
        }

        // A corrupt capacity must not drive an unbounded loop.
        if (capacity < 0 || capacity > MemoryLayout.MaxEntities)
        {
            return EntityReadResult.Invalid;
        }

        var players = new List<PlayerEntity>(capacity);
        PlayerEntity? local = null;
        var invalid = 0;

        for (var slot = 0; slot < capacity; slot++)
        {
            var address = MemoryLayout.SlotAddress(listBase, slot);
            switch (TryParse(address, slot, out var entity))
            {
                case SlotState.Empty:
                    continue;
                case SlotState.Invalid:
                    invalid++;
                    continue;
            }

            if (slot == localIndex)
            {
                local = entity;
            }
            else
            {
                players.Add(entity!);
            }
        }

        return new EntityReadResult(true, tick, time, local, players, invalid);
    }

    private enum SlotState { Valid, Empty, Invalid }

    private SlotState TryParse(long address, int slot, out PlayerEntity? entity)
    {
        entity = null;
        if (!reader.TryRead<int>(address + MemoryLayout.EntityId, out var id))
        {
            return SlotState.Invalid;
        }

        if (id == 0)
        {
            return SlotState.Empty;
        }

        if (id < 0 ||
            !reader.TryRead<int>(address + MemoryLayout.EntityTeam, out var team) ||
            !reader.TryRead<int>(address + MemoryLayout.EntityHealth, out var health) ||
            !reader.TryRead<int>(address + MemoryLayout.EntityFlags, out var rawFlags) ||
            !reader.TryRead<Vector3>(address + MemoryLayout.EntityOrigin, out var origin) ||
            !reader.TryRead<Vector3>(address + MemoryLayout.EntityHead, out var head) ||
            !reader.TryRead<Vector3>(address + MemoryLayout.EntityBody, out var body) ||
            !reader.TryRead<float>(address + MemoryLayout.EntityPitch, out var pitch) ||
            !reader.TryRead<float>(address + MemoryLayout.EntityYaw, out var yaw))
        {
            return SlotState.Invalid;
        }

        var flags = (EntityFlags)rawFlags;
        var alive = flags.HasFlag(EntityFlags.Alive);

        if (team is not ((int)Team.Attackers or (int)Team.Defenders) ||
            health is < 0 or > 100 ||
            alive != health > 0 ||
            (rawFlags & ~(int)(EntityFlags.Alive | EntityFlags.Visible)) != 0 ||
            !IsSane(origin) || !IsSane(head) || !IsSane(body) ||
            !float.IsFinite(pitch) || !float.IsFinite(yaw))
        {
            return SlotState.Invalid;
        }

        entity = new PlayerEntity(
            id, slot, (Team)team, health, alive, flags.HasFlag(EntityFlags.Visible),
            origin, head, body, new ViewAngles(pitch, yaw));
        return SlotState.Valid;
    }

    private static bool IsSane(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) &&
        MathF.Abs(v.X) < MaxCoordinate && MathF.Abs(v.Y) < MaxCoordinate && MathF.Abs(v.Z) < MaxCoordinate;
}
