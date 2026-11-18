using AimLab.Memory;

namespace AimLab.Simulation;

/// <summary>
/// The controlled "game process": owns the world and mirrors its state into a
/// <see cref="SimulatedProcessMemory"/> using <see cref="MemoryLayout"/> after every step.
/// </summary>
public sealed class SimulatedGameProcess
{
    public SimulatedGameProcess(SimulationOptions options)
    {
        Options = options;
        World = new GameWorld(options);
        Memory = new SimulatedProcessMemory(MemoryLayout.RequiredSize);
        Publish();
    }

    public SimulationOptions Options { get; }

    public GameWorld World { get; }

    public SimulatedProcessMemory Memory { get; }

    public void Step(double dt)
    {
        World.Step(dt);
        Publish();
    }

    /// <summary>Writes the header and every entity slot. Unused slots are zeroed.</summary>
    public void Publish()
    {
        var players = World.Players;
        var capacity = Math.Min(players.Count, MemoryLayout.MaxEntities);

        Memory.Write(MemoryLayout.HeaderMagic, MemoryLayout.Magic);
        Memory.Write(MemoryLayout.HeaderVersion, MemoryLayout.Version);
        Memory.Write(MemoryLayout.HeaderEntityListPtr, MemoryLayout.EntityListBase);
        Memory.Write(MemoryLayout.HeaderEntityCapacity, MemoryLayout.MaxEntities);
        Memory.Write(MemoryLayout.HeaderTick, World.Tick);
        Memory.Write(MemoryLayout.HeaderTime, World.Time);

        var localIndex = -1;
        for (var i = 0; i < capacity; i++)
        {
            var p = players[i];
            var slot = MemoryLayout.SlotAddress(MemoryLayout.EntityListBase, i);
            var flags = EntityFlags.None;
            if (p.IsAlive)
            {
                flags |= EntityFlags.Alive;
            }

            if (p.IsVisibleToLocal)
            {
                flags |= EntityFlags.Visible;
            }

            var view = ReferenceEquals(p, World.LocalPlayer) ? World.CurrentView : p.View;

            Memory.Write(slot + MemoryLayout.EntityId, p.Id);
            Memory.Write(slot + MemoryLayout.EntityTeam, (int)p.Team);
            Memory.Write(slot + MemoryLayout.EntityHealth, p.Health);
            Memory.Write(slot + MemoryLayout.EntityFlags, (int)flags);
            Memory.Write(slot + MemoryLayout.EntityOrigin, p.Origin);
            Memory.Write(slot + MemoryLayout.EntityHead, p.Head);
            Memory.Write(slot + MemoryLayout.EntityBody, p.Body);
            Memory.Write(slot + MemoryLayout.EntityPitch, view.Pitch);
            Memory.Write(slot + MemoryLayout.EntityYaw, view.Yaw);

            if (ReferenceEquals(p, World.LocalPlayer))
            {
                localIndex = i;
            }
        }

        for (var i = capacity; i < MemoryLayout.MaxEntities; i++)
        {
            Memory.Clear(MemoryLayout.SlotAddress(MemoryLayout.EntityListBase, i), MemoryLayout.EntityStride);
        }

        Memory.Write(MemoryLayout.HeaderLocalPlayerIndex, localIndex);
    }
}
