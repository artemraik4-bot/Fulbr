param(
    [string]$VmName = 'FulbrWin11',
    [string]$GuestUser = 'fulbr',
    [string]$GuestPassword = $env:FULBR_VM_PASSWORD,
    [string]$Source = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'artifacts\service'),
    [string]$GuestRoot = 'C:\FulbrService'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GuestPassword)) {
    throw 'Set FULBR_VM_PASSWORD before running this script.'
}
$vbm = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$installer = Join-Path $repo 'tools\install-service.ps1'

if (-not (Test-Path (Join-Path $Source 'Fulbr.Service.exe'))) {
    throw "Service artifacts not found at $Source. Run tools\build-service.ps1 first."
}

Write-Host "Copying service artifacts to $VmName:$GuestRoot\service ..."
& $vbm guestcontrol $VmName copyto `
    --username $GuestUser --password $GuestPassword `
    --target-directory "$GuestRoot\service" --recursive $Source

Write-Host 'Copying installer script...'
& $vbm guestcontrol $VmName copyto `
    --username $GuestUser --password $GuestPassword `
    --target-directory $GuestRoot $installer

Write-Host 'Running service installer inside the VM (requires an elevated guest)...'
& $vbm guestcontrol $VmName run `
    --username $GuestUser --password $GuestPassword `
    --exe 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' `
    -- -NoProfile -ExecutionPolicy Bypass -File "$GuestRoot\install-service.ps1" `
       -Source "$GuestRoot\service" -InstallDir 'C:\Program Files\Fulbr\Service'

Write-Host 'Checking service status inside the VM...'
& $vbm guestcontrol $VmName run `
    --username $GuestUser --password $GuestPassword `
    --exe 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' `
    -- -NoProfile -Command "Get-Service FulbrSvc | Format-List Name,Status,StartType"

Write-Host 'If UAC blocked the installer, open the VM as Administrator and run:'
Write-Host "  $GuestRoot\install-service.ps1 -Source $GuestRoot\service"
