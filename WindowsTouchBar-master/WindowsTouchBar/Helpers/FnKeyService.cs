using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsTouchBar.Helpers;

/// <summary>
/// Infers standalone Fn transitions from Apple KeyManager keyboard activity.
/// The installed Apple driver does not expose its actual Fn state to user mode.
/// Nearby ordinary key transitions are filtered, but simultaneous Fn/key transitions
/// can be ambiguous. A 60-second keyboard-activity idle guard releases stale state,
/// and the launchers can reselect a default layout to reset it immediately.
/// This never changes a driver setting or sends a keyboard/device report.
/// </summary>
internal sealed class FnKeyService : IDisposable
{
    private const uint KeyboardActivityRegistration = 0xb40320e3;
    private readonly SafeFileHandle device;
    private readonly AutoResetEvent signal = new(false);
    private readonly CancellationTokenSource cancellation = new();
    private readonly KeyboardActivityMonitor keyboardActivity;
    private readonly FnInferenceState inference = new();
    private readonly Action<string> log;
    private readonly Task listener;
    private bool disposed;

    public bool Pressed => inference.Pressed;
    public event Action<bool>? Changed;

    public FnKeyService(Action<string>? log = null)
    {
        this.log = log ?? (_ => { });
        device = CreateFile(@"\\.\KeyManager", 0xc0000000, 3, 0, 3, 0, 0);
        try
        {
            if (device.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Apple KeyManager could not be opened.");
            if (!DeviceIoControl(device, KeyboardActivityRegistration,
                    signal.SafeWaitHandle.DangerousGetHandle(), 0, 0, 0, out _, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Apple keyboard activity could not be observed.");
            keyboardActivity = new KeyboardActivityMonitor();
            listener = Task.Run(Listen);
            WriteLog("Fn listener ready (standalone-key inference; simultaneous ordinary keys can be ambiguous).");
        }
        catch
        {
            device.Dispose();
            signal.Dispose();
            cancellation.Dispose();
            throw;
        }
    }

    // Called when the launcher reselects a default, to recover a missed transition.
    public void Reset() => ApplyTransition(inference.Reset());

    private void Listen()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (!signal.WaitOne(100))
                {
                    ApplyTransition(inference.RecoverIfInactive(Stopwatch.GetTimestamp()));
                    continue;
                }
                if (cancellation.IsCancellationRequested) break;
                long timestamp = Stopwatch.GetTimestamp();

                // This keyboard emits two activity pulses about 0.1–0.8 ms apart.
                // Drain that single report's duplicates without a long debounce,
                // so ordinary press/release pairs remain separate transitions.
                if (cancellation.Token.WaitHandle.WaitOne(5)) break;
                while (signal.WaitOne(0)) { }
                if (cancellation.Token.WaitHandle.WaitOne(20)) break;

                bool ordinaryKeyActivityNear = keyboardActivity.WasActiveNear(
                    timestamp, TimeSpan.FromMilliseconds(70));
                ApplyTransition(inference.ObserveActivity(timestamp, ordinaryKeyActivityNear));
            }
        }
        catch (Exception error)
        {
            WriteLog("Fn listener stopped: " + error.Message);
            ApplyTransition(inference.Reset());
        }
    }

    private void ApplyTransition(FnInferenceTransition transition)
    {
        if (transition == FnInferenceTransition.None) return;
        if (transition == FnInferenceTransition.Pressed)
            WriteLog("Fn inferred down.");
        else if (transition == FnInferenceTransition.InactivityReleased)
            WriteLog($"Fn inference safety reset after {FnInferenceState.InactivityReleaseSeconds} seconds without keyboard activity.");
        else
            WriteLog("Fn inferred up.");
        Changed?.Invoke(transition == FnInferenceTransition.Pressed);
    }

    private void WriteLog(string message)
    {
        // Diagnostics must not leak a live registered listener if logging fails.
        try { log(message); } catch { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();
        signal.Set();
        listener.GetAwaiter().GetResult();
        // KeyManager removes registrations associated with this file on close.
        device.Dispose();
        keyboardActivity.Dispose();
        signal.Dispose();
        cancellation.Dispose();
    }

    private sealed class KeyboardActivityMonitor : IDisposable
    {
        private readonly bool[] state = new bool[256];
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task poller;
        private long lastActivity = long.MinValue;

        public KeyboardActivityMonitor()
        {
            for (int key = 8; key < state.Length; key++) state[key] = IsDown(key);
            poller = Task.Run(Poll);
        }

        public bool WasActiveNear(long timestamp, TimeSpan tolerance)
        {
            long activity = Interlocked.Read(ref lastActivity);
            return activity != long.MinValue &&
                Math.Abs(activity - timestamp) <= tolerance.TotalSeconds * Stopwatch.Frequency;
        }

        private void Poll()
        {
            while (!cancellation.IsCancellationRequested)
            {
                for (int key = 8; key < state.Length; key++)
                {
                    bool down = IsDown(key);
                    if (down == state[key]) continue;
                    state[key] = down;
                    Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());
                }
                if (cancellation.Token.WaitHandle.WaitOne(4)) break;
            }
        }

        private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        public void Dispose()
        {
            cancellation.Cancel();
            poller.GetAwaiter().GetResult();
            cancellation.Dispose();
        }
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security,
        uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, nint input, uint inputSize,
        nint output, uint outputSize, out uint returned, nint overlapped);
}
