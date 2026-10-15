param(
  [string]$Version = "1.0.0-beta.1",
  [string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
# Builds into artifacts\installer:
#   PROGNODE-Monitor-Setup-<version>.exe   PROGNODE Monitor service + tray agent for the plant PC (admin install)
#   PROGNODE-Client-Setup-<version>.exe alarm client for other Windows PCs on the plant network (per-user)
# Everything is published self-contained for win-x64, then compiled with Inno Setup 6.
# Code signing: set PROGNODE_SIGN_CMD (e.g. a signtool command with {file}) to sign the binaries and the setup.
# Release signing: set PROGNODE_RELEASE_KEY to the offline Ed25519 private key file to write <setup>.sig (upload it with the .sha256).
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

# libsodium.dll (NSec: license signatures in Core, the device key in the Client) links the Visual C++
# runtime, which a clean Windows PC does not have. Ship vcruntime140.dll next to it (Microsoft's
# app-local deployment). Source: PROGNODE_VCRUNTIME, a Visual Studio redist folder, or System32.
$vcRuntime = $env:PROGNODE_VCRUNTIME
if (!$vcRuntime) {
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (Test-Path $vswhere) {
    $vs = & $vswhere -latest -products * -property installationPath
    if ($vs) { $vcRuntime = Get-ChildItem (Join-Path $vs "VC\Redist\MSVC") -Recurse -Filter vcruntime140.dll -ErrorAction SilentlyContinue |
      Where-Object FullName -match '\\x64\\Microsoft\.VC\d+\.CRT\\' | Select-Object -First 1 -ExpandProperty FullName }
  }
}
if (!$vcRuntime) { $vcRuntime = Join-Path $env:SystemRoot "System32\vcruntime140.dll" }
$vcSignature = Get-AuthenticodeSignature $vcRuntime
if ($vcSignature.Status -ne "Valid" -or $vcSignature.SignerCertificate.Subject -notmatch "O=Microsoft Corporation") {
  throw "vcruntime140.dll at $vcRuntime is missing or not signed by Microsoft. Install the Visual C++ Redistributable (x64) or set PROGNODE_VCRUNTIME."
}
foreach ($app in "core", "client") { Copy-Item $vcRuntime (Join-Path $publish $app) -Force }
Write-Host "Bundled $vcRuntime ($((Get-Item $vcRuntime).VersionInfo.FileVersion))"

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

foreach ($product in "Monitor", "Client") {
  & $Iscc "/Q" "/DAppVersion=$Version" "/DVersionInfo=$numeric.0" "/DPublishDir=$publish" (Join-Path $PSScriptRoot "PROGNODE-$product.iss")
  if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed: $product" }
  $setup = Join-Path $root "artifacts\installer\PROGNODE-$product-Setup-$Version.exe"
  Invoke-Sign $setup
  $hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
  "$hash  PROGNODE-$product-Setup-$Version.exe" | Set-Content -Encoding ascii "$setup.sha256"
  # In-app updates install only releases signed with the offline release key (installer/sign-release.cjs).
  if ($env:PROGNODE_RELEASE_KEY) {
    node (Join-Path $PSScriptRoot "sign-release.cjs") sign $setup $product $Version $env:PROGNODE_RELEASE_KEY
    if ($LASTEXITCODE -ne 0) { throw "Release signing failed: $setup" }
  } else {
    Write-Warning "PROGNODE_RELEASE_KEY is not set: $product $Version is unsigned and installed systems will not install it as an update."
  }
  Write-Host "Built $setup" -ForegroundColor Green
  Write-Host "SHA-256 $hash"
}
