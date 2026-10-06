param(
    [string]$BaseUrl = 'http://127.0.0.1:5080',
    [Parameter(Mandatory=$true)][string]$Email,
    [Parameter(Mandatory=$true)][string]$Password
)

$ErrorActionPreference = 'Stop'
Write-Host '===== PROGNODE RC6.3 BATCH API SMOKE TEST =====' -ForegroundColor Cyan

$loginBody = @{ email = $Email; password = $Password } | ConvertTo-Json
$login = Invoke-RestMethod "$BaseUrl/api/access/login" -Method Post -ContentType 'application/json' -Body $loginBody
if (-not $login.authenticated -or -not $login.token) { throw 'Offline sign-in failed.' }
$headers = @{ 'X-PROGNODE-Session' = $login.token }

$current = Invoke-RestMethod "$BaseUrl/api/batches/current" -Headers $headers
if ($null -ne $current) {
    throw ("A Batch is already running: {0}. Complete/abort it before running this smoke test." -f $current.batchNo)
}

$batchNo = 'RC63-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$startBody = @{ batchNo=$batchNo; recipeName='RC6.3 Smoke'; operator='Developer Test Bench'; note='API contract smoke test' } | ConvertTo-Json
$started = Invoke-RestMethod "$BaseUrl/api/batches/start" -Method Post -Headers $headers -ContentType 'application/json' -Body $startBody
if ($started.batchNo -ne $batchNo -or $started.state -ne 'Running') { throw 'Start Batch contract failed.' }
Write-Host ("Started       : {0} ({1})" -f $started.batchNo, $started.id)

$current = Invoke-RestMethod "$BaseUrl/api/batches/current" -Headers $headers
if ($current.id -ne $started.id) { throw 'Get Current Batch contract failed.' }

$byId = Invoke-RestMethod "$BaseUrl/api/batches/$($started.id)" -Headers $headers
if ($byId.id -ne $started.id) { throw 'Get Batch by ID contract failed.' }

$recent = Invoke-RestMethod "$BaseUrl/api/batches?limit=10" -Headers $headers
if (-not @($recent).Where({ $_.id -eq $started.id })) { throw 'List Recent Batches contract failed.' }

$hist = Invoke-RestMethod "$BaseUrl/api/batches/$($started.id)/historian?maxRows=10" -Headers $headers
$alarms = Invoke-RestMethod "$BaseUrl/api/batches/$($started.id)/alarms?limit=10" -Headers $headers
Write-Host ("Historian rows: {0}" -f @($hist).Count)
Write-Host ("Alarm rows    : {0}" -f @($alarms).Count)

$completeBody = @{ note='RC6.3 API smoke PASS' } | ConvertTo-Json
$completed = Invoke-RestMethod "$BaseUrl/api/batches/$($started.id)/complete" -Method Post -Headers $headers -ContentType 'application/json' -Body $completeBody
if ($completed.state -ne 'Completed' -or -not $completed.endedAt) { throw 'Complete Batch contract failed.' }

$currentAfter = Invoke-RestMethod "$BaseUrl/api/batches/current" -Headers $headers
if ($null -ne $currentAfter) { throw 'Current Batch must be empty after completion.' }

Write-Host 'RC6.3 Batch/Lot API contract: PASS' -ForegroundColor Green
Write-Host 'NOTE: Modbus FC01/02/03/04 and analog alarm timing require the Developer Test Bench PLC simulator.' -ForegroundColor Yellow
Write-Host '================================================' -ForegroundColor Cyan
