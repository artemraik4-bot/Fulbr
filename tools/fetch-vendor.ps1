#requires -Version 5.1
<#
.SYNOPSIS
    Downloads the pinned third-party Fulbr runtime binaries and verifies them.

.DESCRIPTION
    Reads vendor/MANIFEST.json, downloads the exact sing-box and Wintun archives,
    verifies the archive SHA-256, extracts only the runtime files, verifies their
    SHA-256, and places them where the build expects them:

        artifacts/app/engine/sing-box.exe
        artifacts/service/engine-service/sing-box.exe
        artifacts/service/engine-service/wintun.dll

    Binaries are intentionally not committed to Git. Run this once after cloning,
    then run tools/build-release.ps1 and tools/build-service.ps1.

.PARAMETER Force
    Re-download archives and overwrite existing binaries.

.PARAMETER VerifyOnly
    Verify already downloaded/extracted binaries without network access.
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$manifestPath = Join-Path $root 'vendor\MANIFEST.json'
if (-not (Test-Path $manifestPath)) { throw "Missing vendor manifest: $manifestPath" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

$vendorDir = Join-Path $root 'vendor'
# Keep the upstream archive names so an existing verified download is reused.
$singBoxArchive = Join-Path $vendorDir (Split-Path $manifest.'sing-box'.source -Leaf)
$wintunArchive = Join-Path $vendorDir (Split-Path $manifest.wintun.source -Leaf)
$singBoxExtract = Join-Path $vendorDir 'extract'
$wintunExtract = Join-Path $vendorDir 'wintun-extract'

$appEngineDir = Join-Path $root 'artifacts\app\engine'
$serviceEngineDir = Join-Path $root 'artifacts\service\engine-service'

function Assert-Hash {
    param([string]$Path, [string]$Expected, [string]$Label)
    if (-not (Test-Path $Path)) { throw "Missing $Label : $Path" }
    $actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash
    if ($actual -ne $Expected.ToUpperInvariant()) {
        throw "SHA-256 mismatch for $Label`n  expected: $($Expected.ToUpperInvariant())`n  actual:   $actual`n  file:     $Path"
    }
    Write-Host "  OK $Label  $actual"
}

function Get-PinnedFile {
    param([string]$Url, [string]$Destination, [string]$Sha256)
    if ($VerifyOnly) {
        if (-not (Test-Path $Destination)) { throw "VerifyOnly: missing $Destination" }
        Assert-Hash $Destination $Sha256 'archive'
        return
    }
    if ($Force -or -not (Test-Path $Destination)) {
        Write-Host "  Downloading $Url"
        Invoke-WebRequest -Uri $Url -OutFile $Destination -UseBasicParsing
    } else {
        Write-Host "  Using cached $Destination"
    }
    Assert-Hash $Destination $Sha256 'archive'
}

Write-Host '=== sing-box ==='
Get-PinnedFile $manifest.'sing-box'.source $singBoxArchive $manifest.'sing-box'.archiveSha256
if (-not $VerifyOnly) {
    if ($Force -and (Test-Path $singBoxExtract)) { Remove-Item $singBoxExtract -Recurse -Force }
    if (-not (Test-Path $singBoxExtract)) {
        Expand-Archive -Path $singBoxArchive -DestinationPath $singBoxExtract -Force
    }
}

$singBoxExe = Get-ChildItem $singBoxExtract -Recurse -Filter 'sing-box.exe' -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $singBoxExe) { throw "sing-box.exe not found under $singBoxExtract" }
Assert-Hash $singBoxExe.FullName $manifest.'sing-box'.exeSha256 'sing-box.exe'

foreach ($dir in @($appEngineDir, $serviceEngineDir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item $singBoxExe.FullName (Join-Path $dir 'sing-box.exe') -Force
}

Write-Host '=== Wintun ==='
Get-PinnedFile $manifest.wintun.source $wintunArchive $manifest.wintun.archiveSha256
if (-not $VerifyOnly) {
    if ($Force -and (Test-Path $wintunExtract)) { Remove-Item $wintunExtract -Recurse -Force }
    if (-not (Test-Path $wintunExtract)) {
        Expand-Archive -Path $wintunArchive -DestinationPath $wintunExtract -Force
    }
}

$wintunDll = Get-ChildItem $wintunExtract -Recurse -Filter 'wintun.dll' -File |
    Where-Object { $_.FullName -match '\\amd64\\' } |
    Select-Object -First 1
if (-not $wintunDll) { throw "wintun.dll (amd64) not found under $wintunExtract" }
Assert-Hash $wintunDll.FullName $manifest.wintun.dllSha256 'wintun.dll'

New-Item -ItemType Directory -Force -Path $serviceEngineDir | Out-Null
Copy-Item $wintunDll.FullName (Join-Path $serviceEngineDir 'wintun.dll') -Force

Write-Host '=== Placed binaries ==='
Get-Item (Join-Path $appEngineDir 'sing-box.exe'),
        (Join-Path $serviceEngineDir 'sing-box.exe'),
        (Join-Path $serviceEngineDir 'wintun.dll') |
    Select-Object FullName, Length | Format-Table -AutoSize

Write-Host 'Vendor binaries verified.'
