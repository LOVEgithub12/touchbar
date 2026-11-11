# usbipd writes UTF-8 regardless of Windows PowerShell's console code page.
# Keep the decoding local to this child process; do not change global encodings.
function Read-UsbIpState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [ValidateRange(100, 30000)][int]$TimeoutMilliseconds = 10000
    )
    $encoding = New-Object System.Text.UTF8Encoding($false, $true)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.Arguments = 'state'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $encoding
    $start.StandardErrorEncoding = $encoding
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'Could not start usbipd state.' }
        # Drain both pipes concurrently, including on error, to avoid a pipe deadlock.
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            $process.Kill()
            [void]$process.WaitForExit(1000)
            throw 'USBIP device enumeration timed out.'
        }
        $json = $output.GetAwaiter().GetResult()
        $errorText = $errors.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            $detail = $errorText.Trim()
            if ($detail.Length -gt 512) { $detail = $detail.Substring(0, 512) }
            throw ('USBIP device enumeration failed (exit ' + $process.ExitCode + '): ' + $detail)
        }
        try { $state = ConvertFrom-Json -InputObject $json -ErrorAction Stop }
        catch { throw 'USBIP device enumeration returned invalid JSON.' }
        if ($null -eq $state -or $null -eq $state.PSObject.Properties['Devices']) {
            throw 'USBIP device enumeration did not return a Devices list.'
        }
        return $state
    } finally { $process.Dispose() }
}
