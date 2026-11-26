using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

// Installed as a windowless, elevated, interactive-user logon task. It only
// launches the fixed collector beside itself; it never executes the UI path.
internal static class SensorAutoStartHost
{
    internal sealed class Configuration { public string ApplicationPath { get; set; } public string UserSid { get; set; } }
    internal sealed class Parent : IDisposable
    {
        public Process Process;
        public Process Collector;
        public DateTime RetryAt;
        public void Dispose() { if (Collector != null) Collector.Dispose(); Process.Dispose(); }
    }
    static readonly string DirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string LogPath = Path.Combine(DirectoryPath, "host.log");
    static void Log(string message)
    {
        try {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024) {
                string previous = LogPath + ".old";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(LogPath, previous);
            }
            File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine, new UTF8Encoding(false));
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    internal static bool MatchesCollector(string path, string command, string installed, string legacy, int parent)
    {
        return (String.Equals(path, installed, StringComparison.OrdinalIgnoreCase) || String.Equals(path, legacy, StringComparison.OrdinalIgnoreCase)) &&
            command != null && Regex.IsMatch(command, @"(?:^|\s)--serve(?:\s|$)") &&
            Regex.IsMatch(command, @"(?:^|\s)--parent\s+" + parent + @"(?:\s|$)");
    }
    static Process ExistingCollector(int parent, int session, string installed, string legacy)
    {
        using (var query = new ManagementObjectSearcher("SELECT ProcessId,ExecutablePath,CommandLine,SessionId FROM Win32_Process WHERE Name='TouchBarSensors.exe'"))
        using (var rows = query.Get()) {
            foreach (ManagementObject row in rows) using (row) {
                if (Convert.ToInt32(row["SessionId"]) != session || !MatchesCollector((string)row["ExecutablePath"], (string)row["CommandLine"], installed, legacy, parent)) continue;
                try {
                    var process = Process.GetProcessById(Convert.ToInt32(row["ProcessId"]));
                    try { var handle = process.Handle; if (!process.HasExited) return process; } catch { process.Dispose(); throw; }
                    process.Dispose();
                } catch (ArgumentException) { } catch (InvalidOperationException) { }
            }
        }
        return null;
    }
    static void TrackParents(Dictionary<int, Parent> parents, string applicationPath, int session)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(applicationPath))) {
            bool retained = false;
            try {
                if (!parents.ContainsKey(process.Id) && process.SessionId == session &&
                    String.Equals(process.MainModule.FileName, applicationPath, StringComparison.OrdinalIgnoreCase)) {
                    var handle = process.Handle;
                    if (!process.HasExited) {
                        parents.Add(process.Id, new Parent { Process = process }); retained = true;
                        Log("Application detected: " + process.Id + ".");
                    }
                }
            } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            finally { if (!retained) process.Dispose(); }
        }
    }
    static int Main()
    {
        var parents = new Dictionary<int, Parent>();
        try {
            var config = new JavaScriptSerializer().Deserialize<Configuration>(File.ReadAllText(Path.Combine(DirectoryPath, "host.json")));
            var identity = WindowsIdentity.GetCurrent();
            int session = Process.GetCurrentProcess().SessionId;
            if (config == null || identity.User.Value != config.UserSid || session == 0 ||
                !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Expected the configured elevated interactive user.");
            string app = Path.GetFullPath(config.ApplicationPath);
            if (!Path.IsPathRooted(config.ApplicationPath) || !String.Equals(Path.GetFileName(app), "WindowsTouchBar.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid application path.");
            string collector = Path.Combine(DirectoryPath, "TouchBarSensors.exe");
            string legacy = Path.Combine(Path.GetDirectoryName(app), "Telemetry", "TouchBarSensors.exe");
            if (!File.Exists(collector)) throw new FileNotFoundException("Installed collector missing.");
            bool created;
            using (var mutex = new Mutex(true, "Local\\TouchBarSensorHost." + config.UserSid, out created)) {
                if (!created) return 0;
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\TouchBarSensorHost.Stop." + config.UserSid)) {
                    stop.Reset();
                    Log("Host started: PID=" + Process.GetCurrentProcess().Id + "; session=" + session + "; application=" + app + ".");
                    while (!stop.WaitOne(0)) {
                        foreach (int pid in parents.Keys.ToArray()) {
                            Parent parent = parents[pid];
                            if (parent.Process.HasExited) {
                                Log("Application exited: " + pid + "; sampling ends with its collector.");
                                parent.Dispose(); parents.Remove(pid);
                            }
                        }
                        TrackParents(parents, app, session);
                        foreach (var item in parents) {
                            Parent parent = item.Value;
                            if (parent.Collector != null && !parent.Collector.HasExited) continue;
                            if (parent.Collector != null) {
                                Log("Collector exited for application " + item.Key + "; retry in 5 seconds.");
                                parent.Collector.Dispose(); parent.Collector = null; parent.RetryAt = DateTime.UtcNow.AddSeconds(5);
                            }
                            if (DateTime.UtcNow < parent.RetryAt || parent.Process.HasExited) continue;
                            try {
                                parent.Collector = ExistingCollector(item.Key, session, collector, legacy);
                                if (parent.Collector != null) { Log("Adopted collector " + parent.Collector.Id + " for application " + item.Key + "."); continue; }
                                var start = new ProcessStartInfo(collector, "--serve --parent " + item.Key + " --owner " + Process.GetCurrentProcess().Id) {
                                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = DirectoryPath,
                                    RedirectStandardOutput = true, RedirectStandardError = true
                                };
                                var child = new Process { StartInfo = start };
                                child.OutputDataReceived += (s, e) => { };
                                child.ErrorDataReceived += (s, e) => { if (!String.IsNullOrWhiteSpace(e.Data)) Log("Collector: " + e.Data); };
                                try { child.Start(); child.BeginOutputReadLine(); child.BeginErrorReadLine(); parent.Collector = child; }
                                catch { child.Dispose(); throw; }
                                Log("Started collector " + child.Id + " for application " + item.Key + ".");
                            } catch (Exception e) { parent.RetryAt = DateTime.UtcNow.AddSeconds(10); Log("Collector start failed: " + e.Message); }
                        }
                        stop.WaitOne(1000);
                    }
                    Log("Host stopping; owned collectors will close hardware access.");
                }
                mutex.ReleaseMutex();
            }
            return 0;
        } catch (Exception e) { Log("Host failed: " + e); return 1; }
        finally { foreach (var parent in parents.Values) parent.Dispose(); }
    }
}
