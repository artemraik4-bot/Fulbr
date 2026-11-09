$ErrorActionPreference = 'Continue'
$log = 'C:\Users\user\Fulbr\tools\install-toolchain.log'
New-Item -ItemType Directory -Force -Path 'C:\Users\user\Fulbr\tools' | Out-Null
Start-Transcript -Path $log -Force

function Install-Package {
    param([string]$Id, [string]$Version)
    Write-Host "=== Installing $Id $Version ==="
    & winget install --id $Id --exact --version $Version --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    Write-Host "=== ExitCode for $Id : $LASTEXITCODE ==="
}

Install-Package -Id 'Git.Git' -Version '2.56.0.4'
Install-Package -Id 'Microsoft.DotNet.SDK.10' -Version '10.0.403'
Install-Package -Id 'JRSoftware.InnoSetup' -Version '6.7.3'

Write-Host '=== Final versions ==='
try { git --version } catch { Write-Host 'git not found' }
try { dotnet --version } catch { Write-Host 'dotnet not found' }
Write-Host ('ISCC exists: ' + (Test-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'))

Stop-Transcript
