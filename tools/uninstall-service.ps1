#Requires -RunAsAdministrator
$ErrorActionPreference = 'Continue'

Write-Host 'Stopping Fulbr service...'
Stop-Service -Name 'FulbrSvc' -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host 'Deleting Fulbr service...'
& sc.exe delete FulbrSvc

Write-Host 'Stopping any orphaned service engine...'
Get-Process sing-box -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $path = $_.MainModule.FileName
        if ($path -like '*\Fulbr\Service\engine-service\sing-box.exe') {
            $_ | Stop-Process -Force
        }
    } catch { }
}

$installDir = Join-Path $env:ProgramFiles 'Fulbr\Service'
if (Test-Path $installDir) {
    Write-Host "Removing $installDir"
    Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Fulbr service removed. User proxy-mode data under %LOCALAPPDATA%\Fulbr is preserved.'
