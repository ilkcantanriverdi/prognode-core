param(
    [string]$Base = "http://127.0.0.1:5080",
    [string]$LicensePath = "",
    [string]$Email = "",
    [string]$Password = "",
    [int]$ExpectedMaxTags = 0,
    [switch]$ExpectRevoked
)

$ErrorActionPreference = "Stop"
Write-Host "PROGNODE RC6.3.1 - license/session acceptance" -ForegroundColor Cyan

function Get-RedactedLicense {
    return Invoke-RestMethod "$Base/api/license" -TimeoutSec 5
}

if (-not [string]::IsNullOrWhiteSpace($LicensePath)) {
    if (-not (Test-Path $LicensePath)) { throw "License file not found: $LicensePath" }
    $temp = Join-Path $env:TEMP ("prognode-rc631-import-" + [guid]::NewGuid().ToString("N") + ".json")
    try {
        $status = & curl.exe -sS -o $temp -w "%{http_code}" -F "license=@$LicensePath" "$Base/api/license/import"
        $raw = Get-Content $temp -Raw
        $imported = $raw | ConvertFrom-Json
        if ([int]$status -lt 200 -or [int]$status -ge 300) {
            $message = if ($null -ne $imported.message -and -not [string]::IsNullOrWhiteSpace([string]$imported.message)) { [string]$imported.message } else { "License import failed with HTTP $status" }
            throw $message
        }
        if (-not $imported.imported) { throw "Import response did not report imported=true." }
        Write-Host ("Imported. Assigned user: {0} <{1}>" -f $imported.assignedUserName, $imported.assignedUserEmail) -ForegroundColor Green
    }
    finally {
        Remove-Item $temp -Force -ErrorAction SilentlyContinue
    }
}

$public = Get-RedactedLicense
if ($public.licenseInstalled -and $null -ne $public.expiresAt) {
    throw "Signed-out /api/license leaked expiresAt."
}
if ($public.licenseInstalled -and $null -ne $public.assignedUserEmail) {
    throw "Signed-out /api/license leaked assigned user email."
}
Write-Host "PASS: signed-out license metadata is redacted." -ForegroundColor Green

if ([string]::IsNullOrWhiteSpace($Email) -or [string]::IsNullOrWhiteSpace($Password)) {
    Write-Host "Login skipped. Supply -Email and -Password to validate authenticated details/custom maxTags/revocation." -ForegroundColor Yellow
    exit 0
}

$loginBody = @{ email = $Email; password = $Password } | ConvertTo-Json
$login = Invoke-RestMethod "$Base/api/access/login" -Method Post -ContentType "application/json" -Body $loginBody -TimeoutSec 10
if (-not $login.authenticated -or [string]::IsNullOrWhiteSpace($login.token)) { throw "Offline login failed." }
$headers = @{ "X-PROGNODE-Session" = $login.token }

$license = Invoke-RestMethod "$Base/api/license" -Headers $headers -TimeoutSec 5
$usage = Invoke-RestMethod "$Base/api/license/usage" -Headers $headers -TimeoutSec 5
Write-Host ("Authenticated status={0} plan={1} maxTags={2}" -f $license.status, $license.plan, $usage.maxTags)

if ($ExpectedMaxTags -gt 0 -and [int]$usage.maxTags -ne $ExpectedMaxTags) {
    throw "Expected maxTags=$ExpectedMaxTags but Core reported $($usage.maxTags)."
}
if ($ExpectedMaxTags -gt 0) {
    Write-Host "PASS: signed custom maxTags is effective at runtime." -ForegroundColor Green
}

if ($ExpectRevoked) {
    Write-Host "Waiting up to 90 seconds for server revocation..." -ForegroundColor Yellow
    $revoked = $false
    for ($i = 1; $i -le 9; $i++) {
        Start-Sleep -Seconds 10
        $license = Invoke-RestMethod "$Base/api/license" -Headers $headers -TimeoutSec 5
        Write-Host ("[{0}/9] effective status={1}" -f $i, $license.status)
        if ([string]$license.status -eq "REVOKED") { $revoked = $true; break }
    }
    if (-not $revoked) { throw "Core did not reach REVOKED within the acceptance window. Verify the Web heartbeat contract." }
    Write-Host "PASS: server revocation reached Core and replaced local ACTIVE lifecycle." -ForegroundColor Green
}

Write-Host "RC6.3.1 license/session acceptance complete." -ForegroundColor Green
