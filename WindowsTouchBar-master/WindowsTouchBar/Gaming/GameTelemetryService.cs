using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;

namespace WindowsTouchBar.Gaming;

internal sealed class GameTelemetryService : IDisposable
{
    private sealed class Capture
    {
        public int Pid;
        public PresentMonFrames Frames = new();
        public TelemetrySnapshot Hardware = TelemetrySnapshot.Empty;
        public double HardwareAt, ElevatedAt;
    }
    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Action<string> log;
    private readonly Action<string>? frameDiagnostic;
    private readonly Task worker, pipeWorker;
    private readonly bool useTrafficMonitor = TrafficMonitorTelemetryReader.IsConfigured();
    private readonly TrafficMonitorTelemetryReader hardwareBridge = new();
    private int desiredPid;
    private Capture state = new();
    private Process? sensors, presents;
    private int sensorGeneration, frameGeneration;
    private readonly string session = "TouchBarGaming-" + Environment.ProcessId;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public GameTelemetryService(Action<string> log, Action<string>? frameDiagnostic = null)
    { this.log = log; log("Hardware telemetry source: " + (useTrafficMonitor ? "TrafficMonitor unified CPU/GPU bridge; no sensor helper" : "independent collector")); this.frameDiagnostic = frameDiagnostic; worker = Task.Run(Run); pipeWorker = Task.Run(ReadElevated); }
    public void SetProcess(int pid) => Volatile.Write(ref desiredPid, pid);
    public TelemetrySnapshot Snapshot
    {
        get {
            lock (gate) {
                if (state.Pid == 0 || state.Pid != Volatile.Read(ref desiredPid)) return TelemetrySnapshot.Empty;
                var value = useTrafficMonitor ? hardwareBridge.Read() :
                    Now - state.HardwareAt <= 3 ? state.Hardware : TelemetrySnapshot.Empty;
                var frame = state.Frames.Read(Now);
                return value with { Fps = frame.Fps, FrameTimeMs = frame.Ms };
            }
        }
    }
    private void ReadHardware(string line, Capture captured, int generation, bool elevated)
    {
        try {
            var data = JsonSerializer.Deserialize<TelemetrySnapshot>(line);
            if (data is null) return;
            lock (gate) {
                if (!ReferenceEquals(state, captured) || captured.Pid != Volatile.Read(ref desiredPid)) return;
                if (!elevated && (generation != sensorGeneration || Now - captured.ElevatedAt < 3)) return;
                captured.Hardware = data; captured.HardwareAt = Now;
                if (elevated) captured.ElevatedAt = Now;
            }
        } catch (JsonException) { }
    }
    private async Task ReadElevated()
    {
        if (useTrafficMonitor) return;
        try {
            while (!stop.IsCancellationRequested) {
                if (Volatile.Read(ref desiredPid) == 0) { await Task.Delay(1000, stop.Token); continue; }
                using var pipe = new NamedPipeClientStream(".", "TouchBarSensors-" + Environment.ProcessId, PipeDirection.In, PipeOptions.Asynchronous);
                try {
                    await pipe.ConnectAsync(500, stop.Token);
                    using var cancel = stop.Token.Register(() => pipe.Dispose());
                    using var reader = new StreamReader(pipe);
                    log("Game sensors: connected to independent elevated collector.");
                    while (!stop.IsCancellationRequested && Volatile.Read(ref desiredPid) > 0) {
                        string? line = await reader.ReadLineAsync();
                        if (line is null) break;
                        Capture captured; lock (gate) captured = state;
                        if (line.Length < 65536) ReadHardware(line, captured, 0, true);
                    }
                } catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or ObjectDisposedException) { }
                await Task.Delay(1000, stop.Token);
            }
        } catch (OperationCanceledException) { }
    }
    private async Task Run()
    {
        int activePid = 0; double retryAfter = 0;
        try {
            while (!stop.IsCancellationRequested) {
                int next = Volatile.Read(ref desiredPid);
                if (next != activePid) {
                    lock (gate) { ++sensorGeneration; ++frameGeneration; state = new Capture { Pid = next }; }
                    StopPresentMon(); StopSensor(); activePid = next; retryAfter = 0;
                    log("Game telemetry target PID: " + activePid);
                }
                if (activePid > 0 && Now >= retryAfter) {
                    try {
                        Capture captured; lock (gate) captured = state;
                        if (!useTrafficMonitor && (sensors is null || sensors.HasExited)) {
                            sensors?.Dispose(); int generation = ++sensorGeneration;
                            var sensorArgs = new List<string> { "--parent", Environment.ProcessId.ToString(CultureInfo.InvariantCulture) };
                            sensors = Start("TouchBarSensors.exe", sensorArgs,
                                line => ReadHardware(line, captured, generation, false));
                        }
                        if (presents is null || presents.HasExited) {
                            presents?.Dispose(); int generation = ++frameGeneration;
                            lock (gate) captured.Frames = new();
                            presents = Start("PresentMon.exe", new[] { "--process_id", activePid.ToString(CultureInfo.InvariantCulture), "--session_name", session, "--stop_existing_session",
                                "--output_stdout", "--v1_metrics", "--qpc_time", "--no_console_stats", "--no_track_input",
                                "--no_track_gpu", "--no_track_display", "--terminate_on_proc_exit" },
                                line => { frameDiagnostic?.Invoke(line); lock (gate) { if (ReferenceEquals(state, captured) && generation == frameGeneration && captured.Pid == Volatile.Read(ref desiredPid))
                                    captured.Frames.Add(line, captured.Pid, Now); } });
                        }
                        retryAfter = Now + 3;
                    } catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
                    { log("Game telemetry: " + e.Message); retryAfter = Now + 30; }
                }
                await Task.Delay(250, stop.Token);
            }
        } catch (OperationCanceledException) { }
        catch (Exception e) { log("Game telemetry stopped: " + e.Message); }
        finally { StopPresentMon(); StopSensor(); }
    }
    private Process Start(string file, IEnumerable<string> args, Action<string> read)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Telemetry", file))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "Telemetry") };
        foreach (string argument in args) start.ArgumentList.Add(argument);
        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 and < 65536 } line) read(line); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) log("Game " + file + ": " + e.Data); };
        try { process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process; }
        catch { process.Dispose(); throw; }
    }
    private void StopPresentMon()
    {
        if (presents is null) return;
        try {
            if (!presents.HasExited) {
                var start = new ProcessStartInfo(presents.StartInfo.FileName) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--session_name"); start.ArgumentList.Add(session); start.ArgumentList.Add("--terminate_existing_session");
                using var terminator = Process.Start(start);
                if (terminator is not null && !terminator.WaitForExit(1500)) terminator.Kill();
                if (!presents.WaitForExit(2000)) { presents.Kill(); presents.WaitForExit(1000); }
            }
        } catch (Exception e) { log("Game capture stop: " + e.Message); }
        finally { presents.Dispose(); presents = null; }
    }
    private void StopSensor()
    {
        if (sensors is null) return;
        try { if (!sensors.HasExited) { sensors.StandardInput.Close(); sensors.WaitForExit(2500); } } catch (IOException) { }
        try { if (!sensors.HasExited) { sensors.Kill(); sensors.WaitForExit(1000); } }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception e) { log("Game sensor stop: " + e.Message); }
        finally { sensors.Dispose(); sensors = null; }
    }
    public void Dispose()
    {
        stop.Cancel();
        try { Task.WaitAll(new[] { worker, pipeWorker }, TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
        if (worker.IsCompleted && pipeWorker.IsCompleted) stop.Dispose();
        lock (gate) hardwareBridge.Dispose();
    }
}
