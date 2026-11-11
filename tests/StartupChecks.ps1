$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
. (Join-Path $workspace 'TouchBar-USBIP\Read-UsbIpState.ps1')
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('touchbar-startup-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$executable = Join-Path $temporary 'UsbIpStateFixture.exe'
$pidFile = Join-Path $temporary 'fixture.pid'
$originalMode = $env:TOUCHBAR_STARTUP_FIXTURE_MODE
$originalPidFile = $env:TOUCHBAR_STARTUP_FIXTURE_PID_FILE
$originalEncoding = [Console]::OutputEncoding
try {
    Add-Type -OutputAssembly $executable -OutputType ConsoleApplication -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
public static class UsbIpStateFixture {
    static void Write(Stream stream, string value) {
        byte[] bytes = new UTF8Encoding(false).GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }
    public static int Main() {
        string mode = Environment.GetEnvironmentVariable("TOUCHBAR_STARTUP_FIXTURE_MODE");
        File.WriteAllText(Environment.GetEnvironmentVariable("TOUCHBAR_STARTUP_FIXTURE_PID_FILE"), Process.GetCurrentProcess().Id.ToString());
        if (mode == "timeout") Thread.Sleep(10000);
        if (mode == "invalid") { Write(Console.OpenStandardOutput(), "{broken"); return 0; }
        if (mode == "missing") { Write(Console.OpenStandardOutput(), "{}"); return 0; }
        if (mode == "stderr" || mode == "failure")
            Write(Console.OpenStandardError(), new string('e', 131072));
        Write(Console.OpenStandardOutput(), "{\"Devices\":[{\"InstanceId\":\"USB\\\\VID_05AC&PID_8302\\\\0000000000000000\",\"BusId\":\"1-6\",\"IsForced\":true},{\"Description\":\"iQOO Neo5 \u6d3b\u529b\u7248\"}]}");
        return mode == "failure" ? 7 : 0;
    }
}
'@
    $env:TOUCHBAR_STARTUP_FIXTURE_PID_FILE = $pidFile
    # Reproduce the legacy Windows PowerShell host used by the HKCU Run entry.
    [Console]::OutputEncoding = [Text.Encoding]::GetEncoding(936)
    foreach ($mode in @('success', 'stderr', 'failure', 'invalid', 'missing', 'timeout')) {
        $env:TOUCHBAR_STARTUP_FIXTURE_MODE = $mode
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $failure = $null
        $state = $null
        try { $state = Read-UsbIpState -Executable $executable -TimeoutMilliseconds 1000 }
        catch { $failure = $_.Exception.Message }
        if ($mode -eq 'success' -or $mode -eq 'stderr') {
            if ($failure) { throw ($mode + ': ' + $failure) }
            $expected = 'iQOO Neo5 ' + [char]0x6d3b + [char]0x529b + [char]0x7248
            if ($state.Devices[1].Description -cne $expected) { throw 'Chinese UTF-8 description was corrupted.' }
            $matches = @($state.Devices | Where-Object { $_.InstanceId -eq 'USB\VID_05AC&PID_8302\0000000000000000' -and $_.IsForced })
            if ($matches.Count -ne 1) { throw 'Exact Touch Bar identity was not preserved.' }
        } else {
            $expectedFailure = switch ($mode) {
                'failure' { 'failed \(exit 7\)' }
                'invalid' { 'invalid JSON' }
                'missing' { 'Devices list' }
                'timeout' { 'timed out' }
            }
            if (-not $failure -or $failure -notmatch $expectedFailure) { throw ($mode + ' did not fail for the expected reason: ' + $failure) }
        }
        if ($mode -eq 'timeout') {
            if ($timer.Elapsed.TotalSeconds -gt 4) { throw 'Timed-out enumeration was not bounded.' }
            $fixtureProcessId = [int](Get-Content -LiteralPath $pidFile -Raw)
            if (Get-Process -Id $fixtureProcessId -ErrorAction SilentlyContinue) { throw 'Timed-out fixture was left running.' }
        }
        Write-Output ('PASS: ' + $mode)
    }
    Write-Output '6/6 startup encoding/process checks passed; no USB devices were opened.'
} finally {
    [Console]::OutputEncoding = $originalEncoding
    $env:TOUCHBAR_STARTUP_FIXTURE_MODE = $originalMode
    $env:TOUCHBAR_STARTUP_FIXTURE_PID_FILE = $originalPidFile
    $resolved = [IO.Path]::GetFullPath($temporary)
    $expectedParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ((Split-Path -Parent $resolved).TrimEnd('\') -eq $expectedParent.TrimEnd('\') -and
        (Split-Path -Leaf $resolved) -like 'touchbar-startup-checks-*') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
