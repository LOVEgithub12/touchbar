using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using WindowsTouchBar.Device;

void Require(bool value, string message) { if (!value) throw new Exception(message); }
uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
async Task<byte[]> Exact(NetworkStream stream, int length)
{
    byte[] bytes = new byte[length]; int offset = 0;
    while (offset < length)
    { int read = await stream.ReadAsync(bytes.AsMemory(offset)); if (read == 0) throw new EndOfStreamException(); offset += read; }
    return bytes;
}
async Task<byte[]> Request(NetworkStream stream)
{
    byte[] header = await Exact(stream, 48);
    if (Read32(header, 0) == 1 && Read32(header, 12) == 0) await Exact(stream, (int)Read32(header, 24));
    return header;
}
async Task Reply(NetworkStream stream, byte[] request, byte[]? payload = null, int status = 0, bool unlink = false)
{
    byte[] header = new byte[48]; Write32(header, 0, unlink ? 4u : 3u); Write32(header, 4, Read32(request, 4));
    Write32(header, 20, unchecked((uint)status));
    bool input = Read32(request, 12) != 0;
    Write32(header, 24, unlink ? 0 : input ? (uint)(payload?.Length ?? 0) : Read32(request, 24));
    await stream.WriteAsync(header.AsMemory(0, 13));
    await stream.WriteAsync(header.AsMemory(13));
    if (payload is not null && input) await stream.WriteAsync(payload);
}
async Task Run(Func<UsbIpMultiplexer, Task> caller, Func<NetworkStream, Task> server)
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    try
    {
        using var tcp = new TcpClient();
        var accept = listener.AcceptTcpClientAsync();
        await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var peer = await accept;
        using var mux = UsbIpMultiplexer.FromConnectedTestPeer(tcp);
        await Task.WhenAll(caller(mux), server(peer.GetStream())).WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally { listener.Stop(); }
}

await Run(async mux =>
{
    Task<byte[]> touch = mux.ReadAsync(0x81, 64, TimeSpan.FromSeconds(2));
    Task<byte[]> display = mux.ReadAsync(0x82, 256, TimeSpan.FromSeconds(2));
    await mux.WriteAsync(1, new byte[] { 1, 2, 3 });
    Require((await display).SequenceEqual(new byte[] { 9, 8 }), "Display response misrouted.");
    Require((await touch).SequenceEqual(new byte[] { 7 }), "Touch response misrouted.");
}, async stream =>
{
    var requests = new[] { await Request(stream), await Request(stream), await Request(stream) };
    var write = requests.Single(r => Read32(r, 12) == 0);
    var display = requests.Single(r => Read32(r, 16) == 2);
    var touch = requests.Single(r => Read32(r, 16) == 1 && Read32(r, 12) == 1);
    await Reply(stream, write); await Reply(stream, display, new byte[] { 9, 8 }); await Reply(stream, touch, new byte[] { 7 });
});
Console.WriteLine("PASS: concurrent pending touch, display read, and display write route by sequence.");

await Run(async mux =>
{
    try { await mux.ReadAsync(0x81, 64, TimeSpan.FromMilliseconds(80)); throw new Exception("Timeout expected."); }
    catch (TimeoutException) { }
    await mux.WriteAsync(1, new byte[] { 4 });
}, async stream =>
{
    var original = await Request(stream); var unlink = await Request(stream);
    Require(Read32(unlink, 0) == 2 && Read32(unlink, 20) == Read32(original, 4), "UNLINK did not name its original request.");
    await Reply(stream, unlink, status: -104, unlink: true);
    await Reply(stream, await Request(stream));
});
Console.WriteLine("PASS: UNLINK cancellation win leaves socket usable.");

await Run(async mux =>
{
    try { await mux.ReadAsync(0x81, 64, TimeSpan.FromMilliseconds(80)); throw new Exception("Timeout expected."); }
    catch (TimeoutException) { }
    await mux.WriteAsync(1, new byte[] { 4 });
}, async stream =>
{
    var original = await Request(stream); var unlink = await Request(stream);
    await Reply(stream, original, new byte[] { 1 });
    await Reply(stream, unlink, status: 0, unlink: true);
    await Reply(stream, await Request(stream));
});
Console.WriteLine("PASS: normal completion winning UNLINK race leaves socket usable.");

byte[] rgba = { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 };
byte[] packet = UsbIpTouchBarTransport.BuildFrame(rgba, 2, 2, 0x123456789);
Require(packet.AsSpan(60, 12).SequenceEqual(new byte[] { 7, 8, 9, 1, 2, 3, 10, 11, 12, 4, 5, 6 }), "Framebuffer orientation or color order incorrect.");
Require(packet.Length % 16 == 0 && BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(60 + 12 + 32)) == 0x123456789, "Frame footer/alignment invalid.");
byte[] report = new byte[56]; report[0] = 0x11; report[1] = 0xff; report[2] = 0x7f; report[3] = 127;
Require(UsbIpTouchBarTransport.ParseTouchReport(report, 2008, 60) == (true, 2007, 59), "Touch maximum scaling mismatch.");
Array.Clear(report); Require(!UsbIpTouchBarTransport.ParseTouchReport(report, 2008, 60).Down, "Release report not recognized.");
Console.WriteLine("PASS: RGBA rotation, RGB order, timestamp footer, frame alignment, touch scaling, and release parsing. No USB devices accessed.");
