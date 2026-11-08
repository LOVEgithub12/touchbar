$ErrorActionPreference = 'Stop'
$transportPath = Join-Path $PSScriptRoot '../WindowsTouchBar-master/WindowsTouchBar/Device/DfrTransport.cs'
$source = Get-Content -LiteralPath $transportPath -Raw
$bodies = @{
    WinUsb_Initialize = 'interfaceHandle = new IntPtr(2); return true;'
    WinUsb_Free = 'FakeUsb.FreeCalls++; return true;'
    WinUsb_QueryInterfaceSettings = 'descriptor = default; return true;'
    WinUsb_QueryPipe = 'pipeInformation = default; return true;'
    WinUsb_SetPipePolicy = 'return true;'
    WinUsb_ResetPipe = 'return true;'
    WinUsb_WritePipe = 'return FakeUsb.Start(bufferLength, out lengthTransferred);'
    WinUsb_ReadPipe = 'return FakeUsb.Start(bufferLength, out lengthTransferred);'
    WinUsb_AbortPipe = 'FakeUsb.Aborted = true; return true;'
    CreateEventW = 'return new IntPtr(3);'
    ResetEvent = 'return true;'
    WaitForSingleObject = 'return FakeUsb.Mode == 2 ? 258u : 0u;'
    WinUsb_GetOverlappedResult = 'return FakeUsb.Complete(out lengthTransferred);'
}
# Replace only the native boundary. Exercise the actual transport control flow,
# with no device handles or driver operations.
$pattern = '\[DllImport\([^\r\n]+\)\]\s+private static extern ([^;]+);'
$source = [regex]::Replace($source, $pattern, {
    param($match)
    $signature = $match.Groups[1].Value
    $name = [regex]::Match($signature, '^\S+\s+(\w+)\(').Groups[1].Value
    if (!$bodies.ContainsKey($name)) { throw "Missing fake native operation: $name" }
    'private static ' + $signature + ' { ' + $bodies[$name] + ' }'
})
$source = "using System;`n" + $source.Replace('namespace WindowsTouchBar.Device;', 'namespace WindowsTouchBar.Device {') + "`n}"
$source += @'

namespace WindowsTouchBar.Interop {
    static class NativeMethods {
        public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
        public static IntPtr CreateFile(string path, uint desiredAccess, FileShare share, IntPtr security,
            FileMode mode, FileOptions options, IntPtr template) => new IntPtr(1);
        public static void CloseHandle(IntPtr handle) { }
    }
}
namespace WindowsTouchBar.Device {
    public static class FakeUsb {
        public static int Mode, FreeCalls;
        public static bool Aborted;
        static uint requested;
        public static bool Start(uint length, out uint transferred) {
            requested = length;
            if (Mode == 4) { transferred = length / 2; return true; }
            transferred = 0;
            Marshal.SetLastPInvokeError(997);
            return false;
        }
        public static bool Complete(out uint transferred) {
            transferred = 0;
            if (Mode == 1 || (Mode == 2 && !Aborted)) {
                Marshal.SetLastPInvokeError(996); return false;
            }
            if (Mode == 2) { Marshal.SetLastPInvokeError(995); return false; }
            transferred = requested;
            return true;
        }
    }
    public static class TransportRegression {
        static readonly System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        static DfrTransport NewTransport(int mode) {
            FakeUsb.Mode = mode; FakeUsb.Aborted = false; FakeUsb.FreeCalls = 0;
            return (DfrTransport)Activator.CreateInstance(typeof(DfrTransport), Flags, null,
                new object[] { new IntPtr(1), new IntPtr(2), new IntPtr(3), (byte)0x81, (byte)1 }, null)!;
        }
        static object? Transfer(DfrTransport transport, object[] arguments) =>
            typeof(DfrTransport).GetMethod("Transfer", Flags)!.Invoke(transport, arguments);
        static object[] Arguments() => new object[] { true, (byte)1, new IntPtr(1), 32u, 1u, 0u, 0 };
        static IntPtr Pending(DfrTransport transport) =>
            (IntPtr)typeof(DfrTransport).GetField("_pendingOverlapped", Flags)!.GetValue(transport)!;
        static void Require(bool condition, string message) {
            if (!condition) throw new Exception(message);
        }
        public static string Run() {
            using (var transport = NewTransport(0)) {
                var args = Arguments();
                Require((bool)Transfer(transport, args)!, "ERROR_IO_PENDING 997 must complete successfully");
                Require((uint)args[5] == 32 && (int)args[6] == 0, "Completion byte count/error are wrong");
                Require(Pending(transport) == IntPtr.Zero, "Completed request retains OVERLAPPED");
            }
            using (var transport = NewTransport(2)) {
                var args = Arguments();
                Require(!(bool)Transfer(transport, args)!, "Timed out operation unexpectedly succeeded");
                Require(FakeUsb.Aborted && (int)args[6] == 121, "Timeout must abort then report ERROR_SEM_TIMEOUT 121");
                Require(Pending(transport) == IntPtr.Zero, "Completed cancellation retains OVERLAPPED");
            }
            var stalled = NewTransport(1);
            try { Transfer(stalled, Arguments()); throw new Exception("Stalled cancellation should throw"); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is IOException) { }
            Require(Pending(stalled) != IntPtr.Zero, "Stalled I/O lost its OVERLAPPED");
            stalled.Dispose();
            Require(FakeUsb.FreeCalls == 0, "Disposed a native interface still using pinned buffers");
            FakeUsb.Mode = 0;
            stalled.Dispose();
            Require(FakeUsb.FreeCalls == 1 && Pending(stalled) == IntPtr.Zero, "Late completion cannot be cleaned up");
            using (var shortWrite = NewTransport(4)) {
                try {
                    typeof(DfrTransport).GetMethod("SendGenericRequest", Flags)!.Invoke(shortWrite, new object[] { 0x47494e46u });
                    throw new Exception("Short GINF write should fail");
                }
                catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is IOException) { }
            }
            return "PASS: pending 997, timeout cancellation, pending-buffer lifetime, and short writes.";
        }
    }
}
'@
Add-Type -TypeDefinition $source -CompilerOptions '/unsafe','/nullable:enable' -ErrorAction Stop
[WindowsTouchBar.Device.TransportRegression]::Run()
