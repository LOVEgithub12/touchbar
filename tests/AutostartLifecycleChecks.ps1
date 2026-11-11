[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    & $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath
    exit $LASTEXITCODE
}

# These copies use unique kernel-object names and fake hardware/app processes.
# Nothing here opens the real USB device, stops the live app, or edits HKCU Run.
$workspace = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('TouchBarLifecycle-' + [Guid]::NewGuid().ToString('N'))
$children = New-Object 'System.Collections.Generic.List[System.Diagnostics.Process]'
$fixtures = New-Object 'System.Collections.Generic.List[string]'
$utf8 = New-Object Text.UTF8Encoding($true)
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
function Get-StartupPreference {
    $key = Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue
    if ($null -eq $key) { return $null }
    return $key.WindowsTouchBar
}
$originalPreference = Get-StartupPreference

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Wait-Until([scriptblock]$condition, [string]$message, [int]$timeoutMilliseconds = 12000) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMilliseconds)
    do {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $message
}
function Read-Text([string]$path) {
    if (Test-Path -LiteralPath $path) { return [IO.File]::ReadAllText($path) }
    return ''
}
function Start-IsolatedScript([string]$path) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $windowsPowerShell
    $start.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $path + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    [void]$process.Start()
    [void]$children.Add($process)
    return $process
}
function Wait-ScriptExit($process, [string]$message, [int]$expectedCode = 0) {
    Require ($process.WaitForExit(12000)) $message
    Require ($process.ExitCode -eq $expectedCode) ($message + ' Exit code: ' + $process.ExitCode)
}
function Get-FakeApp([string]$directory) {
    $pidText = Read-Text (Join-Path $directory 'app\pid.txt')
    if (-not $pidText) { return $null }
    $process = Get-Process -Id ([int]$pidText.Trim()) -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    # A stale PID must never authorize touching an unrelated process.
    if ($process.Path -ne (Join-Path $directory 'app\WindowsTouchBar.exe')) { return $null }
    return $process
}
function New-Fixture([string]$scenario) {
    $directory = Join-Path $fixtureRoot ([Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path (Join-Path $directory 'app') -Force)
    [void]$fixtures.Add($directory)
    $identity = [Guid]::NewGuid().ToString('N')
    foreach ($name in @('AutoStart.ps1', 'Stop.ps1', 'Launch.ps1')) {
        $source = [IO.File]::ReadAllText((Join-Path $workspace ('TouchBar-USBIP\' + $name)))
        $source = $source.Replace('Local\WindowsTouchBar.AutoStart.Cancel', ('Local\TouchBarLifecycle.Cancel.' + $identity))
        $source = $source.Replace('Local\WindowsTouchBar.AutoStart', ('Local\TouchBarLifecycle.Mutex.' + $identity))
        $source = $source.Replace('C:\Program Files\usbipd-win\usbipd.exe', (Join-Path $directory 'usbipd.exe'))
        # The production error path still runs and writes its log. Only the modal
        # message box is suppressed in the isolated launch-failure fixture.
        $source = [regex]::Replace($source, '(?m)^\s*\[void\]\[TouchBarLaunchMessage\]::MessageBox[^\r\n]*', '    # Test: suppress modal MessageBox.')
        [IO.File]::WriteAllText((Join-Path $directory $name), $source, $utf8)
    }
    [IO.File]::WriteAllText((Join-Path $directory 'scenario.txt'), $scenario)
    [IO.File]::WriteAllText((Join-Path $directory 'usbipd.exe'), '')
    [IO.File]::WriteAllText((Join-Path $directory 'app\stop-event.txt'), ('Local\TouchBarLifecycle.AppStop.' + $identity))
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'FakeTouchBar.exe') -Destination (Join-Path $directory 'app\WindowsTouchBar.exe')
    $stub = @'
$script:LifecycleFixtureRoot = $PSScriptRoot
function Get-Service {
    param([string]$Name)
    $scenario = [IO.File]::ReadAllText((Join-Path $script:LifecycleFixtureRoot 'scenario.txt'))
    [pscustomobject]@{ Status = $(if ($scenario -eq 'waiting') { 'Stopped' } else { 'Running' }) }
}
function Get-ItemProperty {
    param([string]$LiteralPath)
    if ($LiteralPath -notlike 'HKLM:\SYSTEM\CurrentControlSet\Enum\*') { throw 'Unexpected registry access in fixture.' }
    [pscustomobject]@{ Service = 'VBoxUSB' }
}
function Get-PnpDeviceProperty {
    param([string]$InstanceId, [string]$KeyName)
    [pscustomobject]@{ Data = 0 }
}
function Read-UsbIpState {
    param([string]$Executable)
    [pscustomobject]@{ Devices = @([pscustomobject]@{
        InstanceId = 'USB\VID_05AC&PID_8302\0000000000000000'
        BusId = '1-6'
        IsForced = $true
    }) }
}
'@
    [IO.File]::WriteAllText((Join-Path $directory 'Read-UsbIpState.ps1'), $stub, $utf8)
    return $directory
}

try {
    [void](New-Item -ItemType Directory -Path $fixtureRoot)
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading;
public static class FakeTouchBar {
    public static int Main(string[] args) {
        string app = AppDomain.CurrentDomain.BaseDirectory;
        string root = Directory.GetParent(app.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        string eventName = File.ReadAllText(Path.Combine(app, "stop-event.txt"));
        if (Array.IndexOf(args, "--stop") >= 0) {
            EventWaitHandle existing;
            if (EventWaitHandle.TryOpenExisting(eventName, out existing)) {
                using (existing) { existing.Set(); }
            }
            return 0;
        }
        File.AppendAllText(Path.Combine(app, "launches.txt"), System.Diagnostics.Process.GetCurrentProcess().Id + "\n");
        File.WriteAllText(Path.Combine(app, "pid.txt"), System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
        string scenario = File.ReadAllText(Path.Combine(root, "scenario.txt"));
        string status = Path.Combine(app, "WindowsTouchBar.status.log");
        File.WriteAllText(status, "Opening isolated fake display...\n");
        if (scenario == "initializing") { Thread.Sleep(60000); }
        if (scenario == "failure") {
            File.WriteAllText(status, "FAILED: isolated initialization failure\n");
            return 7;
        }
        using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, eventName)) {
            File.WriteAllText(status, "RUNNING: isolated fake display\n");
            stop.WaitOne(60000);
        }
        return 0;
    }
}
'@ -OutputAssembly (Join-Path $fixtureRoot 'FakeTouchBar.exe') -OutputType ConsoleApplication

    $waiting = New-Fixture 'waiting'
    $helper = Start-IsolatedScript (Join-Path $waiting 'AutoStart.ps1')
    Wait-Until { (Read-Text (Join-Path $waiting 'autostart.log')) -match 'Login startup requested' } 'Waiting helper did not start.'
    $stop = Start-IsolatedScript (Join-Path $waiting 'Stop.ps1')
    Wait-ScriptExit $stop 'Waiting cancellation command failed.'
    Wait-ScriptExit $helper 'Waiting helper ignored cancellation.'
    Require (-not (Test-Path -LiteralPath (Join-Path $waiting 'app\launches.txt'))) 'Cancelled device wait launched an app.'
    Require ((Read-Text (Join-Path $waiting 'autostart.log')) -match 'cancelled') 'Waiting cancellation was not logged.'
    Require (-not (Test-Path -LiteralPath (Join-Path $waiting 'autostart.disabled'))) 'Stop persistently disabled login startup.'
    Write-Output 'PASS: Cancelled device wait does not launch or disable future login startup.'

    $sentinelDirectory = New-Fixture 'normal'
    $sentinel = New-Object Diagnostics.Process
    $sentinel.StartInfo = New-Object Diagnostics.ProcessStartInfo
    $sentinel.StartInfo.FileName = Join-Path $sentinelDirectory 'app\WindowsTouchBar.exe'
    $sentinel.StartInfo.UseShellExecute = $false
    $sentinel.StartInfo.CreateNoWindow = $true
    [void]$sentinel.Start()
    [void]$children.Add($sentinel)
    Wait-Until { (Read-Text (Join-Path $sentinelDirectory 'app\WindowsTouchBar.status.log')) -match 'RUNNING:' } 'Same-name sentinel did not start.'

    $initializing = New-Fixture 'initializing'
    $helper = Start-IsolatedScript (Join-Path $initializing 'AutoStart.ps1')
    Wait-Until { $null -ne (Get-FakeApp $initializing) } 'Initializing fake app was not launched.'
    $owned = Get-FakeApp $initializing
    $stop = Start-IsolatedScript (Join-Path $initializing 'Stop.ps1')
    Wait-ScriptExit $stop 'Initialization cancellation command failed.'
    Wait-ScriptExit $helper 'Initializing helper ignored cancellation.'
    Require ($owned.WaitForExit(3000)) 'Cancelled initialization left its child running.'
    $sentinel.Refresh()
    Require (-not $sentinel.HasExited) 'Cancellation stopped a different same-name app.'
    Require (-not (Test-Path -LiteralPath (Join-Path $initializing 'autostart.disabled'))) 'Initialization cancellation changed future login preference.'
    Require ((Read-Text (Join-Path $initializing 'autostart.log')) -match 'cancelled during application initialization') 'Initialization cancellation was not logged.'
    Write-Output 'PASS: Cancelled initialization cleans only its own child, preserving another same-name process.'

    $normal = New-Fixture 'normal'
    $helper = Start-IsolatedScript (Join-Path $normal 'AutoStart.ps1')
    Wait-ScriptExit $helper 'Normal startup helper did not finish.'
    Require ((Read-Text (Join-Path $normal 'autostart.log')) -match 'RUNNING; process') 'Normal startup did not acknowledge RUNNING.'
    $running = Get-FakeApp $normal
    Require ($null -ne $running) 'Normal startup lost its app.'
    $stop = Start-IsolatedScript (Join-Path $normal 'Stop.ps1')
    Wait-ScriptExit $stop 'Normal stop command failed.'
    Require ($running.WaitForExit(3000)) 'Normal stop left its app running.'
    Require (-not (Test-Path -LiteralPath (Join-Path $normal 'autostart.disabled'))) 'Normal stop changed future login preference.'
    Write-Output 'PASS: Normal startup acknowledges RUNNING and Stop exits the app.'

    $failure = New-Fixture 'failure'
    $launcher = Start-IsolatedScript (Join-Path $failure 'Launch.ps1')
    Wait-ScriptExit $launcher 'Failed initialization was not rejected by manual launcher.' 1
    $log = Read-Text (Join-Path $failure 'launcher.log')
    Require ($log -match 'isolated initialization failure') 'Manual launcher did not preserve initialization failure details.'
    Require ($log -notmatch '(?m)^RUNNING |(?m)^Started ') 'Manual launcher falsely reported success.'
    Require ($null -eq (Get-FakeApp $failure)) 'Failed manual initialization left a child running.'
    Write-Output 'PASS: Manual launcher reports initialization failure instead of success.'

    Require ((Get-StartupPreference) -eq $originalPreference) 'Lifecycle tests changed the actual HKCU startup preference.'
    Write-Output '4/4 isolated lifecycle checks passed under Windows PowerShell 5.1; actual startup preference unchanged.'
} finally {
    foreach ($process in $children) {
        try { if (-not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(2000) } } catch {}
        $process.Dispose()
    }
    foreach ($directory in $fixtures) {
        $process = Get-FakeApp $directory
        if ($null -ne $process) {
            try { $process.Kill(); [void]$process.WaitForExit(2000) } catch {}
            $process.Dispose()
        }
    }
    # Verify the resolved recursive-delete target before removing test artifacts.
    $resolvedRoot = [IO.Path]::GetFullPath($fixtureRoot)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedRoot).StartsWith('TouchBarLifecycle-')) {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
