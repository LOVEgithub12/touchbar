using WindowsTouchBar.Device;
using System.Runtime.InteropServices;

namespace WindowsTouchBar;

unsafe class LockedFrameBuffer : IDisposable
{
    private readonly IDisplayTransport _transport;
    private readonly int _width;
    private readonly int _height;

    public LockedFrameBuffer(IDisplayTransport transport, int width, int height)
    {
        _transport = transport;
        _width = width;
        _height = height;

        Address = Marshal.AllocHGlobal(checked(RowBytes * _height));
    }

    public IntPtr Address { get; private set; }

    public int RowBytes => _width * 4;

    public unsafe void VSync()
    {
        _transport.SubmitRgbaFrame(Address, checked(RowBytes * _height));
    }

    public void Dispose()
    {
        // Disposal must release memory even if the device has disconnected.
        Marshal.FreeHGlobal(Address);
        Address = IntPtr.Zero;
    }
}
