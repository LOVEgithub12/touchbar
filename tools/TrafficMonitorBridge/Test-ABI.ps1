param([Parameter(Mandatory=$true)][string]$ZigPath)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Path build,plugins -Force|Out-Null
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'plugins/TouchBarCpuBridge.ini'),"[bridge]`r`ngpu_identifier=/gpu-amd/0`r`n",[Text.Encoding]::Unicode)

    foreach($name in @('FixtureBridge','BridgeAbiCheck')){
        & $ZigPath cc -target x86_64-windows-msvc -nostdlib -nostdinc -fno-rtti -fno-exceptions -O2 -c ($name+'.cpp') -o ('build/'+$name+'.obj')
        if($LASTEXITCODE -ne 0){throw ($name+' compilation failed')}
    }
    & $ZigPath cc -target x86_64-windows-gnu -shared build/FixtureBridge.obj -o build/FixtureBridge.dll
    if($LASTEXITCODE -ne 0){throw 'Fixture linking failed'}
    & $ZigPath cc -target x86_64-windows-gnu build/BridgeAbiCheck.obj -o build/BridgeAbiCheck.exe
    if($LASTEXITCODE -ne 0){throw 'ABI test linking failed'}
    & ./build/BridgeAbiCheck.exe
    if($LASTEXITCODE -ne 0){throw ('ABI test failed: '+$LASTEXITCODE)}
    Write-Output 'PASS: MSVC ABI, host API 0, GHz to MHz, CPU/GPU atomic v2, selected adapter, MHz parsing, missing/idle values, 10001 updates without metadata calls, unload invalidation and v2 restart with retained reader.'
}finally{Pop-Location}
