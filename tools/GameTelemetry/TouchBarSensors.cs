using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using LibreHardwareMonitor.Hardware;

// Runs separately from the ordinary desktop UI; no commands are accepted over IPC.
class TouchBarSensors
{
    static readonly ManualResetEvent Closing = new ManualResetEvent(false);
    static string latest;
    static long latestAt;
    static NamedPipeServerStream server;
    static bool Valid(float? v) { return v.HasValue && v >= 0 && !float.IsNaN(v.Value) && !float.IsInfinity(v.Value); }
    static double? Find(IHardware h, SensorType type, params string[] names)
    {
        if (h == null) return null;
        foreach (var name in names) {
            var s = h.Sensors.FirstOrDefault(v => v.SensorType == type && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && Valid(v.Value));
            if (s != null) return s.Value.Value;
        }
        return null;
    }
    static double? CpuTemperature(IHardware cpu)
    {
        if (cpu == null) return null;
        var native = Find(cpu, SensorType.Temperature, "Core Average");
        if (native.HasValue) return native;
        var values = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && Regex.IsMatch(s.Name, @"^CPU Core #\d+$") && Valid(s.Value)).Select(s => (double)s.Value.Value).ToArray();
        return values.Length > 0 ? (double?)values.Average() : null;
    }
    static double? CpuClock(IHardware cpu)
    {
        if (cpu == null) return null;
        var values = cpu.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Name.StartsWith("CPU Core") && Valid(s.Value) && s.Value > 0).Select(s => (double)s.Value.Value).ToArray();
        return values.Length > 0 ? (double?)values.Average() : null;
    }
    static Process LifetimeProcess(int pid)
    {
        if (pid <= 0) return null;
        var process = Process.GetProcessById(pid);
        try { var handle = process.Handle; return process; }
        catch { process.Dispose(); throw; }
    }
    static void Serve(int parent)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
        while (!Closing.WaitOne(0)) {
            try {
                using (var pipe = new NamedPipeServerStream("TouchBarSensors-" + parent, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security)) {
                    server = pipe; pipe.WaitForConnection();
                    using (var writer = new StreamWriter(pipe) { AutoFlush = true }) {
                        while (!Closing.WaitOne(0) && pipe.IsConnected) {
                            var value = Volatile.Read(ref latest);
                            if (value != null && (Stopwatch.GetTimestamp() - Interlocked.Read(ref latestAt)) / (double)Stopwatch.Frequency < 3) writer.WriteLine(value);
                            Closing.WaitOne(1000);
                        }
                    }
                }
            } catch (IOException) { } catch (ObjectDisposedException) { }
        }
    }
    static void Main(string[] args)
    {
        int parent = 0, owner = 0, seconds = 0; string output = null;
        for (int i = 0; i + 1 < args.Length; i++) {
            if (args[i] == "--parent") int.TryParse(args[i + 1], out parent);
            if (args[i] == "--owner") int.TryParse(args[i + 1], out owner);
            if (args[i] == "--seconds") int.TryParse(args[i + 1], out seconds);
            if (args[i] == "--output") output = args[i + 1];
        }
        bool once = Array.IndexOf(args, "--once") >= 0, serve = Array.IndexOf(args, "--serve") >= 0;
        if (serve && parent <= 0) { Environment.ExitCode = 2; return; }
        if (parent > 0 && !serve) {
            var input = new Thread(() => { try { Console.In.ReadToEnd(); } finally { Closing.Set(); } }); input.IsBackground = true; input.Start();
        }
        var computer = new Computer { IsCpuEnabled = Array.IndexOf(args, "--gpu-only") < 0, IsGpuEnabled = true };
        var json = new JavaScriptSerializer(); var watch = Stopwatch.StartNew();
        StreamWriter file = output == null ? null : new StreamWriter(output) { AutoFlush = true };
        Process parentProcess = null, ownerProcess = null;
        try {
            parentProcess = LifetimeProcess(parent); ownerProcess = LifetimeProcess(owner);
            if ((parentProcess != null && parentProcess.HasExited) || (ownerProcess != null && ownerProcess.HasExited)) return;
            computer.Open();
            if (serve) { var listener = new Thread(() => Serve(parent)); listener.IsBackground = true; listener.Start(); }
            while (!Closing.WaitOne(0) && (parentProcess == null || !parentProcess.HasExited) && (ownerProcess == null || !ownerProcess.HasExited) && (seconds == 0 || watch.Elapsed.TotalSeconds < seconds)) {
                foreach (var h in computer.Hardware) { try { h.Update(); } catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); } }
                var cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                var gpu = computer.Hardware.Where(h => h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuIntel)
                    .OrderByDescending(h => h.HardwareType != HardwareType.GpuIntel).ThenByDescending(h => Find(h, SensorType.Load, "GPU Core", "D3D 3D") ?? 0).FirstOrDefault();
                var data = new {
                    CpuTemperature = CpuTemperature(cpu), CpuLoad = Find(cpu, SensorType.Load, "CPU Total"), CpuClockMHz = CpuClock(cpu),
                    GpuTemperature = Find(gpu, SensorType.Temperature, "GPU Core"), GpuLoad = Find(gpu, SensorType.Load, "GPU Core", "D3D 3D"), GpuClockMHz = Find(gpu, SensorType.Clock, "GPU Core")
                };
                var line = json.Serialize(data); Volatile.Write(ref latest, line); Interlocked.Exchange(ref latestAt, Stopwatch.GetTimestamp());
                if (file != null) file.WriteLine(line);
                if (!serve) { Console.WriteLine(line); Console.Out.Flush(); }
                if (once) break; Closing.WaitOne(1000);
            }
        } catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); Environment.ExitCode = 1; }
        finally { Closing.Set(); if (server != null) server.Dispose(); computer.Close(); if (file != null) file.Dispose(); if (parentProcess != null) parentProcess.Dispose(); if (ownerProcess != null) ownerProcess.Dispose(); }
    }
}
