using System.Numerics;
using AimLab.Core;
using AimLab.Entities;
using AimLab.Memory;
using AimLab.Simulation;

namespace AimLab.Tests;

public class MemoryAndEntityTests
{
    [Fact]
    public void Memory_RoundTripsAndBoundsChecks()
    {
        var mem = new SimulatedProcessMemory(MemoryLayout.RequiredSize);
        mem.Write(0x200L, new Vector3(1, 2, 3));
        Assert.Equal(new Vector3(1, 2, 3), mem.Read<Vector3>(0x200));

        Assert.False(mem.TryRead<long>(mem.Size - 4, out _));
        Assert.False(mem.TryRead<int>(-1, out _));
        Assert.Throws<MemoryAccessException>(() => mem.Read<int>(mem.Size));
        Assert.Throws<MemoryAccessException>(() => mem.Write(mem.Size - 2, 1));
    }

    [Fact]
    public void Parser_ReadsPublishedWorld()
    {
        var process = new SimulatedGameProcess(new SimulationOptions { BotsPerTeam = 4 });
        var read = new MemoryEntityParser(process.Memory).ReadSnapshot();

        Assert.True(read.HeaderValid);
        Assert.Equal(0, read.InvalidCount);
        Assert.NotNull(read.LocalPlayer);
        Assert.Equal(1, read.LocalPlayer!.Id);
        Assert.Equal(7, read.Players.Count);
        Assert.Equal(4, read.Players.Count(p => p.Team == Team.Attackers));
        Assert.All(read.Players, p => Assert.Equal(p.Origin + new Vector3(0, 0, SimPlayer.EyeHeight), p.HeadPosition));
    }

    [Fact]
    public void Parser_RejectsCorruptSlotsButKeepsOthers()
    {
        var process = new SimulatedGameProcess(new SimulationOptions { BotsPerTeam = 2 });
        var slot1 = MemoryLayout.SlotAddress(MemoryLayout.EntityListBase, 1);
        var slot2 = MemoryLayout.SlotAddress(MemoryLayout.EntityListBase, 2);
        process.Memory.Write(slot1 + MemoryLayout.EntityHealth, 500);
        process.Memory.Write(slot2 + MemoryLayout.EntityOrigin, new Vector3(float.NaN, 0, 0));

        var read = new MemoryEntityParser(process.Memory).ReadSnapshot();

        Assert.True(read.HeaderValid);
        Assert.Equal(2, read.InvalidCount);
        Assert.Single(read.Players);
    }

    [Fact]
    public void Parser_RejectsBadHeader()
    {
        var process = new SimulatedGameProcess(new SimulationOptions());
        process.Memory.Write(MemoryLayout.HeaderEntityCapacity, int.MaxValue);
        Assert.False(new MemoryEntityParser(process.Memory).ReadSnapshot().HeaderValid);

        process.Publish();
        process.Memory.Write(MemoryLayout.HeaderMagic, 0xDEADBEEFu);
        Assert.False(new MemoryEntityParser(process.Memory).ReadSnapshot().HeaderValid);
    }

    [Fact]
    public void Cache_TracksVisibilityOnsetAndEvictsStale()
    {
        var cache = new EntityCache();
        var enemy = TestData.Player(5, Team.Attackers, new Vector3(100, 0, 0));
        EntityReadResult Read(long tick, params PlayerEntity[] players) =>
            new(true, tick, tick / 64.0, null, players, 0);

        cache.Update(Read(1, enemy with { IsVisible = false }));
        Assert.Null(cache.Get(5)!.VisibleSince);

        cache.Update(Read(2, enemy));
        cache.Update(Read(3, enemy));
        Assert.Equal(2 / 64.0, cache.Get(5)!.VisibleSince);

        cache.Update(Read(3 + EntityCache.StaleTicks + 1));
        Assert.Null(cache.Get(5));
        Assert.Empty(cache.Players);
    }

    [Fact]
    public void Cache_IgnoresInvalidHeaderRead()
    {
        var cache = new EntityCache();
        cache.Update(new EntityReadResult(true, 1, 0, null, [TestData.Player(5, Team.Attackers, Vector3.Zero)], 0));
        cache.Update(EntityReadResult.Invalid);
        Assert.Single(cache.Players);
    }
}
