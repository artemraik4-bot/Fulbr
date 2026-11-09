$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
            [System.Environment]::GetEnvironmentVariable('Path', 'User')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host '=== Build service ==='
dotnet publish 'src\Fulbr.Service\Fulbr.Service.csproj' -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -o 'artifacts\service'

$service = 'artifacts\service\Fulbr.Service.exe'
$engine = 'artifacts\service\engine-service\sing-box.exe'
$wintun = 'artifacts\service\engine-service\wintun.dll'
foreach ($file in @($service, $engine, $wintun)) {
    if (-not (Test-Path $file)) { throw "Missing service artifact: $file" }
}

Write-Host '=== Service artifacts ==='
Get-Item $service, $engine, $wintun | Select-Object FullName, Length | Format-Table -AutoSize

Write-Host '=== SHA-256 ==='
Get-FileHash $service -Algorithm SHA256 | ForEach-Object {
    Write-Host "$($_.Path)"
    Write-Host $_.Hash
}
