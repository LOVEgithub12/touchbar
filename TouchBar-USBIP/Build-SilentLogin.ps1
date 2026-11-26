[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(!$OutputDirectory){$OutputDirectory=$PSScriptRoot}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe ('/out:'+(Join-Path $OutputDirectory 'SilentLogin.exe')) (Join-Path $PSScriptRoot 'SilentLogin.cs')
if($LASTEXITCODE -ne 0){throw 'Silent login launcher build failed.'}
