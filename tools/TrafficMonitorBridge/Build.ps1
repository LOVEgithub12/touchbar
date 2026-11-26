param([Parameter(Mandatory=$true)][string]$ZigPath,[string]$OutputDirectory=(Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$obj=Join-Path $OutputDirectory 'TouchBarCpuBridge.obj'
$dll=Join-Path $OutputDirectory 'TouchBarCpuBridge.dll'
# Compile the plugin with Microsoft's x64 C++ ABI; Zig supplies only the PE linker/startup and Win32 import libraries.
& $ZigPath cc -target x86_64-windows-msvc -nostdlib -nostdinc -fno-rtti -fno-exceptions -O2 -c (Join-Path $PSScriptRoot 'TouchBarCpuBridge.cpp') -o $obj
if($LASTEXITCODE -ne 0){throw 'Bridge compilation failed'}
& $ZigPath cc -target x86_64-windows-gnu -shared $obj -o $dll
if($LASTEXITCODE -ne 0){throw 'Bridge linking failed'}
Get-FileHash -LiteralPath $dll
