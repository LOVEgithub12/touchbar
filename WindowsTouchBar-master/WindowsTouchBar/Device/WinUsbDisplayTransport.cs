namespace WindowsTouchBar.Device;

internal sealed class WinUsbDisplayTransport : IDisplayTransport
{
    private readonly DfrTransport transport;
    public int Width => transport.Width;
    public int Height => transport.Height;

    public WinUsbDisplayTransport(string path) => transport = DfrTransport.Open(path);

    public unsafe void SubmitRgbaFrame(IntPtr address, int byteLength)
    {
        if (byteLength != checked(Width * Height * 4)) throw new ArgumentException("Unexpected framebuffer length.");
        Span<byte> pixels = transport.PixelArea;
        fixed (byte* destination = pixels)
        {
            byte* source = (byte*)address;
            byte* output = destination;
            for (int x = 0; x < Width; x++)
            for (int y = Height - 1; y >= 0; y--)
            {
                byte* pixel = source + (Width * y + x) * 4;
                *output++ = pixel[0];
                *output++ = pixel[1];
                *output++ = pixel[2];
            }
        }
        transport.UpdateFrame();
    }

    public void Dispose() => transport.Dispose();
}
