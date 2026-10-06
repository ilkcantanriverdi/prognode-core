# Non-destructive HF4.1.2.1 regression check; may run without UAC.
$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot 'LAN_ACCESS_ELEVATED.ps1'
if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw "LAN helper not found: $path"}
$tokens=$null
$issues=$null
[System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$issues) | Out-Null
if ($null -ne $issues -and $issues.Count -gt 0){
  $issues | ForEach-Object { Write-Host ($_.Extent.StartLineNumber.ToString()+': '+$_.Message) -ForegroundColor Red }
  throw ('FAIL: LAN helper has '+$issues.Count+' parser error(s). Do not run UAC.')
}
$agentProject=Join-Path $PSScriptRoot 'src\Prognode.Agent.Windows\Prognode.Agent.Windows.csproj'
$projectContent=Get-Content -LiteralPath $agentProject -Raw
if($projectContent -notmatch 'LAN_ACCESS_ELEVATED\.ps1' -or $projectContent -notmatch 'CopyToOutputDirectory'){
  throw 'FAIL: Windows Agent must copy this helper to its bin output.'
}
Write-Host 'PASS: LAN helper PowerShell parser and Agent content-copy contract' -ForegroundColor Green
Write-Host 'This is not an end-to-end Windows UAC/TLS test.' -ForegroundColor Yellow
