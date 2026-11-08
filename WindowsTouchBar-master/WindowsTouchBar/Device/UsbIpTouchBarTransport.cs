// SPDX-License-Identifier: GPL-2.0-only
// Display packet composition follows TonyTouchBar and Linux appletbdrm.
// Protocol credit: Kerem Karabay and Linux appletbdrm contributors.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using WindowsTouchBar.Events;

namespace WindowsTouchBar.Device;

internal sealed class UsbIpTouchBarTransport : IDisplayTransport, ITouchInput
{
    private readonly UsbIpMultiplexer usb;
    private readonly CancellationTokenSource stop = new();
    private readonly object frameLock = new();
    private byte[] rgba = Array.Empty<byte>();
    private ulong timestamp = 1;
    private int disposed;
    private Exception? failure;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public event Action<TouchEventArgs>? Event;
    public event Action<Exception>? Failed;

    private UsbIpTouchBarTransport(UsbIpMultiplexer usb) => this.usb = usb;
    public static UsbIpTouchBarTransport Open()
    {
        var transport = new UsbIpTouchBarTransport(UsbIpMultiplexer.ImportUnique());
        try
        {
            transport.InitializeAsync().GetAwaiter().GetResult();
            _ = Task.Run(transport.ReadTouchesAsync);
            return transport;
        }
        catch { transport.Dispose(); throw; }
    }

    private async Task InitializeAsync()
    {
        await ValidateDescriptorsAsync().ConfigureAwait(false);
        byte[] configuration = await usb.ControlAsync(0x80, 8, 0, 0, 1).ConfigureAwait(false);
        if (configuration.Length != 1) throw new IOException("Invalid configuration reply.");
        if (configuration[0] != 2)
        {
            await usb.ControlAsync(0, 9, 0, 0, 0).ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
        }
        // VBoxUSB creates endpoint handles when this session selects configuration 2.
        await usb.ControlAsync(0, 9, 2, 0, 0).ConfigureAwait(false);
        await Task.Delay(2000).ConfigureAwait(false);
        configuration = await usb.ControlAsync(0x80, 8, 0, 0, 1).ConfigureAwait(false);
        if (configuration.Length != 1 || configuration[0] != 2) throw new IOException("Touch Bar display configuration was not selected.");
        // Omit SET_INTERFACE to match the sequence verified on this Apple virtual USB stack.
        await usb.ControlAsync(2, 1, 0, 0x01, 0).ConfigureAwait(false);
        await usb.ControlAsync(2, 1, 0, 0x82, 0).ConfigureAwait(false);
        await usb.WriteAsync(0x01, Request(0x47494e46)).ConfigureAwait(false);
        byte[] info = await ReadMessageAsync(0x47494e46, 65).ConfigureAwait(false);
        uint width = Read32(info, 32), height = Read32(info, 36), format = Read32(info, 53);
        if (width == 0 || width > 4096 || height == 0 || height > 256 || format != 0x52474241)
            throw new IOException($"Unsupported Touch Bar geometry/format: {width}x{height}, 0x{format:X8}.");
        Width = (int)width; Height = (int)height; rgba = new byte[checked(Width * Height * 4)];
        await usb.WriteAsync(0x01, Request(0x52454459)).ConfigureAwait(false);
        await usb.WriteAsync(0x01, Request(0x434c4452)).ConfigureAwait(false);
    }

    private async Task ValidateDescriptorsAsync()
    {
        if (usb.ImportedDevice.Configurations < 2 || usb.ImportedDevice.Configurations > 8)
            throw new IOException("Unexpected Touch Bar configuration count.");
        bool found = false;
        for (int index = 0; index < usb.ImportedDevice.Configurations; index++)
        {
            ushort selector = (ushort)(0x200 | index);
            byte[] head = await usb.ControlAsync(0x80, 6, selector, 0, 9).ConfigureAwait(false);
            if (head.Length != 9 || head[0] != 9 || head[1] != 2) throw new IOException("Invalid configuration descriptor header.");
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(2));
            if (length < 9 || length > 4096) throw new IOException("Configuration descriptor length is out of bounds.");
            byte[] descriptor = await usb.ControlAsync(0x80, 6, selector, 0, length).ConfigureAwait(false);
            if (descriptor.Length != length || descriptor[5] != head[5]) throw new IOException("Configuration descriptor changed.");
            bool display = false, touch = false, bulkIn = false, bulkOut = false, touchIn = false;
            for (int offset = 9; offset < descriptor.Length;)
            {
                int size = descriptor[offset];
                if (size < 2 || offset + size > descriptor.Length) throw new IOException("Truncated USB descriptor.");
                if (descriptor[offset + 1] == 4)
                {
                    if (size < 9) throw new IOException("Truncated interface descriptor.");
                    display = descriptor[5] == 2 && descriptor[offset + 2] == 1 && descriptor[offset + 3] == 0;
                    touch = descriptor[5] == 2 && descriptor[offset + 2] == 0 && descriptor[offset + 3] == 0;
                }
                else if (descriptor[offset + 1] == 5)
                {
                    if (size < 7) throw new IOException("Truncated endpoint descriptor.");
                    byte endpoint = descriptor[offset + 2], type = (byte)(descriptor[offset + 3] & 3);
                    if (display && type == 2) { bulkOut |= endpoint == 1; bulkIn |= endpoint == 0x82; }
                    if (touch && type == 3) touchIn |= endpoint == 0x81;
                }
                offset += size;
            }
            if (descriptor[5] == 2) found = bulkIn && bulkOut && touchIn;
        }
        if (!found) throw new IOException("Required display and touch endpoints are absent from configuration 2.");
    }

    private async Task<byte[]> ReadMessageAsync(uint key, int minimum, ulong? frameId = null)
    {
        for (int count = 0; count < 4; count++)
        {
            byte[] bytes = await usb.ReadAsync(0x82, 256, TimeSpan.FromSeconds(5), stop.Token).ConfigureAwait(false);
            if (bytes.Length >= minimum && Read32(bytes, 0) == 0x01140000 && Read32(bytes, 16) == key &&
                (!frameId.HasValue || BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(32)) == frameId.Value)) return bytes;
        }
        throw new IOException($"No matching Touch Bar response for 0x{key:X8}.");
    }

    public void SubmitRgbaFrame(IntPtr address, int byteLength)
    {
        lock (frameLock)
        {
            ThrowIfUnavailable();
            if (address == IntPtr.Zero || byteLength != rgba.Length) throw new ArgumentException("Unexpected RGBA frame length.");
            Marshal.Copy(address, rgba, 0, byteLength);
            try
            {
                byte[] packet = BuildFrame(rgba, Width, Height, timestamp);
                usb.WriteAsync(0x01, packet).GetAwaiter().GetResult();
                ReadMessageAsync(0x5544434c, 40, timestamp).GetAwaiter().GetResult();
                timestamp = checked(timestamp + 1);
            }
            catch (Exception exception) { Interlocked.CompareExchange(ref failure, exception, null); throw; }
        }
    }

    private async Task ReadTouchesAsync()
    {
        bool wasDown = false; int previousX = 0, previousY = 0;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                byte[] report;
                try { report = await usb.ReadAsync(0x81, 64, TimeSpan.FromSeconds(30), stop.Token).ConfigureAwait(false); }
                catch (TimeoutException) { continue; }
                var touch = ParseTouchReport(report, Width, Height);
                if (touch.Down)
                {
                    var kind = wasDown ? TouchEventType.TouchMove : TouchEventType.TouchDown;
                    if (!wasDown || touch.X != previousX || touch.Y != previousY) Event?.Invoke(new TouchEventArgs(touch.X, touch.Y, kind));
                    previousX = touch.X; previousY = touch.Y;
                }
                else if (wasDown) Event?.Invoke(new TouchEventArgs(previousX, previousY, TouchEventType.TouchUp));
                wasDown = touch.Down;
            }
        }
        catch (Exception exception)
        {
            if (!stop.IsCancellationRequested && Volatile.Read(ref disposed) == 0)
            {
                Interlocked.CompareExchange(ref failure, exception, null);
                Failed?.Invoke(exception);
            }
        }
    }

    internal static (bool Down, int X, int Y) ParseTouchReport(byte[] report, int width, int height)
    {
        if (report.Length < 44 || report.Length > 64) throw new IOException("Unexpected Touch Bar input report size.");
        for (int slot = 0; slot < 11; slot++)
        {
            int offset = slot * 4;
            if ((report[offset] & 0x10) == 0) continue;
            uint rawX = BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(offset + 1));
            uint rawY = report[offset + 3];
            if (rawX > 32767 || rawY > 127) throw new IOException("Touch coordinates exceed the hardware descriptor range.");
            return (true, Math.Min(width - 1, (int)(rawX * width / 32767)), Math.Min(height - 1, (int)(rawY * height / 127)));
        }
        return (false, 0, 0);
    }

    internal static byte[] BuildFrame(byte[] source, int width, int height, ulong stamp)
    {
        if (width <= 0 || width > 4096 || height <= 0 || height > 256 || source.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid framebuffer dimensions.");
        int rgbLength = checked(width * height * 3), footer = 60 + rgbLength, total = (footer + 80 + 15) & ~15;
        byte[] packet = new byte[total];
        Write32(packet, 0, 0x00120002); Write32(packet, 4, 9); Write32(packet, 12, (uint)(total - 16));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16), 1); packet[18] = (byte)stamp;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(52), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(54), (ushort)height); Write32(packet, 56, (uint)rgbLength);
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            int from = ((height - 1 - y) * width + x) * 4, to = 60 + (x * height + y) * 3;
            packet[to] = source[from]; packet[to + 1] = source[from + 1]; packet[to + 2] = source[from + 2];
        }
        Write32(packet, footer + 12, 0xfffe); Write32(packet, footer + 28, 0x80001);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(footer + 32), stamp);
        Write32(packet, footer + 52, 0x80002); Write32(packet, footer + 76, 0xffff);
        return packet;
    }
    private static byte[] Request(uint key)
    { var bytes = new byte[32]; Write32(bytes, 0, 0x15120002); Write32(bytes, 12, 16); Write32(bytes, 16, key); Write32(bytes, 28, 16); return bytes; }
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(UsbIpTouchBarTransport));
        if (Volatile.Read(ref failure) is { } exception) throw new IOException("Touch Bar transport failed.", exception);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        usb.Dispose();
        // Do not join the touch task while the UI view lock can be held by the caller.
    }
}
