$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:5080"

Write-Host "===== PROGNODE REMOTE ACCESS DIAGNOSTIC =====" -ForegroundColor Cyan

try {
    $identity = Invoke-RestMethod "$base/api/server/identity"
    Write-Host "Server:" $identity.displayName
    Write-Host "ServerId:" $identity.serverId
} catch {
    Write-Host "Core is not reachable on 127.0.0.1:5080" -ForegroundColor Red
    exit 1
}

try {
    $license = Invoke-RestMethod "$base/api/license"
    Write-Host "License:" $license.licenseId
    Write-Host "Plan:" $license.plan
    Write-Host "Remote entitlement:" $license.entitlements.remoteAccessEnabled
    if ($license.entitlements.remoteAccessUnlimited) {
        Write-Host "Signed capacity: Unlimited"
    } else {
        Write-Host "Signed capacity:" $license.entitlements.maxRemoteClients
    }
} catch {
    Write-Host "Could not read local license status:" $_.Exception.Message -ForegroundColor Yellow
}

try {
    $status = Invoke-RestMethod "$base/api/remote-access/status"
    Write-Host "`nCloud configured:" $status.cloudConfigured
    Write-Host "Server bound:" $status.serverBound
    Write-Host "Binding status:" $status.bindingStatus
    Write-Host "Subscription:" $status.subscriptionStatus
    if ($status.unlimitedClients) {
        Write-Host "Seats:" $status.usedClients "/ Unlimited"
    } else {
        Write-Host "Seats:" $status.usedClients "/" $status.maxClients "(remaining" $status.remainingClients ")"
    }
    Write-Host "Last sync:" $status.lastSyncedAtUtc
    if ($status.lastError) { Write-Host "Last error:" $status.lastError -ForegroundColor Yellow }
} catch {
    Write-Host "Remote Access status failed:" $_.Exception.Message -ForegroundColor Red
}

try {
    $clients = @(Invoke-RestMethod "$base/api/remote-access/clients")
    Write-Host "`nPaired clients:" $clients.Count
    foreach ($client in $clients) {
        $seat = if ($client.remoteEnabled) { "REMOTE" } else { "LAN ONLY" }
        Write-Host "-" $client.deviceName "|" $client.platform "|" $seat "| Local:" $client.localClientId "| Remote:" $client.remoteClientId
    }
} catch {
    Write-Host "Could not list clients:" $_.Exception.Message -ForegroundColor Yellow
}

Write-Host "`nDiagnostic is read-only. It does not bind, register or revoke clients." -ForegroundColor DarkGray
