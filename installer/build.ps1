param(
  [string]$Version = "1.0.0-beta.1",
  [string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
# Builds into artifacts\installer:
#   PROGNODE-Core-Setup-<version>.exe   Core service + tray agent for the plant PC (admin install)
#   PROGNODE-Client-Setup-<version>.exe alarm client for other Windows PCs on the plant network (per-user)
# Everything is published self-contained for win-x64, then compiled with Inno Setup 6.
# Code signing: set PROGNODE_SIGN_CMD (e.g. a signtool command with {file}) to sign the binaries and the setup.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts\publish"
if (!(Test-Path $Iscc)) { $Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (!(Test-Path $Iscc)) { throw "Inno Setup 6 (ISCC.exe) not found. Install with: winget install JRSoftware.InnoSetup" }

$numeric = ($Version -split '[-+]')[0]
if ($numeric -notmatch '^\d+\.\d+\.\d+$') { throw "Version must start with major.minor.patch: $Version" }
Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue

$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Version=$Version", "-p:InformationalVersion=$Version", "-p:DebugType=none")
dotnet publish (Join-Path $root "src\Prognode.Host\Prognode.Host.csproj") @common -o (Join-Path $publish "core")
if ($LASTEXITCODE -ne 0) { throw "Core publish failed" }
# Customer-facing process name is PROGNODE.Core.exe. Only the apphost is renamed: it carries the
# Prognode.Host.dll path, and an assembly rename would collide with Prognode.Core.dll.
Rename-Item (Join-Path $publish "core\Prognode.Host.exe") "PROGNODE.Core.exe"
# Windows App SDK runtime is bundled so the notification agent needs no separate runtime install.
dotnet publish (Join-Path $root "src\Prognode.Agent.Windows\Prognode.Agent.Windows.csproj") @common "-p:WindowsAppSDKSelfContained=true" -o (Join-Path $publish "agent")
if ($LASTEXITCODE -ne 0) { throw "Agent publish failed" }
dotnet publish (Join-Path $root "src\Prognode.Client.Windows\Prognode.Client.Windows.csproj") @common "-p:WindowsAppSDKSelfContained=true" -o (Join-Path $publish "client")
if ($LASTEXITCODE -ne 0) { throw "Client publish failed" }

# Never ship development leftovers.
Get-ChildItem $publish -Recurse -Include "appsettings.Development.json", "*.pdb" | Remove-Item -Force

function Invoke-Sign([string]$file) {
  if ($env:PROGNODE_SIGN_CMD) {
    $cmd = $env:PROGNODE_SIGN_CMD.Replace("{file}", "`"$file`"")
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $file" }
  }
}
if ($env:PROGNODE_SIGN_CMD) {
  Get-ChildItem $publish -Recurse -Include "Prognode*.exe", "Prognode*.dll", "PROGNODE*.exe", "PROGNODE*.dll" | ForEach-Object { Invoke-Sign $_.FullName }
}

foreach ($product in "Core", "Client") {
  & $Iscc "/Q" "/DAppVersion=$Version" "/DVersionInfo=$numeric.0" "/DPublishDir=$publish" (Join-Path $PSScriptRoot "PROGNODE-$product.iss")
  if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed: $product" }
  $setup = Join-Path $root "artifacts\installer\PROGNODE-$product-Setup-$Version.exe"
  Invoke-Sign $setup
  $hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
  "$hash  PROGNODE-$product-Setup-$Version.exe" | Set-Content -Encoding ascii "$setup.sha256"
  Write-Host "Built $setup" -ForegroundColor Green
  Write-Host "SHA-256 $hash"
}
