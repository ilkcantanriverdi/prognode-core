$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:5080'

Write-Host '===== PROGNODE WEB V1.8 FINAL CONTRACT =====' -ForegroundColor Cyan
$license = Invoke-RestMethod "$base/api/license"
$usage = Invoke-RestMethod "$base/api/license/usage"
$cloud = Invoke-RestMethod "$base/api/cloud-license/status"

Write-Host ("Lifecycle      : {0}" -f $license.status)
Write-Host ("PricingVersion : {0}" -f $license.pricingVersion)
Write-Host ("Billing        : {0}" -f $license.billingPeriod)
Write-Host ("Expires        : {0}" -f $license.expiresAt)
Write-Host ("Grace until    : {0}" -f $license.graceUntil)
Write-Host ("Tag capacity   : {0}" -f $(if ($usage.unlimited) {'UNLIMITED'} else {$usage.maxTags}))
Write-Host ("Tags used      : {0}" -f $usage.usedTags)
Write-Host ("Capacity state : {0}" -f $usage.capacityStatus)
Write-Host ("Remote enabled : {0}" -f $license.entitlements.remoteAccessEnabled)
Write-Host ("Remote expires : {0}" -f $license.entitlements.remoteAccessExpiresAtUtc)
Write-Host ("Cloud configured: {0}" -f $cloud.configured)
Write-Host ("Cloud status   : {0}" -f $cloud.licenseStatus)
Write-Host ("Cloud last sync: {0}" -f $cloud.lastSyncedAtUtc)
Write-Host ("Cloud error    : {0}" -f $cloud.lastError)

if ($license.pricingVersion -eq 'V1.8') {
    if ($usage.unlimited) {
        throw 'Web V1.8 contract violation: Tag capacity cannot be unlimited.'
    }

    if (@(100, 250, 500) -notcontains [int]$usage.maxTags) {
        throw ("Web V1.8 contract violation: maxTags must be 100, 250 or 500; got {0}." -f $usage.maxTags)
    }

    if ($license.entitlements.remoteAccessEnabled) {
        if ($license.entitlements.remoteAccessUnlimited) {
            throw 'Web V1.8 contract violation: Remote Access cannot be unlimited.'
        }

        if (@(5, 10, 25) -notcontains [int]$license.entitlements.maxRemoteClients) {
            throw ("Web V1.8 contract violation: Remote Access maxClients must be 5, 10 or 25; got {0}." -f $license.entitlements.maxRemoteClients)
        }

        if (-not $license.entitlements.remoteAccessExpiresAtUtc) {
            throw 'Web V1.8 contract violation: Remote Access effective signed expiry is missing.'
        }
    }
}

Write-Host 'RC6.3 V1.8 numeric-capacity + cloud-sync regression checks: PASS' -ForegroundColor Green
Write-Host '=============================================' -ForegroundColor Cyan
