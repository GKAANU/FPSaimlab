using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AimLab.Memory;

/// <summary>
/// In-process byte arena standing in for a game's address space.
/// It never touches another process; reads and writes are bounds-checked and serialized by a lock.
/// </summary>
public sealed class SimulatedProcessMemory : IMemoryReader, IMemoryWriter
{
    private readonly byte[] _buffer;
    private readonly Lock _gate = new();

    public SimulatedProcessMemory(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, MemoryLayout.HeaderSize);
        _buffer = new byte[size];
    }

    public long Size => _buffer.Length;

    public T Read<T>(long address) where T : unmanaged
    {
        if (!TryRead<T>(address, out var value))
        {
            throw new MemoryAccessException(address, Unsafe.SizeOf<T>(), Size);
        }

        return value;
    }

    public bool TryRead<T>(long address, out T value) where T : unmanaged
    {
        var length = Unsafe.SizeOf<T>();
        if (!InBounds(address, length))
        {
            value = default;
            return false;
        }

        lock (_gate)
        {
            value = MemoryMarshal.Read<T>(_buffer.AsSpan((int)address, length));
        }

        return true;
    }

    public void Write<T>(long address, T value) where T : unmanaged
    {
        var length = Unsafe.SizeOf<T>();
        if (!InBounds(address, length))
        {
            throw new MemoryAccessException(address, length, Size);
        }

        lock (_gate)
        {
            MemoryMarshal.Write(_buffer.AsSpan((int)address, length), in value);
        }
    }

    public void Clear(long address, int length)
    {
        if (!InBounds(address, length))
        {
            throw new MemoryAccessException(address, length, Size);
        }

        lock (_gate)
        {
            _buffer.AsSpan((int)address, length).Clear();
        }
    }

    private bool InBounds(long address, int length) =>
        address >= 0 && length >= 0 && address <= _buffer.Length - length;
}
