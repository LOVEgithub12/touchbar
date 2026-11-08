namespace WindowsTouchBar.Device;

internal interface IDisplayTransport : IDisposable
{
    int Width { get; }
    int Height { get; }
    void SubmitRgbaFrame(IntPtr address, int byteLength);
}
