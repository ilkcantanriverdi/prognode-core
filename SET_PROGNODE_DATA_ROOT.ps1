# HF6.2 developer-pilot data-root selection. Do NOT put license secrets here.
[CmdletBinding(DefaultParameterSetName='Existing')]
param(
 [Parameter(ParameterSetName='Existing')][string]$Path,
 [Parameter(ParameterSetName='New',Mandatory)][switch]$CreateNew
)
$ErrorActionPreference='Stop'
if($CreateNew){
 $Path=Join-Path $env:LOCALAPPDATA 'PROGNODE\data'
 if(Test-Path -LiteralPath $Path){
   $prior=@(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
   if($prior.Count -gt 0){ throw "This folder is not empty: $Path. Refusing to overwrite existing data." }
 } else {New-Item -ItemType Directory -Path $Path -Force | Out-Null}
 $marker=Join-Path $Path '.prognode-first-run-approved'
 [IO.File]::WriteAllText($marker,'First-run approved on '+[DateTimeOffset]::UtcNow.ToString('o'))
 Write-Host 'An empty pilot Core is authorized for first start; do NOT use this on an existing factory PC.' -ForegroundColor Yellow
} else {
 if([string]::IsNullOrWhiteSpace($Path)){ throw 'Provide -Path "C:\old-prognode\data" for an EXISTING project, or -CreateNew for a clean pilot PC.' }
 $Path=[IO.Path]::GetFullPath($Path)
 if(-not (Test-Path -LiteralPath (Join-Path $Path 'prognode.db') -PathType Leaf)){
    throw "Existing PROGNODE database not found at $Path. No pointer changed."
 }
}
$absolute=[IO.Path]::GetFullPath($Path)
$pointerDir=Join-Path $env:LOCALAPPDATA 'PROGNODE'
$pointer=Join-Path $pointerDir 'dev-data-root.txt'
New-Item -ItemType Directory -Path $pointerDir -Force | Out-Null
if(Test-Path $pointer){
 $previous=[string](Get-Content -LiteralPath $pointer -Raw).Trim()
 if($previous -and $previous -ne $absolute){
   Write-Warning "The saved data path was: $previous"
   $confirm=Read-Host 'To CHANGE the selected project folder, type CHANGE'
   if($confirm -cne 'CHANGE'){throw 'Project data-root selection unchanged.'}
 }
}
$tmp=Join-Path $pointerDir ('dev-data-root.'+[guid]::NewGuid().ToString('N')+'.tmp')
try{[IO.File]::WriteAllText($tmp,$absolute);Move-Item -LiteralPath $tmp -Destination $pointer -Force -ErrorAction Stop}
finally{if(Test-Path $tmp){Remove-Item -LiteralPath $tmp -Force}}
Write-Host "Saved stable developer-project data path: $absolute" -ForegroundColor Green
Write-Host 'Now use START_PROGNODE.cmd from any new HF6.2+ project ZIP.'
