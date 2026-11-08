using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowsTouchBar.Device;

if (args.Length == 2 && args[0] == "--fake")
{
    string mode = args[1];
    Console.Error.WriteLine("Touch input interface ready.");
    Console.Error.WriteLine(mode == "bad-size" ? "READY 9999999 60" : "READY 8 2");
    Stream input = Console.OpenStandardInput(), output = Console.OpenStandardOutput();
    ulong sequence = 1;
    while (true)
    {
        byte[] header = new byte[8];
        if (!ReadExact(input, header)) break;
        if (header[0] == 'Q') break;
        if (header[0] != 'F' || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != 64) return 5;
        byte[] receivedPixels = new byte[64];
        if (!ReadExact(input, receivedPixels) || receivedPixels.Where((value, index) => value != index).Any()) return 6;
        Console.Error.WriteLine("TOUCH 3 1 DOWN");
        Console.Error.WriteLine("TOUCH 3 1 UP");
        if (mode == "no-ack") { await Task.Delay(5000); break; }
        byte[] ack = { (byte)'A', (byte)'C', (byte)'K', (byte)'1', 0, 0, 0, 0, 0, 0, 0, 0 };
        BinaryPrimitives.WriteUInt64LittleEndian(ack.AsSpan(4), mode == "stale-ack" ? 99 : sequence++);
        await output.WriteAsync(ack.AsMemory(0, 4));
        await output.FlushAsync();
        await Task.Delay(15);
        await output.WriteAsync(ack.AsMemory(4));
        await output.FlushAsync();
    }
    return 0;
}

int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Reject(Action action, string message)
{
    try { action(); } catch (IOException) { return; }
    throw new Exception(message);
}
WslTouchBarBridge OpenFake(string mode) => WslTouchBarBridge.Open(
    new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--fake", mode } },
    TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(500));

var pixels = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
IntPtr address = Marshal.AllocHGlobal(pixels.Length);
Marshal.Copy(pixels, 0, address, pixels.Length);
try
{
    using (var bridge = OpenFake("normal"))
    {
        using var touchSeen = new ManualResetEventSlim();
        int down = 0, up = 0;
        bridge.Event += touch =>
        {
            if (touch.TouchEventType == WindowsTouchBar.Events.TouchEventType.TouchDown) Interlocked.Increment(ref down);
            if (touch.TouchEventType == WindowsTouchBar.Events.TouchEventType.TouchUp) { Interlocked.Increment(ref up); touchSeen.Set(); }
        };
        Check(bridge.Width == 8 && bridge.Height == 2, "READY geometry mismatch");
        bridge.SubmitRgbaFrame(address, pixels.Length);
        Check(touchSeen.Wait(1000) && down == 1 && up == 1, "Touch messages blocked ACK reception or lost transitions");
        bridge.SubmitRgbaFrame(address, pixels.Length);
    }
    passed++;
    using (var bridge = OpenFake("stale-ack"))
        Reject(() => bridge.SubmitRgbaFrame(address, pixels.Length), "A stale ACK was accepted");
    passed++;
    using (var bridge = OpenFake("no-ack"))
    {
        var timer = Stopwatch.StartNew();
        Reject(() => bridge.SubmitRgbaFrame(address, pixels.Length), "Missing ACK did not fail");
        Check(timer.ElapsedMilliseconds < 2000, "Missing ACK did not respect the timeout");
    }
    passed++;
    Reject(() => { using var bridge = OpenFake("bad-size"); }, "Oversize geometry accepted");
    Reject(() => WslTouchBarBridge.ParseTouch("TOUCH 8 1 DOWN", 8, 2), "Out-of-range touch accepted");
    Reject(() => WslTouchBarBridge.ParseTouch("TOUCH 1 1 PRESSED", 8, 2), "Unknown transition accepted");
    passed++;
    Console.WriteLine($"PASS {passed}/4: real process pipes, split ACK, ordered touch delivery, stale ACK rejection, ACK timeout, geometry and touch bounds. No WSL or hardware accessed.");
    return 0;
}
finally { Marshal.FreeHGlobal(address); }

static bool ReadExact(Stream stream, byte[] bytes)
{
    int offset = 0;
    while (offset < bytes.Length)
    {
        int count = stream.Read(bytes, offset, bytes.Length - offset);
        if (count == 0) return false;
        offset += count;
    }
    return true;
}
