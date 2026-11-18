namespace AimLab.Memory;

/// <summary>Read access to an address space. Implemented here only by <see cref="SimulatedProcessMemory"/>.</summary>
public interface IMemoryReader
{
    long Size { get; }

    /// <exception cref="MemoryAccessException">The read falls outside the address space.</exception>
    T Read<T>(long address) where T : unmanaged;

    bool TryRead<T>(long address, out T value) where T : unmanaged;
}

/// <summary>Write access to an address space.</summary>
public interface IMemoryWriter
{
    void Write<T>(long address, T value) where T : unmanaged;

    void Clear(long address, int length);
}

public sealed class MemoryAccessException(long address, int length, long size)
    : Exception($"Access of {length} bytes at 0x{address:X} is outside the 0x{size:X}-byte address space.")
{
    public long Address { get; } = address;
}
