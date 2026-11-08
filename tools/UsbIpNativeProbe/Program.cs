// SPDX-License-Identifier: GPL-2.0-only
// Display packet composition follows the TonyTouchBar bridge and appletbdrm protocol.
// Protocol credit: Kerem Karabay and Linux appletbdrm contributors. See README.md.
using System.Buffers.Binary;
using System.Text.Json;
using TouchBarUsbIp;

if (args.Length == 0 || args.SequenceEqual(new[] { "--help" }))
{
    Console.WriteLine("UsbIpNativeProbe --busid <exact-id> --test-frame [--skip-set-interface] [--clear-pipes] [--hold-seconds 1..120]\nUses local usbipd at 127.0.0.1:3240; only imports VID/PID 05ac:8302.\nReads configuration descriptors, selects configuration 2, performs GINF, and requires an ACK for a color/checker image.\n--skip-set-interface matches libusb claim behavior; --clear-pipes clears display endpoint halts like DFR kernel pipe reset.\n--hold-seconds keeps ownership after the frame ACK for physical observation.\nNo driver installation or keyboard events. Closing releases USB/IP ownership.");
    return;
}
int holdSeconds = 0;
var extraArguments = args.Skip(3).ToList();
int holdIndex = extraArguments.IndexOf("--hold-seconds");
bool validHold = true;
if (holdIndex >= 0)
{
    validHold = holdIndex + 1 < extraArguments.Count && int.TryParse(extraArguments[holdIndex + 1], out holdSeconds) && holdSeconds >= 1 && holdSeconds <= 120;
    if (validHold) extraArguments.RemoveRange(holdIndex, 2);
}
if (args.Length < 3 || args[0] != "--busid" || args[2] != "--test-frame" || !validHold ||
    extraArguments.Any(a => a != "--skip-set-interface" && a != "--clear-pipes") || extraArguments.Distinct().Count() != extraArguments.Count)
{ Console.Error.WriteLine("Use --help."); Environment.ExitCode = 64; return; }

void Report(object result) => Console.WriteLine(JsonSerializer.Serialize(result));
string stage = "Import";
try
{
    using var usb = UsbIpClient.Import(args[1]);
    usb.Trace = message => Report(new { Stage = "URB", Detail = message });
    Report(new { Stage = stage, Device = usb.ImportedDevice });
    stage = "ConfigurationDescriptors";
    VerifyDisplayEndpoints(usb, Report);
    stage = "GetConfiguration";
    byte configuration = usb.GetConfiguration();
    Report(new { Stage = stage, Configuration = configuration });
    // SET_CONFIGURATION is required even if already configured: VBoxUSB uses it
    // to establish the endpoint handles in this imported session.
    if (configuration != 2)
    {
        stage = "SetConfiguration0"; usb.SetConfiguration(0); Report(new { Stage = stage, Completed = true }); Thread.Sleep(1000);
    }
    stage = "SetConfiguration2"; usb.SetConfiguration(2); Report(new { Stage = stage, Completed = true }); Thread.Sleep(2000);
    stage = "VerifyConfiguration";
    if (usb.GetConfiguration() != 2) throw new IOException("Display configuration 2 was not selected.");
    Report(new { Stage = stage, Configuration = 2, Completed = true });
    if (!args.Contains("--skip-set-interface"))
    { stage = "SelectDisplayInterface"; usb.SetDisplayInterface(); Report(new { Stage = stage, Completed = true }); }
    if (args.Contains("--clear-pipes"))
    {
        stage = "ClearDisplayOUT"; usb.ClearEndpointHalt(0x01); Report(new { Stage = stage, Completed = true });
        stage = "ClearDisplayIN"; usb.ClearEndpointHalt(0x82); Report(new { Stage = stage, Completed = true });
    }
    stage = "GINFWrite"; usb.BulkWrite(0x01, Request(0x47494e46));
    Report(new { Stage = stage, Completed = true });
    stage = "GINFRead"; byte[] info = Array.Empty<byte>();
    for (int responseIndex = 0; responseIndex < 4; responseIndex++)
    {
        byte[] response = usb.BulkRead(0x82, 256);
        Report(new { Stage = "GINFResponse", Index = responseIndex, Length = response.Length, Hex = Convert.ToHexString(response) });
        if (response.Length >= 65 && ReadLittle(response, 0) == 0x01140000 && ReadLittle(response, 16) == 0x47494e46)
        { info = response; break; }
    }
    if (info.Length < 65) throw new IOException("No valid full GINF response in four device replies.");
    uint width = ReadLittle(info, 32), height = ReadLittle(info, 36), format = ReadLittle(info, 53);
    if (width == 0 || height == 0 || width > 4096 || height > 256 || format != 0x52474241)
        throw new IOException($"Unsupported geometry/format: {width}x{height}, 0x{format:X8}.");
    Report(new { Stage = "GINF", Width = width, Height = height, Format = $"0x{format:X8}" });
    stage = "REDY"; usb.BulkWrite(0x01, Request(0x52454459));
    stage = "CLDR"; usb.BulkWrite(0x01, Request(0x434c4452));
    stage = "FrameWrite"; usb.BulkWrite(0x01, TestFrame((ushort)width, (ushort)height, 1));
    stage = "FrameACK";
    bool matched = false;
    for (int i = 0; i < 4; i++)
    {
        byte[] ack = usb.BulkRead(0x82, 256);
        Report(new { Stage = "FrameACKResponse", Index = i, Length = ack.Length, Hex = Convert.ToHexString(ack) });
        if (ack.Length >= 40 && ReadLittle(ack, 0) == 0x01140000 && ReadLittle(ack, 16) == 0x5544434c &&
            BinaryPrimitives.ReadUInt64LittleEndian(ack.AsSpan(32)) == 1) { matched = true; break; }
    }
    if (!matched) throw new IOException("No matching UDCL acknowledgement for timestamp 1.");
    Report(new { Stage = "TEST_FRAME_ACK", Timestamp = 1, Width = width, Height = height, Pattern = "color-bars-and-checkerboard", PhysicalDisplayNeedsConfirmation = true });
    if (holdSeconds > 0)
    {
        Report(new { Stage = "HOLD_BEGIN", Seconds = holdSeconds, Note = "Keeping the same USB/IP connection open for display observation." });
        Thread.Sleep(TimeSpan.FromSeconds(holdSeconds));
        Report(new { Stage = "HOLD_END" });
    }
}
catch (Exception ex)
{ Report(new { Stage = stage, Error = ex.ToString() }); Environment.ExitCode = 1; }

static byte[] Request(uint key)
{
    byte[] packet = new byte[32];
    WriteLittle(packet, 0, 0x15120002); WriteLittle(packet, 12, 16); WriteLittle(packet, 16, key); WriteLittle(packet, 28, 16);
    return packet;
}
static void VerifyDisplayEndpoints(UsbIpClient usb, Action<object> report)
{
    int count = usb.ImportedDevice.ConfigurationCount;
    if (count < 2 || count > 8) throw new IOException("Unexpected USB configuration count.");
    bool found = false;
    for (int index = 0; index < count; index++)
    {
        ushort selector = (ushort)(0x0200 | index);
        byte[] head = usb.Control(0x80, 6, selector, 0, 9);
        if (head.Length != 9 || head[0] != 9 || head[1] != 2) throw new IOException("Invalid configuration header.");
        ushort total = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(2));
        if (total < 9 || total > 4096) throw new IOException("Invalid configuration total length.");
        byte[] descriptor = usb.Control(0x80, 6, selector, 0, total);
        if (descriptor.Length != total || descriptor[1] != 2 || descriptor[5] != head[5]) throw new IOException("Configuration descriptor changed.");
        report(new { Stage = "ConfigurationDescriptor", Index = index, ConfigurationValue = descriptor[5], Interfaces = descriptor[4], TotalLength = total });
        bool display = false, bulkIn = false, bulkOut = false;
        for (int offset = 9; offset < descriptor.Length;)
        {
            int length = descriptor[offset];
            if (length < 2 || offset + length > descriptor.Length) throw new IOException("Malformed USB descriptor chain.");
            byte type = descriptor[offset + 1];
            if (type == 4)
            {
                if (length < 9) throw new IOException("Truncated USB interface descriptor.");
                display = descriptor[5] == 2 && descriptor[offset + 2] == 1 && descriptor[offset + 3] == 0;
                report(new { Stage = "InterfaceDescriptor", Configuration = descriptor[5], Number = descriptor[offset + 2], Alternate = descriptor[offset + 3], Endpoints = descriptor[offset + 4], Class = descriptor[offset + 5] });
            }
            else if (type == 5)
            {
                if (length < 7) throw new IOException("Truncated USB endpoint descriptor.");
                byte address = descriptor[offset + 2], attributes = descriptor[offset + 3];
                ushort packetSize = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.AsSpan(offset + 4));
                report(new { Stage = "EndpointDescriptor", Configuration = descriptor[5], Address = $"0x{address:X2}", Attributes = attributes, MaximumPacketSize = packetSize, Interval = descriptor[offset + 6] });
                if (display && (attributes & 3) == 2 && packetSize > 0)
                { bulkIn |= address == 0x82; bulkOut |= address == 0x01; }
            }
            offset += length;
        }
        if (descriptor[5] == 2) found = bulkIn && bulkOut;
    }
    if (!found) throw new IOException("Configuration 2 does not expose display interface 1 alt 0 with bulk OUT01/IN82.");
}
static byte[] TestFrame(ushort width, ushort height, ulong timestamp)
{
    int rgbLength = checked(width * height * 3), footer = 60 + rgbLength;
    int total = (footer + 80 + 15) & ~15;
    byte[] packet = new byte[total];
    WriteLittle(packet, 0, 0x00120002); WriteLittle(packet, 4, 9); WriteLittle(packet, 12, (uint)(total - 16));
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16), 1); packet[18] = (byte)timestamp;
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(52), width); BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(54), height);
    WriteLittle(packet, 56, (uint)rgbLength);
    byte[,] colors = { {255,255,255}, {255,230,0}, {0,220,255}, {0,220,70}, {255,0,220}, {255,40,30}, {40,80,255}, {24,24,24} };
    for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
    {
        int screenY = height - 1 - y, target = 60 + (x * height + y) * 3;
        if (screenY >= height * 2 / 3)
        { byte checker = ((x / 12 + screenY / 6) & 1) != 0 ? (byte)255 : (byte)0; packet[target] = packet[target + 1] = packet[target + 2] = checker; }
        else for (int channel = 0; channel < 3; channel++) packet[target + channel] = colors[x * 8 / width, channel];
    }
    WriteLittle(packet, footer + 12, 0xfffe); WriteLittle(packet, footer + 28, 0x80001);
    BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(footer + 32), timestamp);
    WriteLittle(packet, footer + 52, 0x80002); WriteLittle(packet, footer + 76, 0xffff);
    return packet;
}
static uint ReadLittle(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
static void WriteLittle(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
