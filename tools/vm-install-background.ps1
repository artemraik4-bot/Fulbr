$ErrorActionPreference = 'Stop'
$vbm = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
$vm = 'FulbrWin11'
$dir = Join-Path (Split-Path -Parent $PSScriptRoot) 'vm'
$iso = Join-Path $dir 'Win11Eval.iso'
$jobIdFile = Join-Path $dir 'bits-job-id.txt'
$log = Join-Path $dir 'vm-install.log'
$expected = 'BC3F24086EBADC94489066B5AD78089E2CF5C3491E90E790BB81A2B199C10E38'
$vmPassword = $env:FULBR_VM_PASSWORD
if ([string]::IsNullOrWhiteSpace($vmPassword)) { throw 'Set FULBR_VM_PASSWORD before running this script.' }

function Log([string]$message) {
    $line = "$(Get-Date -Format o)  $message"
    Add-Content -Path $log -Value $line
    Write-Host $line
}

Log 'Waiting for BITS download to finish...'
$jobId = Get-Content $jobIdFile -Raw
$jobId = $jobId.Trim()
while ($true) {
    $job = Get-BitsTransfer -JobId $jobId -ErrorAction SilentlyContinue
    if (-not $job) { throw "BITS job $jobId not found." }
    Log "BITS state=$($job.JobState) bytes=$($job.BytesTransferred)/$($job.BytesTotal)"
    if ($job.JobState -eq 'Transferred') {
        Complete-BitsTransfer -BitsJob $job
        Log 'BITS transfer completed.'
        break
    }
    if ($job.JobState -in @('Error', 'TransientError', 'Cancelled')) {
        throw "BITS job ended in state $($job.JobState): $($job.ErrorDescription)"
    }
    Start-Sleep -Seconds 30
}

if (-not (Test-Path $iso)) { throw "ISO not found: $iso" }
$size = (Get-Item $iso).Length
Log "ISO size=$size"
if ($size -ne 8225329152) { throw "Unexpected ISO size: $size" }

Log 'Computing SHA-256...'
$hash = (Get-FileHash $iso -Algorithm SHA256).Hash
Log "ISO SHA-256=$hash"
if ($hash -ne $expected) { throw "ISO SHA-256 mismatch. Expected $expected" }

Log 'Attaching ISO...'
& $vbm storageattach $vm --storagectl 'IDE' --port 1 --device 0 --type dvddrive --medium $iso | Out-Null
& $vbm modifyvm $vm --boot1 dvd --boot2 disk --boot3 none --boot4 none | Out-Null

Log 'Starting unattended Windows 11 installation...'
& $vbm unattended install $vm --iso=$iso --user=fulbr --user-password=$vmPassword `
    --admin-password=$vmPassword --full-user-name='Fulbr Test' --install-additions `
    --locale=en_US --country=US --time-zone=UTC --hostname=fulbr-test.local --image-index=1 `
    --start-vm=headless 2>&1 | Tee-Object -FilePath $log -Append

Log 'Unattended install command returned. Windows installation continues in the VM.'
