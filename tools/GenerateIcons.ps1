# Regenerates every app icon asset from the logo's vector definition below (logo "A": a cobalt-blue
# rounded tile with a white envelope). Run from anywhere:  powershell -File tools\GenerateIcons.ps1
#
# Drawn with System.Drawing, so it needs nothing beyond Windows PowerShell. Small sizes use a
# bolder variant (thicker strokes, tile closer to the edge) because the regular strokes would be
# well under one pixel wide at 16-32px.
param(
    [string]$AssetsDir = (Join-Path $PSScriptRoot '..\src\MailClient.App\Assets')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$AssetsDir = (Resolve-Path $AssetsDir).Path

$tileColor = [System.Drawing.Color]::FromArgb(255, 0x2F, 0x5D, 0xE0)
$white = [System.Drawing.Color]::White

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Draws the logo into a square of side $size whose top-left corner is ($left, $top). The design
# lives on a 256-unit grid; $bold selects the heavier small-size variant.
function Draw-Logo($g, [float]$left, [float]$top, [float]$size, [bool]$bold) {
    $state = $g.Save()
    $g.TranslateTransform($left, $top)
    $g.ScaleTransform($size / 256, $size / 256)

    if ($bold) {
        $tile = @(6, 6, 244, 244, 58)
        $env = @(46, 74, 164, 112, 20)
        $stroke = 22
        $flap = @(@(60, 94), @(128, 152), @(196, 94))
    } else {
        $tile = @(16, 16, 224, 224, 56)
        $env = @(56, 80, 144, 98, 16)
        $stroke = 12
        $flap = @(@(64, 96), @(128, 142), @(192, 96))
    }

    $tilePath = New-RoundedRect $tile[0] $tile[1] $tile[2] $tile[3] $tile[4]
    $brush = New-Object System.Drawing.SolidBrush $tileColor
    $g.FillPath($brush, $tilePath)

    $pen = New-Object System.Drawing.Pen $white, $stroke
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $envPath = New-RoundedRect $env[0] $env[1] $env[2] $env[3] $env[4]
    $g.DrawPath($pen, $envPath)

    $points = @()
    foreach ($p in $flap) { $points += New-Object System.Drawing.PointF ([float]$p[0]), ([float]$p[1]) }
    $g.DrawLines($pen, [System.Drawing.PointF[]]$points)

    $pen.Dispose(); $brush.Dispose(); $tilePath.Dispose(); $envPath.Dispose()
    $g.Restore($state)
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

# Square asset: the logo fills `$fill` of the canvas, centered.
function Save-Square([string]$name, [int]$px, [double]$fill = 1.0) {
    $bmp, $g = New-Canvas $px $px
    $side = $px * $fill
    $o = ($px - $side) / 2
    Draw-Logo $g $o $o $side ($px -le 32)
    $g.Dispose()
    $bmp.Save((Join-Path $AssetsDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("{0,-58} {1}x{1}" -f $name, $px)
}

# Wide asset (wide tile / splash): the logo is `$logoHeight` of the height, centered.
function Save-Wide([string]$name, [int]$w, [int]$h, [double]$logoHeight) {
    $bmp, $g = New-Canvas $w $h
    $side = $h * $logoHeight
    Draw-Logo $g (($w - $side) / 2) (($h - $side) / 2) $side $false
    $g.Dispose()
    $bmp.Save((Join-Path $AssetsDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("{0,-58} {1}x{2}" -f $name, $w, $h)
}

# App list / taskbar / title bar icon: all scales, plus the unplated target sizes Windows asks for
# (unplated = no system-drawn backing plate behind the tile, which would show in our rounded corners).
Save-Square 'Square44x44Logo.png' 44
Save-Square 'Square44x44Logo.scale-200.png' 88
Save-Square 'Square44x44Logo.scale-400.png' 176
foreach ($s in 16, 24, 32, 48, 256) { Save-Square "Square44x44Logo.targetsize-${s}_altform-unplated.png" $s }

Save-Square 'StoreLogo.png' 50
Save-Square 'StoreLogo.scale-200.png' 100
Save-Square 'LockScreenLogo.png' 24
Save-Square 'LockScreenLogo.scale-200.png' 48

# Tiles: the logo sits in the middle with breathing room.
Save-Square 'Square71x71Logo.png' 71 0.72
Save-Square 'Square71x71Logo.scale-200.png' 142 0.72
Save-Square 'Square150x150Logo.png' 150 0.66
Save-Square 'Square150x150Logo.scale-200.png' 300 0.66
Save-Square 'Square310x310Logo.png' 310 0.64
Save-Square 'Square310x310Logo.scale-200.png' 620 0.64
Save-Wide 'Wide310x150Logo.png' 310 150 0.68
Save-Wide 'Wide310x150Logo.scale-200.png' 620 300 0.68
Save-Wide 'SplashScreen.png' 620 300 0.62
Save-Wide 'SplashScreen.scale-200.png' 1240 600 0.62

# AppIcon.ico: PNG-compressed frames (supported since Windows Vista), used for the window icon and tray.
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp, $g = New-Canvas $s $s
    Draw-Logo $g 0 0 $s ($s -le 32)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $AssetsDir 'AppIcon.ico'), $out.ToArray())
Write-Host ("{0,-58} {1} frames" -f 'AppIcon.ico', $sizes.Count)
