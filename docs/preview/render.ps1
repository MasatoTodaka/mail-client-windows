# Renders docs/preview/preview.html to docs/images/light.png and dark.png with headless Edge.
# Usage: powershell -File docs\preview\render.ps1
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$html = (Resolve-Path (Join-Path $PSScriptRoot 'preview.html')).Path
$images = Join-Path $root 'docs\images'
New-Item -ItemType Directory -Force $images | Out-Null

$edge = @('C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', 'C:\Program Files\Microsoft\Edge\Application\msedge.exe') |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found.' }

foreach ($theme in 'light', 'dark') {
    $out = Join-Path $images "$theme.png"
    $url = ([System.Uri]$html).AbsoluteUri + "?theme=$theme"
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
        --window-size=1280,800 --virtual-time-budget=4000 "--screenshot=$out" $url | Out-Null
    Write-Host "$theme -> $out ($((Get-Item $out).Length) bytes)"
}

# The logo shown at the top of the README.
Copy-Item (Join-Path $root 'src\MailClient.App\Assets\Square44x44Logo.targetsize-256_altform-unplated.png') (Join-Path $images 'logo.png') -Force
