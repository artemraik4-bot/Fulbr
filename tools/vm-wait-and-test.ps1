$ErrorActionPreference = 'Continue'
$vbm = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
$vm = 'FulbrWin11'
$user = 'fulbr'
$pass = $env:FULBR_VM_PASSWORD
if ([string]::IsNullOrWhiteSpace($pass)) { throw 'Set FULBR_VM_PASSWORD before running this script.' }
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$log = Join-Path $repo 'vm\service-test.log'

function Log([string]$message) {
    $line = "$(Get-Date -Format o)  $message"
    Add-Content -Path $log -Value $line
    Write-Host $line
}

Log 'Waiting for Guest Additions / guest control to become available...'
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    $output = & $vbm guestcontrol $vm stat --username $user --password $pass 2>&1
    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        Log 'Guest is ready.'
        break
    }
    Log "guest not ready (attempt $($i + 1)/60)"
    Start-Sleep -Seconds 60
}

if (-not $ready) {
    Log 'Guest did not become ready within 60 minutes.'
    exit 1
}

# Give the desktop/first-logon a moment.
Start-Sleep -Seconds 20

Log 'Copying service artifacts...'
& $vbm guestcontrol $vm copyto --username $user --password $pass `
    --target-directory 'C:\FulbrService' --recursive (Join-Path $repo 'artifacts\service') 2>&1 | Out-Null

foreach ($file in @('tools\install-service.ps1', 'tools\test-service-pipe.ps1', 'tools\vm-guest-tests.ps1')) {
    & $vbm guestcontrol $vm copyto --username $user --password $pass `
        --target-directory 'C:\FulbrService' (Join-Path $repo $file) 2>&1 | Out-Null
}
& $vbm guestcontrol $vm copyto --username $user --password $pass `
    --target-directory 'C:\FulbrService' (Join-Path $repo 'vm\test-link.txt') 2>&1 | Out-Null

Log 'Installing service inside VM...'
& $vbm guestcontrol $vm run --username $user --password $pass `
    --exe 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' `
    -- -NoProfile -ExecutionPolicy Bypass -File 'C:\FulbrService\install-service.ps1' `
       -Source 'C:\FulbrService\service' -InstallDir 'C:\Program Files\Fulbr\Service' 2>&1 | Tee-Object -FilePath $log -Append

Log 'Running guest TUN tests...'
& $vbm guestcontrol $vm run --username $user --password $pass `
    --exe 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' `
    -- -NoProfile -ExecutionPolicy Bypass -File 'C:\FulbrService\vm-guest-tests.ps1' 2>&1 | Tee-Object -FilePath $log -Append

$shot = Join-Path $repo 'vm\service-test.png'
& $vbm controlvm $vm screenshotpng $shot 2>&1 | Out-Null
Log "Screenshot: $shot"
Log 'VM service test job finished.'
