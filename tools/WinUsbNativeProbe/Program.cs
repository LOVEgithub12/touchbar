using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using WindowsTouchBar.Device;

if (args.Length != 1 || args[0] != "--ginf")
{
    Console.WriteLine("WinUsbNativeProbe --ginf: one GINF exchange using unmanaged memory; no driver changes.");
    return;
}
if (System.Diagnostics.Process.GetProcessesByName("WindowsTouchBar").Length != 0)
    throw new InvalidOperationException("Stop WindowsTouchBar before probing.");
if (IntPtr.Size != 8) throw new InvalidOperationException("This diagnostic is x64 only.");

IntPtr file = new(-1), usb = IntPtr.Zero, ev = IntPtr.Zero;
IntPtr payload = IntPtr.Zero, overlapped = IntPtr.Zero, count = IntPtr.Zero;
bool retained = false;
void Check(bool ok, string action) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), action); }
void Report(object result) => Console.WriteLine(JsonSerializer.Serialize(result));
try
{
    string path = Locator.FindDfrDevice() ?? throw new InvalidOperationException("Display interface absent.");
    if (!path.Contains("vid_05ac&pid_8302&mi_01", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Unexpected device path; refusing access.");
    file = Native.CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
    Check(file != new IntPtr(-1), "CreateFileW");
    Check(Native.WinUsb_Initialize(file, out usb), "WinUsb_Initialize");
    Check(Native.WinUsb_QueryInterfaceSettings(usb, 0, out var descriptor), "QueryInterfaceSettings");
    Report(new { Stage = "Interface", descriptor.InterfaceNumber, descriptor.AlternateSetting,
        descriptor.NumEndpoints, descriptor.InterfaceClass, descriptor.InterfaceSubClass,
        descriptor.InterfaceProtocol, PointerSize = IntPtr.Size, PipeInfoSize = Marshal.SizeOf<Native.Pipe>() });
    byte outPipe = 0, inPipe = 0;
    for (byte i = 0; i < descriptor.NumEndpoints; i++)
    {
        Check(Native.WinUsb_QueryPipe(usb, 0, i, out var pipe), "QueryPipe");
        Report(new { Stage = "Pipe", pipe.PipeType, pipe.PipeId, pipe.MaximumPacketSize, pipe.Interval });
        if (pipe.PipeType == 2) { if ((pipe.PipeId & 0x80) != 0) inPipe = pipe.PipeId; else outPipe = pipe.PipeId; }
    }
    if (outPipe == 0 || inPipe == 0) throw new InvalidOperationException("Missing bulk endpoint pair.");
    uint timeout = 3000;
    Check(Native.WinUsb_SetPipePolicy(usb, outPipe, 3, 4, ref timeout), "Set OUT timeout");
    Check(Native.WinUsb_SetPipePolicy(usb, inPipe, 3, 4, ref timeout), "Set IN timeout");
    ev = Native.CreateEventW(IntPtr.Zero, true, false, null);
    Check(ev != IntPtr.Zero, "CreateEventW");
    payload = Marshal.AllocHGlobal(4096);
    overlapped = Marshal.AllocHGlobal(32);
    count = Marshal.AllocHGlobal(4);
    byte[] request = new byte[32];
    BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(0), 0x15120002);
    BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16), 0x47494E46);
    BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(28), 16);
    Marshal.Copy(request, 0, payload, request.Length);
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    byte[] actual = new byte[request.Length];
    Marshal.Copy(payload, actual, 0, actual.Length);
    if (!actual.SequenceEqual(request)) throw new InvalidOperationException("Unmanaged request verification failed.");
    Report(new { Stage = "BufferVerifiedAfterGC", Bytes = actual.Length, Allocation = "AllocHGlobal", Request = Convert.ToHexString(actual) });

    bool Transfer(bool write, byte pipe, uint length)
    {
        for (int i = 0; i < 32; i++) Marshal.WriteByte(overlapped, i, 0);
        Marshal.WriteIntPtr(overlapped, 24, ev);
        Marshal.WriteInt32(count, 0);
        Check(Native.ResetEvent(ev), "ResetEvent");
        bool submitted = write ? Native.WinUsb_WritePipe(usb, pipe, payload, length, count, overlapped)
                               : Native.WinUsb_ReadPipe(usb, pipe, payload, length, count, overlapped);
        int submitError = submitted ? 0 : Marshal.GetLastWin32Error();
        uint waitResult = uint.MaxValue;
        bool complete = submitted;
        uint transferred = unchecked((uint)Marshal.ReadInt32(count));
        int completionError = submitError;
        if (!submitted && submitError == 997)
        {
            waitResult = Native.WaitForSingleObject(ev, 5000);
            complete = Native.WinUsb_GetOverlappedResult(usb, overlapped, out transferred, false);
            completionError = complete ? 0 : Marshal.GetLastWin32Error();
            if (!complete && completionError == 996)
            {
                Native.WinUsb_AbortPipe(usb, pipe);
                Native.WaitForSingleObject(ev, 3000);
                complete = Native.WinUsb_GetOverlappedResult(usb, overlapped, out transferred, false);
                completionError = complete ? 0 : Marshal.GetLastWin32Error();
                retained = !complete && completionError == 996;
            }
        }
        Report(new { Stage = write ? "WriteGINF" : "ReadGINF", Submitted = submitted, SubmitError = submitError,
            WaitResult = waitResult, Completed = complete, CompletionError = completionError, Transferred = transferred,
            Internal = $"0x{Marshal.ReadIntPtr(overlapped).ToInt64():X}", InternalHigh = Marshal.ReadIntPtr(overlapped, 8).ToInt64(),
            ResourcesRetainedUntilExit = retained });
        if (complete && !write)
        {
            byte[] response = new byte[transferred];
            Marshal.Copy(payload, response, 0, response.Length);
            Report(new { Stage = "Response", Bytes = Convert.ToHexString(response) });
        }
        return complete && transferred == length;
    }
    if (!Transfer(true, outPipe, 32)) { Environment.ExitCode = 1; return; }
    if (!Transfer(false, inPipe, 65)) Environment.ExitCode = 1;
}
catch (Exception exception)
{
    Report(new { Stage = "Exception", Error = exception.ToString() });
    Environment.ExitCode = 1;
}
finally
{
    if (!retained)
    {
        if (usb != IntPtr.Zero) Native.WinUsb_Free(usb);
        if (file != new IntPtr(-1)) Native.CloseHandle(file);
        if (ev != IntPtr.Zero) Native.CloseHandle(ev);
        if (payload != IntPtr.Zero) Marshal.FreeHGlobal(payload);
        if (overlapped != IntPtr.Zero) Marshal.FreeHGlobal(overlapped);
        if (count != IntPtr.Zero) Marshal.FreeHGlobal(count);
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential, Pack=1)] public struct Interface
    { public byte Length, DescriptorType, InterfaceNumber, AlternateSetting, NumEndpoints, InterfaceClass, InterfaceSubClass, InterfaceProtocol, StringIndex; }
    [StructLayout(LayoutKind.Sequential)] public struct Pipe
    { public uint PipeType; public byte PipeId; public ushort MaximumPacketSize; public byte Interval; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)] public static extern IntPtr CreateFileW(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)] public static extern IntPtr CreateEventW(IntPtr security,bool manualReset,bool initialState,string? name);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ResetEvent(IntPtr ev);
    [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_Initialize(IntPtr file,out IntPtr usb);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_Free(IntPtr usb);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_QueryInterfaceSettings(IntPtr usb,byte alt,out Interface descriptor);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_QueryPipe(IntPtr usb,byte alt,byte index,out Pipe pipe);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_SetPipePolicy(IntPtr usb,byte pipe,uint policy,uint valueSize,ref uint value);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_WritePipe(IntPtr usb,byte pipe,IntPtr data,uint length,IntPtr count,IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_ReadPipe(IntPtr usb,byte pipe,IntPtr data,uint length,IntPtr count,IntPtr overlapped);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_GetOverlappedResult(IntPtr usb,IntPtr overlapped,out uint count,bool wait);
    [DllImport("winusb.dll", SetLastError=true)] public static extern bool WinUsb_AbortPipe(IntPtr usb,byte pipe);
}
