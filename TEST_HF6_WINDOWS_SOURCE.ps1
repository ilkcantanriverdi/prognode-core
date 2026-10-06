$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location -LiteralPath $root
Write-Host '[HF6] Checking source resources and Windows PowerShell syntax...'
$required = @(
  'src/Prognode.Host/wwwroot/index.html',
  'src/Prognode.Host/wwwroot/customer-v2.js',
  'src/Prognode.Host/wwwroot/customer-v2.css',
  'src/Prognode.Host/wwwroot/trend-hf3plus.html',
  'src/Prognode.Host/wwwroot/trend-hf3plus.js',
  'LAN_ACCESS_ELEVATED.ps1',
  'PROGNODE.sln'
)
foreach ($rel in $required) {
  $path = Join-Path $root $rel
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $rel" }
}
foreach ($rel in @('LAN_ACCESS_ELEVATED.ps1','START_PROGNODE_RC6_4_7.ps1')) {
  $t=$null; $errors=$null
  [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root $rel),[ref]$t,[ref]$errors) | Out-Null
  if ($errors -and $errors.Count) { $errors | ForEach-Object { Write-Error $_ }; throw "PowerShell parse failed: $rel" }
}
Write-Host '[PASS] Required sources and PowerShell syntax.' -ForegroundColor Green
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK required for Windows acceptance' }
& dotnet build (Join-Path $root 'PROGNODE.sln') -c Debug --nologo
if ($LASTEXITCODE -ne 0) { throw 'HF6 .NET build failed. Do not start as customer release.' }
Write-Host '[PASS] HF6 .NET build. Proceed to localhost/UI and phone tests (see README).' -ForegroundColor Green
