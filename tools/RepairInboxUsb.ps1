[CmdletBinding()]
param([ValidateSet('Sfc','RestoreMissingInf')][string]$Mode = 'Sfc')
$ErrorActionPreference = 'Stop'
$workRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workRoot 'verification-20261108'
$targetDir = 'C:\Windows\System32\DriverStore\FileRepository\usb.inf_amd64_bbfaef91475db742'
$target = Join-Path $targetDir 'usb.inf'
$sourceDir = 'C:\Windows\WinSxS\amd64_dual_usb.inf_31bf3856ad364e35_10.0.19041.5794_none_b96e696680cd53f1'
$source = Join-Path $sourceDir 'usb.inf'
$published = 'C:\Windows\INF\usb.inf'
$expected = 'C698BABE1A3FD3C0C706F8C7AF1FEFAF43D4B2C90670ACEEF3653A1D4FA8193D'
$log = Join-Path $evidence ('repair-inbox-usb-' + $Mode + '.txt')
Start-Transcript -Path $log -Append | Out-Null
try {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator token required.' }
    if ((Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\DriverDatabase\DriverInfFiles\usb.inf').Active -ne (Split-Path $targetDir -Leaf)) { throw 'Registered USB package changed.' }
    foreach ($path in @($source,$published)) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw ('Original INF mismatch: ' + $path) }
    }
    $sourceSys = Join-Path $sourceDir 'usbccgp.sys'
    $targetSys = Join-Path $targetDir 'usbccgp.sys'
    if ((Get-FileHash -LiteralPath $sourceSys).Hash -ne (Get-FileHash -LiteralPath $targetSys).Hash) { throw 'USB binaries differ between component store and registered package.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $sourceSys
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*CN=Microsoft Windows,*') { throw 'Microsoft binary signature invalid.' }
    Write-Output ('Mode=' + $Mode + '; ' + (Get-Date -Format o))
    Write-Output ('RegisteredPackage=' + $targetDir)
    Write-Output ('TargetExistsBefore=' + (Test-Path -LiteralPath $target))
    $aclBefore = (Get-Acl -LiteralPath $targetDir).Sddl
    if ($Mode -eq 'Sfc') {
        $start = [Diagnostics.ProcessStartInfo]::new('C:\Windows\System32\sfc.exe', ('/scanfile="' + $target + '"'))
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.StandardOutputEncoding = [Text.Encoding]::Unicode
        $start.StandardErrorEncoding = [Text.Encoding]::Unicode
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        Write-Output $stdout.Result
        Write-Output $stderr.Result
        Write-Output ('SfcExitCode=' + $process.ExitCode)
    } else {
        if (Test-Path -LiteralPath $target) { throw 'Target already exists; refusing overwrite.' }
        # Restore only the missing, byte-identical Windows component. Do not alter ACLs or package metadata.
        Copy-Item -LiteralPath $source -Destination (Join-Path $evidence 'original-component-usb.inf')
        [IO.File]::Copy($source,$target,$false)
        if ((Get-FileHash -LiteralPath $target).Hash -ne $expected) { throw 'Restored INF hash mismatch.' }
        if ((Get-Acl -LiteralPath $targetDir).Sddl -ne $aclBefore) { throw 'Unexpected package directory ACL change.' }
    }
    Write-Output ('TargetExistsAfter=' + (Test-Path -LiteralPath $target))
    if (Test-Path -LiteralPath $target) { Get-FileHash -LiteralPath $target | Format-List | Out-String | Write-Output }
} catch {
    Write-Output ('ERROR: ' + $_.Exception.ToString())
    exit 1
} finally { Stop-Transcript | Out-Null }
