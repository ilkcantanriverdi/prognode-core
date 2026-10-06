#requires -RunAsAdministrator
# Legacy alias. RC6.4.6 requires TLS before enabling the LAN API.
& (Join-Path $PSScriptRoot 'ENABLE_PROGNODE_LAN_HTTPS.ps1')
