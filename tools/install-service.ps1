#Requires -RunAsAdministrator
param(
    [string]$Source = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'artifacts\service'),
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Fulbr\Service')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Source 'Fulbr.Service.exe'))) {
    throw "Service publish output not found at $Source. Run tools\build-service.ps1 first."
}

$existing = Get-Service -Name 'FulbrSvc' -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'Stopping existing Fulbr service...'
    Stop-Service -Name 'FulbrSvc' -Force -ErrorAction SilentlyContinue
    & sc.exe delete FulbrSvc | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Installing service files to $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $Source '*') -Destination $InstallDir -Recurse -Force

$exe = Join-Path $InstallDir 'Fulbr.Service.exe'
if (-not (Test-Path $exe)) { throw "Service executable missing after copy: $exe" }

Write-Host 'Creating Windows service...'
& sc.exe create FulbrSvc "binPath= `"$exe`"" "start= auto" "obj= LocalSystem" "DisplayName= Fulbr Service"
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE" }

& sc.exe description FulbrSvc "Fulbr privileged tunnel service (TUN, routes, engine supervision)"
& sc.exe failure FulbrSvc reset= 86400 actions= restart/5000/restart/10000/restart/30000

Write-Host 'Starting service...'
& sc.exe start FulbrSvc
Start-Sleep -Seconds 2
Get-Service FulbrSvc | Select-Object Name, Status, StartType

Write-Host ''
Write-Host 'Service installed. Test the pipe with:'
Write-Host "  powershell -File tools\test-service-pipe.ps1 -Command status"
Write-Host 'To uninstall: tools\uninstall-service.ps1'
