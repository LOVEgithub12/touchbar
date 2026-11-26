using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

internal static class SilentLogin
{
    static int Main()
    {
        string directory = AppDomain.CurrentDomain.BaseDirectory;
        try {
            if (Process.GetCurrentProcess().SessionId == 0 || new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("The Touch Bar login launcher requires an ordinary interactive user.");
            string script = Path.Combine(directory, "AutoStart.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("AutoStart.ps1 missing.", script);
            string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(shell, "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + script + "\"") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true
            };
            var errors = new StringBuilder();
            using (var process = new Process { StartInfo = start }) {
                process.OutputDataReceived += (s,e) => { };
                process.ErrorDataReceived += (s,e) => { if (e.Data != null) lock (errors) { if (errors.Length < 16384) errors.AppendLine(e.Data); } };
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                if (process.ExitCode != 0) WriteFailure(directory, "Startup script exited " + process.ExitCode + ": " + errors);
                return process.ExitCode;
            }
        } catch (Exception e) { WriteFailure(directory, e.ToString()); return 1; }
    }
    static void WriteFailure(string directory, string message)
    {
        try { File.WriteAllText(Path.Combine(directory, "silent-login.error.log"), DateTimeOffset.Now.ToString("o") + " " + message, new UTF8Encoding(false)); }
        catch { }
    }
}
