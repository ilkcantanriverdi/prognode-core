param([switch]$SkipContractTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    Write-Host 'PROGNODE HF6.4 Windows source verification' -ForegroundColor Cyan
    dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'NET 10 SDK missing' }
    dotnet build .\PROGNODE.sln -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'HF6.4 solution build failed' }
    if (-not $SkipContractTests) {
        dotnet run --project .\tests\MobileDeviceAckContract\MobileDeviceAckContract.csproj -c Debug
        if ($LASTEXITCODE -ne 0) { throw 'Device ACK contract tests failed' }
    }
    Write-Host 'Local .NET source checks passed. Run physical Android QR / ACK tests next.' -ForegroundColor Green
}
finally { Pop-Location }
