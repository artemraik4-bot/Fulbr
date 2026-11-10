$ErrorActionPreference = 'Continue'
$root = 'C:\Users\user\Fulbr'
$log = Join-Path $root 'vm\host-tun-test.log'
$svcExe = Join-Path $root 'artifacts\service\Fulbr.Service.exe'
$pipeTest = Join-Path $root 'tools\test-service-pipe.ps1'
$linkFile = Join-Path $root 'vm\test-link.txt'
$consoleOut = Join-Path $root 'vm\service-console.out.log'
$consoleErr = Join-Path $root 'vm\service-console.err.log'

function Log([string]$message) {
    $line = "$(Get-Date -Format o)  $message"
    Add-Content -Path $log -Value $line
    Write-Host $line
}

Set-Content -Path $log -Value "=== Fulbr temporary host TUN test ===" -Encoding UTF8

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'This test must run elevated.' }

if (-not (Test-Path $svcExe)) { throw "Missing service executable: $svcExe" }
if (-not (Test-Path $linkFile)) { throw "Missing test link: $linkFile" }
if (Get-Service FulbrSvc -ErrorAction SilentlyContinue) { throw 'FulbrSvc is installed; this temporary test expects no installed service.' }

Log 'Creating a System Restore checkpoint...'
try {
    Checkpoint-Computer -Description 'Before Fulbr temporary TUN test' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
    Log 'Restore checkpoint created.'
} catch {
    Log "Restore checkpoint could not be created: $($_.Exception.Message)"
}

Log 'Capturing pre-test network state...'
$preRoutes = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
    Select-Object DestinationPrefix, NextHop, InterfaceAlias, RouteMetric
$preDns = Get-DnsClientServerAddress -ErrorAction SilentlyContinue |
    Select-Object InterfaceAlias, AddressFamily, ServerAddresses
$preRoutes | Format-Table -AutoSize | Out-String | Add-Content $log
$preDns | Format-Table -AutoSize | Out-String | Add-Content $log

Log 'Starting Fulbr.Service in elevated console mode (not installed)...'
$svc = Start-Process -FilePath $svcExe -WorkingDirectory (Split-Path $svcExe -Parent) `
    -RedirectStandardOutput $consoleOut -RedirectStandardError $consoleErr -PassThru
Log "Service console PID=$($svc.Id)"

$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    $ping = & powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command ping 2>$null
    if ($ping -match '"ok"\s*:\s*true') { $ready = $true; break }
}
if (-not $ready) {
    Log 'IPC did not become ready.'
    if (-not $svc.HasExited) { Stop-Process -Id $svc.Id -Force }
    throw 'IPC readiness failed.'
}
Log 'IPC is ready.'

$status = & powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command status
Log "status: $status"

if (-not (Test-Path $linkFile)) { throw 'Test link missing.' }
$link = Get-Content $linkFile -Raw
Log 'Sending start (TUN)...'
$start = & powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command start -Link $link
Log "start response: $start"

Start-Sleep -Seconds 5
$status2 = & powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command status
Log "status after start: $status2"

Log 'Capturing TUN network state...'
Get-NetAdapter -ErrorAction SilentlyContinue |
    Select-Object Name, InterfaceDescription, Status, ifIndex, MacAddress |
    Format-Table -AutoSize | Out-String | Add-Content $log
Get-NetRoute -ErrorAction SilentlyContinue |
    Where-Object { $_.InterfaceAlias -match 'Fulbr' -or $_.DestinationPrefix -in @('0.0.0.0/0','0.0.0.0/1','128.0.0.0/1','::/0','::/1','8000::/1') } |
    Select-Object DestinationPrefix, NextHop, InterfaceAlias, RouteMetric |
    Format-Table -AutoSize | Out-String | Add-Content $log
Get-DnsClientServerAddress -ErrorAction SilentlyContinue |
    Select-Object InterfaceAlias, AddressFamily, ServerAddresses |
    Format-Table -AutoSize | Out-String | Add-Content $log
Get-Process sing-box -ErrorAction SilentlyContinue |
    Select-Object Id, Path, @{n='RAM_MB'; e={ [math]::Round($_.WorkingSet64/1MB,1) } } |
    Format-List | Out-String | Add-Content $log

Log 'Verifying route, DNS and HTTPS through the active TUN...'
$route = Find-NetRoute -RemoteIPAddress 1.1.1.1 -ErrorAction SilentlyContinue | Select-Object -First 1
if ($route) { Log "route to 1.1.1.1: interface=$($route.InterfaceAlias) nextHop=$($route.NextHop)" }
else { Log 'route lookup failed.' }
try {
    Resolve-DnsName -Name 'example.com' -Server '10.255.255.2' -Type A -DnsOnly -ErrorAction Stop | Out-Null
    Log 'TUN DNS query succeeded.'
} catch {
    Log "TUN DNS query failed: $($_.Exception.Message)"
}
try {
    Invoke-RestMethod -Uri 'https://api.ipify.org?format=json' -TimeoutSec 15 -ErrorAction Stop | Out-Null
    Log 'HTTPS request through the active route succeeded (value not logged).'
} catch {
    Log "HTTPS request failed: $($_.Exception.Message)"
}

Log 'Stopping TUN...'
$stop = & powershell -NoProfile -ExecutionPolicy Bypass -File $pipeTest -Command stop
Log "stop response: $stop"
Start-Sleep -Seconds 6

Log 'Capturing post-test state...'
$postRoutes = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
    Select-Object DestinationPrefix, NextHop, InterfaceAlias, RouteMetric
$postDns = Get-DnsClientServerAddress -ErrorAction SilentlyContinue |
    Select-Object InterfaceAlias, AddressFamily, ServerAddresses
$postRoutes | Format-Table -AutoSize | Out-String | Add-Content $log
$postDns | Format-Table -AutoSize | Out-String | Add-Content $log
Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Fulbr' } |
    Format-Table -AutoSize | Out-String | Add-Content $log

Log 'Stopping service console...'
if (-not $svc.HasExited) { Stop-Process -Id $svc.Id -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
Get-Process Fulbr.Service,sing-box -ErrorAction SilentlyContinue |
    Select-Object ProcessName, Id | Format-Table -AutoSize | Out-String | Add-Content $log

# Conservative cleanup for the one-off test: no installed service, no WFP.
Get-Process sing-box -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ($_.Path -like '*\Fulbr\artifacts\service\engine-service\sing-box.exe') { $_ | Stop-Process -Force }
    } catch { }
}
$configured = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'Fulbr' }
if ($configured) {
    Log 'WARNING: Fulbr adapter still present after stop.'
} else {
    Log 'No Fulbr adapter remains.'
}
$installed = Get-Service FulbrSvc -ErrorAction SilentlyContinue
if ($installed) { Log 'WARNING: FulbrSvc exists (unexpected).' } else { Log 'No FulbrSvc installed (expected).' }

Log '=== Temporary TUN test finished ==='
