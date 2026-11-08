using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WindowsTouchBar.Device;

// USB/IP 1.1.1, local connection only. RET_UNLINK handling follows usbipd-win 5.3:
// -104 means cancellation won, 0 means the original completion was already sent.
internal sealed class UsbIpMultiplexer : IDisposable
{
    internal sealed record Device(string BusId, uint Bus, uint Number, ushort Vendor, ushort Product, byte Configurations);
    private sealed record Reply(int Status, int Actual, byte[] Bytes);
    private sealed class Pending
    {
        public readonly bool Input;
        public readonly int Length;
        public readonly uint? UnlinkTarget;
        public readonly TaskCompletionSource<Reply> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Pending(bool input, int length, uint? target = null) { Input = input; Length = length; UnlinkTarget = target; }
    }
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly uint deviceId;
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly ConcurrentDictionary<uint, Pending> pending = new();
    private int sequence;
    private int disposed;
    private Exception? failure;
    private const int MaximumTransfer = 4 * 1024 * 1024;
    public Device ImportedDevice { get; }

    private UsbIpMultiplexer(TcpClient client, Device device)
    {
        this.client = client; stream = client.GetStream(); ImportedDevice = device;
        deviceId = (device.Bus << 16) | device.Number;
        _ = Task.Run(ReadRepliesAsync);
    }

    public static UsbIpMultiplexer ImportUnique()
    {
        // Releasing the previous connection briefly resets/re-enumerates this
        // device. Give usbipd time to publish it again during an app restart.
        Device[] matches = Array.Empty<Device>();
        for (int attempt = 0; attempt < 21; attempt++)
        {
            matches = List().Where(d => d.Vendor == 0x05ac && d.Product == 0x8302).ToArray();
            if (matches.Length != 0 || attempt == 20) break;
            Thread.Sleep(500);
        }
        if (matches.Length != 1) throw new IOException("Expected exactly one shared Touch Bar 05ac:8302 in local usbipd after waiting for device enumeration.");
        var expected = matches[0];
        var tcp = Connect();
        try
        {
            var stream = tcp.GetStream(); byte[] request = new byte[40];
            Write16(request, 0, 0x111); Write16(request, 2, 0x8003);
            Encoding.ASCII.GetBytes(expected.BusId).CopyTo(request, 8);
            stream.Write(request); ValidateOperation(ReadExact(stream, 8), 3);
            var actual = ParseDevice(ReadExact(stream, 312));
            if (actual != expected || actual.Bus > ushort.MaxValue || actual.Number > ushort.MaxValue)
                throw new IOException("Touch Bar identity changed during USB/IP import.");
            return new UsbIpMultiplexer(tcp, actual);
        }
        catch { tcp.Dispose(); throw; }
    }

    internal static UsbIpMultiplexer FromConnectedTestPeer(TcpClient tcp) =>
        new(tcp, new Device("1-6", 1, 6, 0x05ac, 0x8302, 2));

    private static List<Device> List()
    {
        using var tcp = Connect(); var stream = tcp.GetStream(); byte[] request = new byte[8];
        Write16(request, 0, 0x111); Write16(request, 2, 0x8005); stream.Write(request);
        ValidateOperation(ReadExact(stream, 8), 5);
        uint count = Read32(ReadExact(stream, 4), 0);
        if (count > 256) throw new IOException("Invalid USB/IP device count.");
        var result = new List<Device>();
        for (int i = 0; i < count; i++)
        {
            byte[] descriptor = ReadExact(stream, 312);
            result.Add(ParseDevice(descriptor)); ReadExact(stream, descriptor[311] * 4);
        }
        return result;
    }

    public async Task<byte[]> ControlAsync(byte type, byte request, ushort value, ushort index, ushort length)
    {
        byte[] setup = new byte[8]; setup[0] = type; setup[1] = request;
        BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(2), value);
        BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(4), index);
        BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(6), length);
        return await SubmitAsync(0, (type & 0x80) != 0, length, null, setup, 0, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    public Task<byte[]> ReadAsync(byte endpoint, int capacity, TimeSpan timeout, CancellationToken token = default) =>
        SubmitAsync((byte)(endpoint & 0x7f), true, capacity, null, null, endpoint == 0x81 ? 1 : 0, timeout, token);
    public async Task WriteAsync(byte endpoint, byte[] data) =>
        _ = await SubmitAsync((byte)(endpoint & 0x7f), false, data.Length, data, null, 0, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

    private async Task<byte[]> SubmitAsync(byte endpoint, bool input, int length, byte[]? output, byte[]? setup,
        int interval, TimeSpan timeout, CancellationToken token = default)
    {
        ThrowIfUnavailable();
        if (endpoint > 15 || length < 0 || length > MaximumTransfer || (!input && length != (output?.Length ?? 0)))
            throw new ArgumentException("Invalid USB/IP transfer dimensions.");
        uint seq = NextSequence(); var item = new Pending(input, length);
        if (!pending.TryAdd(seq, item)) throw new IOException("USB/IP sequence collision.");
        byte[] header = Header(1, seq); Write32(header, 12, input ? 1u : 0u); Write32(header, 16, endpoint);
        Write32(header, 24, (uint)length); Write32(header, 32, uint.MaxValue); Write32(header, 36, (uint)interval);
        setup?.CopyTo(header, 40);
        try
        {
            try { await SendAsync(header, output).ConfigureAwait(false); }
            catch (Exception exception) { Fail(exception); throw; }
            Reply reply;
            try { reply = await item.Completion.Task.WaitAsync(timeout, token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                await CancelAsync(seq, item).ConfigureAwait(false);
                throw;
            }
            if (reply.Status != 0) throw new IOException($"USB/IP endpoint 0x{(endpoint | (input ? 0x80 : 0)):X2} failed with status {reply.Status}.");
            if (!input && reply.Actual != length) throw new IOException($"Short USB/IP write: {reply.Actual}/{length}.");
            return reply.Bytes;
        }
        catch (Exception exception) when (exception is not TimeoutException && exception is not OperationCanceledException)
        { Fail(exception); throw; }
    }

    private async Task CancelAsync(uint target, Pending original)
    {
        if (original.Completion.Task.IsCompleted) return;
        uint seq = NextSequence(); var unlink = new Pending(false, 0, target);
        pending.TryAdd(seq, unlink);
        byte[] header = Header(2, seq); Write32(header, 20, target);
        try
        {
            await SendAsync(header, null).ConfigureAwait(false);
            Reply result = await unlink.Completion.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (result.Status != -104 && result.Status != 0) throw new IOException($"USB/IP unlink failed: {result.Status}.");
            // A zero reply loses the race; the original RET_SUBMIT precedes it on the wire.
            if (result.Status == 0 && !original.Completion.Task.IsCompleted)
                throw new IOException("USB/IP unlink race did not include the original completion.");
        }
        catch (Exception exception) { Fail(exception); throw new IOException("USB/IP cancellation did not finish.", exception); }
    }

    private async Task SendAsync(byte[] header, byte[]? payload)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await writer.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            await stream.WriteAsync(header.AsMemory(), deadline.Token).ConfigureAwait(false);
            if (payload is { Length: > 0 }) await stream.WriteAsync(payload.AsMemory(), deadline.Token).ConfigureAwait(false);
        }
        finally { writer.Release(); }
    }

    private async Task ReadRepliesAsync()
    {
        try
        {
            while (Volatile.Read(ref disposed) == 0)
            {
                byte[] header = await ReadExactAsync(stream, 48).ConfigureAwait(false);
                uint command = Read32(header, 0), seq = Read32(header, 4);
                if (!pending.TryGetValue(seq, out var item)) throw new IOException("USB/IP reply has no matching request.");
                int status = unchecked((int)Read32(header, 20));
                if (item.UnlinkTarget is { } target)
                {
                    if (command != 4) throw new IOException("Expected RET_UNLINK.");
                    if (status == -104 && pending.TryRemove(target, out var original))
                        original.Completion.TrySetResult(new Reply(-104, 0, Array.Empty<byte>()));
                    pending.TryRemove(seq, out _);
                    item.Completion.TrySetResult(new Reply(status, 0, Array.Empty<byte>()));
                    continue;
                }
                if (command != 3) throw new IOException("Expected RET_SUBMIT.");
                int actual = unchecked((int)Read32(header, 24)), packets = unchecked((int)Read32(header, 32));
                if (actual < 0 || actual > item.Length || (packets != 0 && packets != -1))
                    throw new IOException("Invalid USB/IP completion length.");
                byte[] data = item.Input ? await ReadExactAsync(stream, actual).ConfigureAwait(false) : Array.Empty<byte>();
                pending.TryRemove(seq, out _);
                item.Completion.TrySetResult(new Reply(status, actual, data));
            }
        }
        catch (Exception exception) { if (Volatile.Read(ref disposed) == 0) Fail(exception); }
    }

    private void Fail(Exception error)
    {
        Interlocked.CompareExchange(ref failure, error, null);
        client.Dispose();
        foreach (var pair in pending)
            if (pending.TryRemove(pair.Key, out var item)) item.Completion.TrySetException(error);
    }
    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(UsbIpMultiplexer));
        if (Volatile.Read(ref failure) is { } error) throw new IOException("USB/IP connection failed.", error);
    }
    private uint NextSequence()
    {
        int value = Interlocked.Increment(ref sequence);
        if (value <= 0) throw new IOException("USB/IP sequence exhausted; restart the app.");
        return (uint)value;
    }
    private byte[] Header(uint command, uint seq)
    { var bytes = new byte[48]; Write32(bytes, 0, command); Write32(bytes, 4, seq); Write32(bytes, 8, deviceId); return bytes; }
    private static TcpClient Connect()
    {
        var tcp = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true, ReceiveTimeout = 10000, SendTimeout = 10000 };
        try
        {
            using var token = new CancellationTokenSource(10000);
            tcp.ConnectAsync(IPAddress.Loopback, 3240, token.Token).AsTask().GetAwaiter().GetResult();
            return tcp;
        }
        catch { tcp.Dispose(); throw; }
    }
    private static Device ParseDevice(byte[] data) => new(Encoding.ASCII.GetString(data, 256, 32).TrimEnd('\0'),
        Read32(data, 288), Read32(data, 292), BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(300)),
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(302)), data[310]);
    private static void ValidateOperation(byte[] data, ushort expected)
    {
        if (BinaryPrimitives.ReadUInt16BigEndian(data) != 0x111 || BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2)) != expected || Read32(data, 4) != 0)
            throw new IOException("Local usbipd rejected the operation; ensure the Touch Bar is shared and idle.");
    }
    private static byte[] ReadExact(Stream stream, int length)
    {
        byte[] bytes = new byte[length]; int offset = 0;
        while (offset < length) { int read = stream.Read(bytes, offset, length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
        return bytes;
    }
    private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        byte[] bytes = new byte[length]; int offset = 0;
        while (offset < length) { int read = await stream.ReadAsync(bytes.AsMemory(offset)).ConfigureAwait(false); if (read == 0) throw new EndOfStreamException(); offset += read; }
        return bytes;
    }
    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
    private static void Write16(byte[] data, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value);
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) Fail(new ObjectDisposedException(nameof(UsbIpMultiplexer)));
    }
}
