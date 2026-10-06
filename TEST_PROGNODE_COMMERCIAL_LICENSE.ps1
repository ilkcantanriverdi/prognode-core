$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:5080'

Write-Host '===== PROGNODE WEB V1.8 LICENSE =====' -ForegroundColor Cyan
$license = Invoke-RestMethod "$base/api/license"
$usage = Invoke-RestMethod "$base/api/license/usage"

Write-Host ("Product        : {0}" -f $license.plan)
Write-Host ("Status         : {0}" -f $license.status)
Write-Host ("Pricing        : {0}" -f $license.pricingVersion)
Write-Host ("Billing        : {0}" -f $license.billingPeriod)
Write-Host ("Valid from     : {0}" -f $license.validFrom)
Write-Host ("Expires at     : {0}" -f $license.expiresAt)
Write-Host ("Grace until    : {0}" -f $license.graceUntil)
Write-Host ("Capacity state : {0}" -f $usage.capacityStatus)

if ($usage.unlimited) {
  $label = if ($usage.legacyUnlimited) { 'UNLIMITED (LEGACY_V1_7)' } else { 'UNLIMITED' }
  Write-Host ("Tag capacity   : {0}" -f $label)
  Write-Host ("Tags used      : {0}" -f $usage.usedTags)
} else {
  Write-Host ("Tag capacity   : {0}" -f $usage.maxTags)
  Write-Host ("Tags used      : {0} / {1}" -f $usage.usedTags, $usage.maxTags)
  Write-Host ("Tags remaining : {0}" -f $usage.remainingTags)
}

Write-Host ("Remote Access  : {0}" -f $license.entitlements.remoteAccessEnabled)
Write-Host ("Remote expires : {0}" -f $license.entitlements.remoteAccessExpiresAtUtc)
Write-Host '=========================================' -ForegroundColor Cyan
