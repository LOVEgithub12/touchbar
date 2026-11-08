using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using WindowsTouchBar.Events;

namespace WindowsTouchBar.Device;

/// <summary>Owns one Linux bridge process. Only that bridge accesses the attached USB device.</summary>
internal sealed class WslTouchBarBridge : IDisplayTransport, ITouchInput
{
    private readonly Process process;
    private readonly TimeSpan frameTimeout;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BlockingCollection<TouchEventArgs> touchEvents = new(256);
    private readonly ConcurrentQueue<string> diagnostics = new();
    private readonly object ioLock = new();
    private byte[] frame = Array.Empty<byte>();
    private ulong nextFrame = 1;
    private Exception? failure;
    private bool touchReady;
    private int disposed;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public event Action<TouchEventArgs>? Event;
    public event Action<Exception>? Failed;

    private WslTouchBarBridge(Process process, TimeSpan frameTimeout)
    {
        this.process = process;
        this.frameTimeout = frameTimeout;
        _ = Task.Run(ReadMessagesAsync);
        _ = Task.Run(DispatchTouches);
    }

    public static WslTouchBarBridge Open()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsl.exe"));
        foreach (string argument in new[] { "-d", "TouchBarBridge", "-u", "root", "--exec", "/opt/touchbar/t2-touchbar-bridge" })
            start.ArgumentList.Add(argument);
        return Open(start, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(30));
    }

    // A process boundary allows regression tests to supply an in-memory-protocol peer,
    // while production always uses the fixed distribution and bridge path above.
    internal static WslTouchBarBridge Open(ProcessStartInfo start, TimeSpan startupTimeout, TimeSpan frameTimeout)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardErrorEncoding = Encoding.UTF8;
        var process = Process.Start(start) ?? throw new IOException("Could not start the WSL Touch Bar bridge.");
        var bridge = new WslTouchBarBridge(process, frameTimeout);
        try
        {
            bridge.ready.Task.WaitAsync(startupTimeout).GetAwaiter().GetResult();
            bridge.ThrowIfUnavailable();
            bridge.frame = new byte[checked(bridge.Width * bridge.Height * 4)];
            return bridge;
        }
        catch (Exception exception)
        {
            string details = string.Join(" | ", bridge.diagnostics);
            bridge.Dispose();
            throw new IOException("WSL Touch Bar bridge did not become ready. " + details, exception);
        }
    }

    private async Task ReadMessagesAsync()
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (Volatile.Read(ref disposed) != 0) continue;
                if (line.Length > 4096) throw new IOException("Bridge diagnostic line exceeds its size limit.");
                if (line.StartsWith("TOUCH ", StringComparison.Ordinal))
                {
                    var touch = ParseTouch(line, Width, Height);
                    // Losing a release would leave a key down. Stop on overflow instead of
                    // dropping input transitions; shutdown releases the last active button.
                    if (!touchEvents.TryAdd(touch)) throw new IOException("Touch bridge input queue overflowed.");
                }
                else if (line.StartsWith("Touch read failed:", StringComparison.Ordinal))
                {
                    throw new IOException(line);
                }
                else if (line.StartsWith("READY ", StringComparison.Ordinal))
                {
                    if (ready.Task.IsCompleted) throw new IOException("Bridge sent a second READY message.");
                    (int width, int height) = ParseReady(line);
                    if (!touchReady) throw new IOException("Bridge display opened, but its touch interface did not open.");
                    Width = width;
                    Height = height;
                    ready.TrySetResult(true);
                }
                else
                {
                    if (line == "Touch input interface ready.") touchReady = true;
                    diagnostics.Enqueue(line);
                    while (diagnostics.Count > 12) diagnostics.TryDequeue(out _);
                }
            }
            if (Volatile.Read(ref disposed) == 0)
                Fail(new IOException("WSL Touch Bar bridge exited. " + string.Join(" | ", diagnostics)));
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref disposed) == 0) Fail(exception);
        }
    }

    private void DispatchTouches()
    {
        try
        {
            foreach (var touch in touchEvents.GetConsumingEnumerable())
            {
                if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref failure) is not null) break;
                Event?.Invoke(touch);
            }
        }
        catch (Exception exception) { Fail(exception); }
        if (Volatile.Read(ref disposed) == 0 && Volatile.Read(ref failure) is { } error)
            Failed?.Invoke(error);
    }

    private void Fail(Exception error)
    {
        Interlocked.CompareExchange(ref failure, error, null);
        ready.TrySetException(error);
        touchEvents.CompleteAdding();
    }

    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(WslTouchBarBridge));
        if (Volatile.Read(ref failure) is { } error) throw new IOException("WSL Touch Bar connection failed.", error);
    }

    public void SubmitRgbaFrame(IntPtr address, int byteLength)
    {
        lock (ioLock)
        {
            ThrowIfUnavailable();
            if (address == IntPtr.Zero || byteLength != frame.Length) throw new ArgumentException("Unexpected RGBA frame buffer.");
            Marshal.Copy(address, frame, 0, frame.Length);
            try { ExchangeFrameAsync().GetAwaiter().GetResult(); }
            catch (Exception exception)
            {
                Fail(exception);
                throw new IOException("WSL Touch Bar frame was not acknowledged.", exception);
            }
        }
    }

    private async Task ExchangeFrameAsync()
    {
        byte[] header = { (byte)'F', (byte)'R', (byte)'M', (byte)'1', 0, 0, 0, 0 };
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)frame.Length));
        using var timeout = new CancellationTokenSource(frameTimeout);
        var token = timeout.Token;
        await process.StandardInput.BaseStream.WriteAsync(header.AsMemory(), token).AsTask().WaitAsync(token).ConfigureAwait(false);
        await process.StandardInput.BaseStream.WriteAsync(frame.AsMemory(), token).AsTask().WaitAsync(token).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(token).WaitAsync(token).ConfigureAwait(false);
        byte[] acknowledgement = new byte[12];
        int received = 0;
        while (received < acknowledgement.Length)
        {
            int count = await process.StandardOutput.BaseStream.ReadAsync(acknowledgement.AsMemory(received), token).AsTask().WaitAsync(token).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("Bridge closed before the frame acknowledgement.");
            received += count;
        }
        ValidateAcknowledgement(acknowledgement, nextFrame);
        nextFrame = checked(nextFrame + 1);
    }

    internal static (int Width, int Height) ParseReady(string message)
    {
        string[] parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != "READY" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int height) ||
            width <= 0 || width > 4096 || height <= 0 || height > 256)
            throw new IOException("Bridge reported invalid display geometry.");
        return (width, height);
    }

    internal static TouchEventArgs ParseTouch(string message, int width, int height)
    {
        string[] parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "TOUCH" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int x) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int y) ||
            x < 0 || x >= width || y < 0 || y >= height)
            throw new IOException("Bridge reported invalid touch coordinates.");
        var kind = parts[3] switch
        {
            "DOWN" => TouchEventType.TouchDown,
            "MOVE" => TouchEventType.TouchMove,
            "UP" => TouchEventType.TouchUp,
            _ => throw new IOException("Bridge reported an invalid touch transition.")
        };
        return new TouchEventArgs(x, y, kind);
    }

    internal static void ValidateAcknowledgement(ReadOnlySpan<byte> bytes, ulong expected)
    {
        if (bytes.Length != 12 || bytes[0] != 'A' || bytes[1] != 'C' || bytes[2] != 'K' || bytes[3] != '1' ||
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(4)) != expected)
            throw new IOException("Bridge returned an invalid or stale frame acknowledgement.");
    }

    public void Dispose()
    {
        lock (ioLock)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            touchEvents.CompleteAdding();
            try
            {
                if (!process.HasExited)
                {
                    byte[] quit = { (byte)'Q', (byte)'U', (byte)'I', (byte)'T', 0, 0, 0, 0 };
                    process.StandardInput.BaseStream.WriteAsync(quit).AsTask().WaitAsync(TimeSpan.FromMilliseconds(500)).GetAwaiter().GetResult();
                }
            }
            catch { }
            try { process.StandardInput.Close(); } catch { }
            try
            {
                if (!process.WaitForExit(2000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1000);
                }
            }
            catch { }
            process.Dispose();
            // The touch callback can be waiting for TouchBar's view lock. Do not join
            // it here; it observes disposed and leaves after that callback unwinds.
        }
    }
}
