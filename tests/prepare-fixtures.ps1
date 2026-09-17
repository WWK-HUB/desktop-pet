$ErrorActionPreference = 'Stop'
$petFixtureDirectory = Join-Path $PSScriptRoot 'fixtures\demo'
$petFixtureImage = Join-Path $petFixtureDirectory 'assets\character-sheet.png'
# Preserve an existing local fixture. A clean clone generates synthetic artwork.
if (Test-Path -LiteralPath $petFixtureImage) { return }
Add-Type -AssemblyName System.Drawing
$petDefinition = Get-Content -LiteralPath (Join-Path $petFixtureDirectory 'character.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$petBitmap = New-Object System.Drawing.Bitmap(1280, 1280)
$petGraphics = [System.Drawing.Graphics]::FromImage($petBitmap)
$petBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 165, 220))
try {
    $petGraphics.Clear([System.Drawing.Color]::Black)
    foreach ($petSprite in $petDefinition.sprites.PSObject.Properties) {
        $petRegion = $petSprite.Value.region
        $petGraphics.FillEllipse($petBrush, [int]($petRegion[0] + 8), [int]($petRegion[1] + 8), [int]($petRegion[2] - 16), [int]($petRegion[3] - 16))
    }
    New-Item -ItemType Directory -Path (Split-Path $petFixtureImage -Parent) -Force | Out-Null
    $petBitmap.Save($petFixtureImage, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $petBrush.Dispose()
    $petGraphics.Dispose()
    $petBitmap.Dispose()
}
