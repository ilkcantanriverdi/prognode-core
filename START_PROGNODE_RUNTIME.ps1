# PROGNODE local source-pilot supervisor. No administrator privilege needed.
# Installed PROGNODECore Windows services use SCM restart instead of this file.
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$ProjectRoot,
 [Parameter(Mandatory)][string]$CoreDll,
 [Parameter(Mandatory)][string]$DataRoot,
 [Parameter(Mandatory)][string]$ExpectedVersion
)
$ErrorActionPreference='Stop'
$env:Prognode__DataRoot=$DataRoot
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_CONTENTROOT=Join-Path $ProjectRoot 'src\Prognode.Host'
$env:PROGNODE_CORE_SUPERVISED='1'
$marker=Join-Path $env:ProgramData 'PROGNODE\config\lan-restart-request.json'
$status=Join-Path $ProjectRoot 'RC647_SUPERVISOR_STATE.json'
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$iteration=0
while($true) {
 $iteration++
 $out=Join-Path $ProjectRoot 'RC647_CORE_STDOUT.log'
 $err=Join-Path $ProjectRoot 'RC647_CORE_STDERR.log'
 $p=Start-Process -FilePath $dotnet -ArgumentList @('"'+$CoreDll+'"') -WorkingDirectory $ProjectRoot -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
 $launchTime=[DateTimeOffset]::UtcNow
 [pscustomobject]@{supervisorPid=$PID;corePid=$p.Id;generation=$iteration;startedAtUtc=$launchTime.ToString('o');expectedVersion=$ExpectedVersion} | ConvertTo-Json | Set-Content -LiteralPath $status -Encoding UTF8
 $p.WaitForExit()
 # A crash or manual stop is not an approved repair. Exit instead of endlessly
 # respawning an unstable Core or bypassing an operator-initiated shutdown.
 $request=$null
 try {if(Test-Path -LiteralPath $marker -PathType Leaf){$request=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json}}catch{}
 $approved=$false
 if($request){
   $when=[DateTimeOffset]::MinValue
   $valid=[DateTimeOffset]::TryParse([string]$request.requestedAtUtc,[ref]$when)
   $approved=([int]$request.processId -eq [int]$p.Id) -and $valid -and
      $when -ge $launchTime -and $when -le [DateTimeOffset]::UtcNow.AddSeconds(10) -and
      ([DateTimeOffset]::UtcNow - $when).TotalMinutes -lt 3
 }
 if(-not $approved){break}
 Start-Sleep -Seconds 2
}
Remove-Item -LiteralPath $status -Force -ErrorAction SilentlyContinue
