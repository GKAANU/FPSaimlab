namespace AimLab.Memory;

/// <summary>Fixed layout of the simulated process. All offsets are in bytes.</summary>
public static class MemoryLayout
{
    public const uint Magic = 0x4C4D4941; // "AIML"
    public const int Version = 1;
    public const int MaxEntities = 64;

    // Header
    public const long HeaderMagic = 0x00;          // uint
    public const long HeaderVersion = 0x04;        // int
    public const long HeaderEntityListPtr = 0x08;  // long
    public const long HeaderEntityCapacity = 0x10; // int
    public const long HeaderLocalPlayerIndex = 0x14; // int
    public const long HeaderTick = 0x18;           // long
    public const long HeaderTime = 0x20;           // double
    public const int HeaderSize = 0x40;

    public const long EntityListBase = 0x100;
    public const int EntityStride = 0x40;

    // Entity record, relative to its slot address
    public const long EntityId = 0x00;       // int, 0 = empty slot
    public const long EntityTeam = 0x04;     // int (Team)
    public const long EntityHealth = 0x08;   // int
    public const long EntityFlags = 0x0C;    // int (EntityFlags)
    public const long EntityOrigin = 0x10;   // Vector3, feet position
    public const long EntityHead = 0x1C;     // Vector3
    public const long EntityBody = 0x28;     // Vector3
    public const long EntityPitch = 0x34;    // float
    public const long EntityYaw = 0x38;      // float

    public const int RequiredSize = (int)EntityListBase + MaxEntities * EntityStride;

    public static long SlotAddress(long listBase, int index) => listBase + (long)index * EntityStride;
}

[Flags]
public enum EntityFlags
{
    None = 0,
    Alive = 1 << 0,
    Visible = 1 << 1,
}
