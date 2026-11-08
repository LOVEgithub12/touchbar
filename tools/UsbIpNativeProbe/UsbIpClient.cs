using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TouchBarUsbIp;

// Minimal sequential USB/IP 1.1.1 client. Protocol references are in README.md.
// Connection is deliberately restricted to loopback and VID/PID 05ac:8302.
public sealed class UsbIpClient : IDisposable
{
    public sealed record Device(string BusId, uint BusNumber, uint DeviceNumber, ushort VendorId,
        ushort ProductId, byte Configuration, byte ConfigurationCount);
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private uint sequence;
    private readonly uint deviceId;
    private bool disposed;
    public Device ImportedDevice { get; }
    public Action<string>? Trace { get; set; }
    public const int MaximumTransfer = 4 * 1024 * 1024;
    public const int IoTimeoutMs = 10000;

    private UsbIpClient(TcpClient client, Device device)
    {
        this.client = client;
        stream = client.GetStream();
        stream.ReadTimeout = IoTimeoutMs;
        stream.WriteTimeout = IoTimeoutMs;
        ImportedDevice = device;
        deviceId = (device.BusNumber << 16) | device.DeviceNumber;
    }

    public static UsbIpClient Import(string busId)
    {
        ValidateBusId(busId);
        // DEVLIST verifies the target before IMPORT changes device ownership.
        var found = List().SingleOrDefault(d => d.BusId == busId)
            ?? throw new IOException("The exact bus ID is not exported by local usbipd.");
        ValidateIdentity(found);
        var tcp = Connect();
        try
        {
            var stream = tcp.GetStream();
            byte[] request = new byte[40];
            Write16(request, 0, 0x0111); Write16(request, 2, 0x8003);
            Encoding.ASCII.GetBytes(busId).CopyTo(request, 8);
            stream.Write(request);
            ValidateOperation(ReadExact(stream, 8), 3);
            var imported = ParseDevice(ReadExact(stream, 312));
            ValidateIdentity(imported);
            if (imported.BusId != busId || imported.BusNumber != found.BusNumber || imported.DeviceNumber != found.DeviceNumber)
                throw new IOException("Device identity changed during IMPORT.");
            return new UsbIpClient(tcp, imported);
        }
        catch { tcp.Dispose(); throw; }
    }

    public static IReadOnlyList<Device> List()
    {
        using var tcp = Connect();
        var stream = tcp.GetStream();
        byte[] request = new byte[8];
        Write16(request, 0, 0x0111); Write16(request, 2, 0x8005);
        stream.Write(request);
        ValidateOperation(ReadExact(stream, 8), 5);
        uint count = Read32(ReadExact(stream, 4), 0);
        if (count > 256) throw new IOException("USB/IP device count exceeds limit.");
        var devices = new List<Device>();
        for (uint i = 0; i < count; i++)
        {
            byte[] descriptor = ReadExact(stream, 312);
            devices.Add(ParseDevice(descriptor));
            ReadExact(stream, descriptor[311] * 4);
        }
        return devices;
    }

    public byte[] Control(byte requestType, byte request, ushort value, ushort index, ushort length, byte[]? output = null)
    {
        bool input = (requestType & 0x80) != 0;
        byte[] setup = new byte[8];
        setup[0] = requestType; setup[1] = request;
        BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(2), value);
        BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(4), index);
        BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(6), length);
        return Submit(0, input, length, output, setup, 0);
    }

    public byte GetConfiguration()
    {
        var result = Control(0x80, 8, 0, 0, 1);
        if (result.Length != 1) throw new IOException("GET_CONFIGURATION returned an invalid length.");
        return result[0];
    }
    public void SetConfiguration(byte configuration)
    {
        if (configuration != 0 && configuration != 2) throw new ArgumentOutOfRangeException(nameof(configuration));
        Control(0, 9, configuration, 0, 0);
    }
    public void SetDisplayInterface() => Control(1, 11, 0, 1, 0);
    public void ClearEndpointHalt(byte endpoint)
    {
        if (endpoint != 0x01 && endpoint != 0x82) throw new ArgumentOutOfRangeException(nameof(endpoint));
        Control(2, 1, 0, endpoint, 0);
    }
    public byte[] BulkRead(byte endpoint, int capacity) => Submit((byte)(endpoint & 0x7f), true, capacity, null, null, 0);
    public void BulkWrite(byte endpoint, byte[] bytes) => Submit((byte)(endpoint & 0x7f), false, bytes.Length, bytes, null, 0);

    public byte[] Submit(byte endpoint, bool input, int length, byte[]? output, byte[]? setup, int interval)
    {
        if (disposed) throw new ObjectDisposedException(nameof(UsbIpClient));
        if (endpoint > 15 || length < 0 || length > MaximumTransfer) throw new ArgumentOutOfRangeException(nameof(length));
        if (!input && length != (output?.Length ?? 0)) throw new ArgumentException("OUT payload length mismatch.");
        if (setup != null && setup.Length != 8) throw new ArgumentException("Invalid USB setup size.");
        uint current = checked(++sequence);
        byte[] header = new byte[48];
        Write32(header, 0, 1); Write32(header, 4, current); Write32(header, 8, deviceId);
        Write32(header, 12, input ? 1u : 0u); Write32(header, 16, endpoint);
        Write32(header, 24, checked((uint)length));
        Write32(header, 32, uint.MaxValue); // -1, non-isochronous per USB/IP spec.
        Write32(header, 36, unchecked((uint)interval));
        setup?.CopyTo(header, 40);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Trace?.Invoke($"SUBMIT seq={current} ep=0x{(endpoint | (input ? 0x80 : 0)):X2} length={length} interval={interval} setup={(setup == null ? "none" : Convert.ToHexString(setup))}");
            stream.Write(header);
            if (!input && output is { Length: > 0 }) stream.Write(output);
            byte[] reply = ReadExact(stream, 48);
            if (Read32(reply, 0) != 3 || Read32(reply, 4) != current)
                throw new IOException("Unexpected USB/IP reply command or sequence.");
            // usbipd v5.3.0 deliberately sends zeros for devid/direction/ep in replies.
            int status = unchecked((int)Read32(reply, 20));
            int actual = unchecked((int)Read32(reply, 24));
            int packets = unchecked((int)Read32(reply, 32));
            if (actual < 0 || actual > length || (packets != 0 && packets != -1))
                throw new IOException("USB/IP reply transfer dimensions are invalid.");
            byte[] payload = input ? ReadExact(stream, actual) : Array.Empty<byte>();
            Trace?.Invoke($"RET_SUBMIT seq={current} status={status} bytes={actual} elapsedMs={watch.ElapsedMilliseconds}");
            if (status != 0) throw new IOException($"USB/IP URB failed: status={status}, endpoint=0x{(endpoint | (input ? 0x80 : 0)):X2}, bytes={actual}.");
            if (!input && actual != length) throw new IOException($"Short USB/IP write: {actual}/{length} bytes.");
            return payload;
        }
        catch { Dispose(); throw; }
    }

    private static TcpClient Connect()
    {
        var tcp = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true, ReceiveTimeout = IoTimeoutMs, SendTimeout = IoTimeoutMs };
        try
        {
            using var timeout = new CancellationTokenSource(IoTimeoutMs);
            tcp.ConnectAsync(IPAddress.Loopback, 3240, timeout.Token).AsTask().GetAwaiter().GetResult();
            var stream = tcp.GetStream(); stream.ReadTimeout = IoTimeoutMs; stream.WriteTimeout = IoTimeoutMs;
            return tcp;
        }
        catch { tcp.Dispose(); throw; }
    }
    private static void ValidateBusId(string busId)
    {
        if (!Regex.IsMatch(busId, "^[1-9][0-9]{0,4}-[1-9][0-9]{0,4}$")) throw new ArgumentException("Specify an exact usbipd bus ID, for example 1-6.");
    }
    private static void ValidateIdentity(Device device)
    {
        if (device.VendorId != 0x05ac || device.ProductId != 0x8302 || device.BusNumber > 65535 || device.DeviceNumber > 65535)
            throw new IOException("Refusing a device other than Touch Bar 05ac:8302.");
    }
    private static Device ParseDevice(byte[] d) => new(
        Encoding.ASCII.GetString(d, 256, 32).TrimEnd('\0'), Read32(d, 288), Read32(d, 292),
        BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(300)), BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(302)), d[309], d[310]);
    private static void ValidateOperation(byte[] reply, ushort expected)
    {
        if (BinaryPrimitives.ReadUInt16BigEndian(reply) != 0x0111 || BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(2)) != expected)
            throw new IOException("Unexpected USB/IP version or operation reply.");
        uint status = Read32(reply, 4);
        if (status != 0) throw new IOException($"USB/IP operation rejected: status={status} (2=busy, 3=device error, 4=not found).");
    }
    private static byte[] ReadExact(Stream stream, int length)
    {
        if (length < 0 || length > MaximumTransfer) throw new IOException("Network response exceeds size limit.");
        byte[] bytes = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int n = stream.Read(bytes, offset, length - offset);
            if (n == 0) throw new EndOfStreamException("USB/IP peer closed the connection.");
            offset += n;
        }
        return bytes;
    }
    private static uint Read32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
    private static void Write32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
    private static void Write16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), value);
    public void Dispose() { if (!disposed) { disposed = true; client.Dispose(); } }
}
