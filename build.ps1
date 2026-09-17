param([string]$OutputPath = '', [switch]$ChatTests)
$ErrorActionPreference = 'Stop'
$petRoot = $PSScriptRoot
$petCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $petCompiler)) {
    $petCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $petCompiler)) { throw 'Windows .NET Framework 4.x compiler not found.' }
if (-not $OutputPath) { $OutputPath = Join-Path $petRoot 'DesktopPet.exe' }
$petArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Net.Http.dll', '/r:System.Web.Extensions.dll', ('/out:' + $OutputPath))
$petArgs += Get-ChildItem -LiteralPath (Join-Path $petRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName }
$petArgs += '/r:System.Security.dll'
if ($ChatTests) { $petArgs += '/main:DesktopPet.ChatTests'; $petArgs += Get-ChildItem -LiteralPath (Join-Path $petRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName } }
$petIcon = Join-Path $petRoot 'tests\results\pet.ico'
if (Test-Path -LiteralPath $petIcon) { $petArgs += '/win32icon:' + $petIcon }
& $petCompiler @petArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output 'Built DesktopPet.exe. Double-click it to start.'
