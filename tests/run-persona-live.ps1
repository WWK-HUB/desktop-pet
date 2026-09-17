$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'prepare-fixtures.ps1')
$petRoot = Split-Path -Parent $PSScriptRoot
$petResults = Join-Path $PSScriptRoot 'results'
New-Item -ItemType Directory -Path $petResults -Force | Out-Null
$petLiveExe = Join-Path $petResults 'PersonaLiveTests.exe'
& (Join-Path $petRoot 'build.ps1') -OutputPath $petLiveExe -ChatTests
# Explicit opt-in script: sends two short questions using the locally saved API settings.
# Original character files and model settings are never modified.
$petProcess = Start-Process -FilePath $petLiveExe -ArgumentList @(('"' + $petRoot + '"'), '--live-persona') -WindowStyle Hidden -PassThru
$petDone = $false
for ($petWait = 0; $petWait -lt 7; $petWait++) {
    if ($petProcess.WaitForExit(60000)) { $petDone = $true; break }
    Write-Output 'Still waiting for the configured model...'
}
if (-not $petDone) { $petProcess.Kill(); throw 'Live persona comparison timed out.' }
Get-Content -LiteralPath (Join-Path $petResults 'persona-live-results.txt') -Encoding UTF8
if ($petProcess.ExitCode -ne 0) { throw 'Live persona test process failed.' }
