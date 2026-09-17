$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'prepare-fixtures.ps1')
$petRoot = Split-Path -Parent $PSScriptRoot
$petResults = Join-Path $PSScriptRoot 'results'
New-Item -ItemType Directory -Path $petResults -Force | Out-Null
$petChatTestExe = Join-Path $petResults 'ChatTests.exe'
& (Join-Path $petRoot 'build.ps1') -OutputPath $petChatTestExe -ChatTests
$petTest = Start-Process -FilePath $petChatTestExe -ArgumentList ('"' + $petRoot + '"') -WindowStyle Hidden -PassThru
if (-not $petTest.WaitForExit(60000)) { $petTest.Kill(); throw 'Chat tests timed out.' }
Get-Content -LiteralPath (Join-Path $petResults 'chat-test-results.txt') -Encoding UTF8
if ($petTest.ExitCode -ne 0) { throw 'Chat tests failed.' }
