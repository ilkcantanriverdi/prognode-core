$serviceName = "PROGNODECore"
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
  Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
  sc.exe delete $serviceName | Out-Null
  Write-Host "PROGNODE Core service removed." -ForegroundColor Green
} else {
  Write-Host "PROGNODE Core service is not installed."
}

# Remove only the rules owned by PROGNODE's HF4 LAN wizard.
# Never delete unrelated or legacy Private LAN firewall rules by display name.
$currentPrincipal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    @(Get-NetFirewallRule -Group 'PROGNODE LAN Access' -ErrorAction SilentlyContinue |
        Where-Object {$_.Name -match '^PROGNODE-MOBILE-LAN-\d+$'}) |
        ForEach-Object { Remove-NetFirewallRule -Name $_.Name -ErrorAction Stop }
    $state=Join-Path $env:ProgramData 'PROGNODE\lan-access-state.json'
    if (Test-Path $state) {Remove-Item -LiteralPath $state -Force}
    Write-Host 'HF4 app-owned scoped LAN rules cleaned up.' -ForegroundColor Green
} else {Write-Warning 'To remove PROGNODE-owned LAN firewall rules, rerun uninstall as Windows Administrator.'}
