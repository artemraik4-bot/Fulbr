$ErrorActionPreference = 'Continue'
$root = 'C:\FulbrService'
$pipeTest = Join-Path $root 'test-service-pipe.ps1'

Write-Host '=== service status before start ==='
& powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command status

$link = Get-Content (Join-Path $root 'test-link.txt') -Raw
Write-Host '=== start TUN ==='
& powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command start -Link $link

Write-Host '=== waiting 15 s ==='
Start-Sleep -Seconds 15

Write-Host '=== service status after start ==='
& powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command status

Write-Host '=== network adapters ==='
Get-NetAdapter | Format-Table Name, InterfaceDescription, Status, LinkSpeed -AutoSize

Write-Host '=== default routes ==='
Get-NetRoute -DestinationPrefix '0.0.0.0/0', '0.0.0.0/1', '128.0.0.0/1' -ErrorAction SilentlyContinue |
    Format-Table DestinationPrefix, NextHop, InterfaceAlias, RouteMetric -AutoSize

Write-Host '=== DNS servers ==='
Get-DnsClientServerAddress | Format-Table InterfaceAlias, AddressFamily, ServerAddresses -AutoSize

Write-Host '=== engine processes ==='
Get-Process sing-box -ErrorAction SilentlyContinue |
    Select-Object Id, Path, @{n = 'RAM_MB'; e = { [math]::Round($_.WorkingSet64 / 1MB, 1) } } |
    Format-List

Write-Host '=== service ==='
Get-Service FulbrSvc -ErrorAction SilentlyContinue | Format-List Name, Status, StartType

Write-Host '=== stop TUN ==='
& powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command stop
Start-Sleep -Seconds 5
& powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command status

Write-Host '=== adapters after stop ==='
Get-NetAdapter | Format-Table Name, InterfaceDescription, Status -AutoSize
