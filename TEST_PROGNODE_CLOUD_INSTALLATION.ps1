$ErrorActionPreference = "Stop"
$Base = "http://127.0.0.1:5080"

Write-Host "PROGNODE RC6.3.1 - Cloud installation registration check" -ForegroundColor Cyan
Write-Host "Waiting up to 60 seconds for activation / heartbeat..."

$last = $null
for ($i = 1; $i -le 12; $i++) {
    try {
        $last = Invoke-RestMethod "$Base/api/cloud-license/status" -TimeoutSec 5
        $configured = [bool]$last.configured
        $synced = -not [string]::IsNullOrWhiteSpace([string]$last.activationToken) -and $null -ne $last.lastSyncedAtUtc

        Write-Host ("[{0:00}/12] configured={1} status={2} synced={3}" -f $i, $configured, $last.licenseStatus, $synced)
        if ($synced) {
            Write-Host "PASS: this Core has registered/synchronized with PROGNODE Web." -ForegroundColor Green
            $last | Format-List
            exit 0
        }
    }
    catch {
        Write-Host ("[{0:00}/12] Local Core status endpoint unavailable: {1}" -f $i, $_.Exception.Message) -ForegroundColor Yellow
    }

    Start-Sleep -Seconds 5
}

Write-Host "Cloud installation has not synchronized yet." -ForegroundColor Yellow
if ($null -ne $last) {
    $last | Format-List
    if (-not [string]::IsNullOrWhiteSpace([string]$last.lastError)) {
        Write-Host ("LastError: " + $last.lastError) -ForegroundColor Red
    }
}
Write-Host "Offline monitoring remains independent from this cloud registration check."
exit 1
