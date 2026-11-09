$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
            [System.Environment]::GetEnvironmentVariable('Path', 'User')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host '=== Restore ==='
dotnet restore

Write-Host '=== Tests ==='
dotnet test -c Release --no-restore

Write-Host '=== Publish (self-contained win-x64) ==='
$publish = Join-Path $root 'artifacts\app'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish 'src\Fulbr.App\Fulbr.App.csproj' -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publish

$exe = Join-Path $publish 'Fulbr.exe'
if (-not (Test-Path $exe)) { throw "Publish did not produce $exe" }

Write-Host '=== Installer ==='
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
}
if (-not (Test-Path $iscc)) { throw 'Inno Setup 6 (ISCC.exe) not found.' }

& $iscc 'installer\Fulbr.iss'

$installer = Join-Path $root 'artifacts\installer\Fulbr-Setup.exe'
if (-not (Test-Path $installer)) { throw "Installer was not produced: $installer" }

Write-Host '=== Artifacts ==='
Get-Item $exe, $installer | Select-Object FullName, Length | Format-Table -AutoSize

Write-Host '=== SHA-256 ==='
Get-FileHash $exe, $installer -Algorithm SHA256 | ForEach-Object {
    Write-Host "$($_.Path)"
    Write-Host $_.Hash
    Write-Host ''
}
