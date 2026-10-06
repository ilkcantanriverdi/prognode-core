param(
    [Parameter(Mandatory=$true)]
    [string]$LicensePath,

    [string]$CoreUrl = "http://127.0.0.1:5080",

    [string]$Email = "prognode@outlook.com"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $LicensePath)) {
    throw "License file not found: $LicensePath"
}

Write-Host "PROGNODE DEV12.4 offline license/auth smoke test" -ForegroundColor Cyan
Write-Host "Core: $CoreUrl"
Write-Host "License: $LicensePath"
Write-Host ""

# Import using Windows 11 curl.exe. No cloud endpoint is contacted by this script.
$importTemp = [System.IO.Path]::GetTempFileName()
try {
    $status = & curl.exe -sS -o $importTemp -w "%{http_code}" -F "license=@$LicensePath" "$CoreUrl/api/license/import"
    $importBody = Get-Content -LiteralPath $importTemp -Raw
    if ($status -lt 200 -or $status -ge 300) {
        throw "License import failed (HTTP $status): $importBody"
    }
    Write-Host "[PASS] signed license import accepted" -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $importTemp -Force -ErrorAction SilentlyContinue
}

$secure = Read-Host "Password (kept only in process memory for this request)" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $body = @{ email = $Email; password = $password } | ConvertTo-Json -Compress
    $result = Invoke-RestMethod -Method Post -Uri "$CoreUrl/api/access/login" -ContentType "application/json" -Body $body
    if (-not $result.authenticated) {
        throw "Login response was not authenticated."
    }
    Write-Host "[PASS] correct account password accepted by local Core" -ForegroundColor Green

    $token = $result.token
    if ($token) {
        $statusResult = Invoke-RestMethod -Method Get -Uri "$CoreUrl/api/access/status" -Headers @{ "X-PROGNODE-Session" = $token }
        if (-not $statusResult.authenticated) {
            throw "Session validation failed after login."
        }
        Write-Host "[PASS] local session opened and validated" -ForegroundColor Green
    }
}
finally {
    if ($password) { $password = $null }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

$wrongBody = @{ email = $Email; password = "PROGNODE-INTENTIONALLY-WRONG-PASSWORD" } | ConvertTo-Json -Compress
try {
    Invoke-RestMethod -Method Post -Uri "$CoreUrl/api/access/login" -ContentType "application/json" -Body $wrongBody | Out-Null
    throw "Wrong password was unexpectedly accepted."
}
catch {
    if ($_.Exception.Message -eq "Wrong password was unexpectedly accepted.") { throw }
    Write-Host "[PASS] wrong password rejected" -ForegroundColor Green
}

Write-Host ""
Write-Host "For the strict commercial path, start VS Code profile: PROGNODE Server — License Test" -ForegroundColor Yellow
Write-Host "That profile sets Prognode__AllowDevelopmentFallback=false." -ForegroundColor Yellow
