$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'prepare-fixtures.ps1')
$petRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $petRoot 'build.ps1')
$petTest = Start-Process -FilePath (Join-Path $petRoot 'DesktopPet.exe') -ArgumentList '--self-test --character tests/fixtures/demo' -WindowStyle Hidden -PassThru
if (-not $petTest.WaitForExit(60000)) { $petTest.Kill(); throw 'Self-test timed out.' }
if ($petTest.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'results\FAILED.txt') -ErrorAction SilentlyContinue
    throw 'Self-test failed.'
}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'results\test-results.txt')
