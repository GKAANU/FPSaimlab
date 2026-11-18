using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AimLab.UI;

/// <summary>
/// Relative mouse counts from Windows Raw Input for this window only. Registered without RIDEV_INPUTSINK,
/// so Windows delivers input only while the window is in the foreground. Nothing is injected or hooked globally.
/// </summary>
internal sealed class RawMouseInput : IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const uint RidevRemove = 0x00000001;
    private const ushort MouseMoveAbsolute = 0x0001;

    private readonly HwndSource _source;
    private readonly int _headerSize = Marshal.SizeOf<RawInputHeader>();
    private IntPtr _buffer;
    private uint _bufferSize;

    public RawMouseInput(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("No window handle.");
        _source.AddHook(WndProc);
        var device = new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = 0, Target = hwnd };
        IsAvailable = RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    public event Action<int, int>? Moved;

    public bool IsAvailable { get; }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmInput)
        {
            return IntPtr.Zero;
        }

        uint size = 0;
        GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, (uint)_headerSize);
        if (size == 0)
        {
            return IntPtr.Zero;
        }

        if (size > _bufferSize)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)size);
            _bufferSize = size;
        }

        if (GetRawInputData(lParam, RidInput, _buffer, ref size, (uint)_headerSize) != size ||
            (uint)Marshal.ReadInt32(_buffer) != RimTypeMouse)
        {
            return IntPtr.Zero;
        }

        // RAWMOUSE: usFlags @0, union @4, ulRawButtons @8, lLastX @12, lLastY @16.
        var flags = (ushort)Marshal.ReadInt16(_buffer, _headerSize);
        if ((flags & MouseMoveAbsolute) == 0)
        {
            var dx = Marshal.ReadInt32(_buffer, _headerSize + 12);
            var dy = Marshal.ReadInt32(_buffer, _headerSize + 16);
            if (dx != 0 || dy != 0)
            {
                Moved?.Invoke(dx, dy);
            }
        }

        // Not handled: DefWindowProc must still run for WM_INPUT cleanup.
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        var device = new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevRemove, Target = IntPtr.Zero };
        RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>());
        _source.RemoveHook(WndProc);
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    /// <summary>Confines the cursor to a screen rectangle (device pixels) while mouse look is active, or releases it.</summary>
    public static void ClipCursorTo(Rect? screenRect)
    {
        if (screenRect is { } r)
        {
            var rect = new NativeRect { Left = (int)r.Left, Top = (int)r.Top, Right = (int)r.Right, Bottom = (int)r.Bottom };
            ClipCursor(ref rect);
        }
        else
        {
            ClipCursor(IntPtr.Zero);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClipCursor(IntPtr rect);
}
