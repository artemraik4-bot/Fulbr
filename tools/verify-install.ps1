$ErrorActionPreference = 'Continue'

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Fulbr'
$exe = Join-Path $installDir 'Fulbr.exe'
$startMenu = Get-ChildItem (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs') -Recurse -Filter 'Fulbr.lnk' -ErrorAction SilentlyContinue | Select-Object -First 1
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Fulbr.lnk'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'

Write-Host '=== Installed files ==='
Write-Host "Install dir: $installDir (exists: $(Test-Path $installDir))"
Write-Host "Fulbr.exe:   $(Test-Path $exe)"
Write-Host "Start Menu:  $([bool]$startMenu) ($($startMenu.FullName))"
Write-Host "Desktop:     $(Test-Path $desktop)"

Write-Host '=== Installed Apps entry ==='
Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -like 'Fulbr*' } |
    Select-Object DisplayName, DisplayVersion, InstallLocation, UninstallString |
    Format-List

if (Test-Path $exe) {
    Write-Host '=== Launch check ==='
    Get-Process Fulbr -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Start-Process -FilePath $exe
    $proc = $null
    while ($sw.Elapsed.TotalSeconds -lt 15) {
        $proc = Get-Process Fulbr -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($proc -and $proc.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 50
    }
    $sw.Stop()
    if ($proc -and $proc.MainWindowHandle -ne 0) {
        Write-Host "Launch OK: window in $([math]::Round($sw.Elapsed.TotalMilliseconds)) ms, RAM $([math]::Round($proc.WorkingSet64/1MB,1)) MB"
        Start-Sleep -Seconds 1
        Stop-Process -Id $proc.Id -Force
    } else {
        Write-Host 'Launch FAILED: no main window within 15 s.'
    }
}

Write-Host '=== Done ==='
