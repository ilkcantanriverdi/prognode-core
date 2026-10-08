[CmdletBinding()]
param([string]$NuGetConfig, [switch]$UpdateLockFile)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
Push-Location $sourceRoot
try {
    $projects = @(
        'tests/BackupContract/BackupContract.csproj',
        'tests/QrPairingCoreContract/QrPairingCoreContract.csproj',
        'tests/ManualPairingContract/ManualPairingContract.csproj',
        'tests/MobileDeviceAckContract/MobileDeviceAckContract.csproj',
        'tests/CapacityContract/CapacityContract.csproj',
        'tests/ProtocolContract/ProtocolContract.csproj'
    )
    $restoreArgs = @('restore', 'PROGNODE.sln', '--nologo')
    if (-not $UpdateLockFile) { $restoreArgs += '--locked-mode' }
    if ($NuGetConfig) { $restoreArgs += @('--configfile', $NuGetConfig) }
    & dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
    & dotnet build PROGNODE.sln -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    foreach ($project in $projects) {
        $restoreArgs = @('restore', $project, '--nologo')
        if (-not $UpdateLockFile) { $restoreArgs += '--locked-mode' }
        if ($NuGetConfig) { $restoreArgs += @('--configfile', $NuGetConfig) }
        & dotnet @restoreArgs
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $project" }
        & dotnet run --project $project -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Contract failed: $project" }
    }
    Write-Host 'PASS: Release build and six console contract suites.'
} finally { Pop-Location }
