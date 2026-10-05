<#
.SYNOPSIS
    Builds the app icons from the pixel-art source.

.DESCRIPTION
    1. icon-source.jpg (a JPEG of the pixel art, with compression noise and a white background) is sampled
       cell by cell into icon-pixels.png: the clean 32x13 artwork, transparent background, exact colours.
       Skipped when icon-pixels.png already exists, unless -Resample is given.
    2. icon-pixels.png is centred on a 32x32 canvas. Empty cells enclosed by the outline are painted white
       (as in the source drawing), so the harpoon stays readable on a dark taskbar; only the outside is transparent.
       The canvas is then rendered at every icon size:
         - 32, 64, 128, 256 : nearest neighbour (exact integer multiples, perfectly crisp);
         - 16..48           : high-quality bicubic from 256 (-Small ink: each target pixel takes the darkest
                              source pixel it covers instead; crisper strokes but the white interior is lost).
    3. Writes GetOverHere.ico (active) and GetOverHere-paused.ico (greyed out, semi-transparent), and their
       dark-taskbar counterparts GetOverHere-dark.ico / GetOverHere-dark-paused.ico, where the colours are
       inverted: the navy outline and the white interior swap (white harpoon), other colours get their
       lightness inverted. Also writes icon-256.png and icon-256-dark.png for documentation. Sizes up to 64 are stored as 32-bit DIBs (readable everywhere,
       including System.Drawing.Icon); 128 and 256 as PNG.

.EXAMPLE
    pwsh GetOverHere/Assets/make-icons.ps1
#>
param([switch]$Resample, [ValidateSet('smooth', 'ink')][string]$Small = 'smooth')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$pixelsPath = Join-Path $here 'icon-pixels.png'

# ───────────── 1. JPEG -> clean pixel art ─────────────
if ($Resample -or -not (Test-Path $pixelsPath)) {
    $jpg = [System.Drawing.Bitmap]::FromFile((Join-Path $here 'icon-source.jpg'))
    try {
        # Bounding box of the artwork and grid size, measured on the source (cells of ~23.5 px).
        $left = 41; $top = 272; $right = 791; $bottom = 577; $cols = 32; $rows = 13
        $cw = ($right - $left + 1) / $cols; $ch = ($bottom - $top + 1) / $rows

        # Average a small patch at the centre of each cell (ignores JPEG noise near the edges).
        $samples = @()
        for ($r = 0; $r -lt $rows; $r++) {
            for ($c = 0; $c -lt $cols; $c++) {
                $cx = [int]($left + ($c + 0.5) * $cw); $cy = [int]($top + ($r + 0.5) * $ch)
                $sr = 0; $sg = 0; $sb = 0; $n = 0
                for ($dy = -5; $dy -le 5; $dy++) { for ($dx = -5; $dx -le 5; $dx++) {
                    $p = $jpg.GetPixel($cx + $dx, $cy + $dy); $sr += $p.R; $sg += $p.G; $sb += $p.B; $n++ } }
                $samples += [pscustomobject]@{ R = $sr / $n; G = $sg / $n; B = $sb / $n; Row = $r; Col = $c }
            }
        }

        # Background = near white. Remaining cells are grouped into colour clusters, each painted with its mean.
        $ink = @($samples | Where-Object { ($_.R + $_.G + $_.B) -lt 720 })
        $clusters = @()
        foreach ($s in $ink) {
            $match = $clusters | Where-Object { [math]::Abs($_.R - $s.R) + [math]::Abs($_.G - $s.G) + [math]::Abs($_.B - $s.B) -lt 40 } | Select-Object -First 1
            if ($match) { $match.Members += $s; $match.R = ($match.Members | Measure-Object R -Average).Average
                          $match.G = ($match.Members | Measure-Object G -Average).Average; $match.B = ($match.Members | Measure-Object B -Average).Average }
            else { $clusters += [pscustomobject]@{ R = $s.R; G = $s.G; B = $s.B; Members = @($s) } }
        }

        $art = New-Object System.Drawing.Bitmap $cols, $rows, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        foreach ($cl in $clusters) {
            $color = [System.Drawing.Color]::FromArgb(255, [int]$cl.R, [int]$cl.G, [int]$cl.B)
            foreach ($m in $cl.Members) { $art.SetPixel($m.Col, $m.Row, $color) }
        }
        $art.Save($pixelsPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $art.Dispose()
        "icon-pixels.png: $($ink.Count) pixels, $($clusters.Count) colours"
    }
    finally { $jpg.Dispose() }
}

# ───────────── 2. Rendering at every size ─────────────
$art = [System.Drawing.Bitmap]::FromFile($pixelsPath)
$canvas = 32
$ox = [int](($canvas - $art.Width) / 2); $oy = [int][math]::Floor(($canvas - $art.Height) / 2)

# The source draws the harpoon as an outline filled with the white page background. Making all white
# transparent would hollow it out (invisible on a dark taskbar), so empty cells enclosed by the outline
# are painted white: only the cells reachable from the outside become transparent.
function Get-Outside {
    $w = $art.Width + 2; $h = $art.Height + 2           # one empty cell of padding all around
    $outside = New-Object 'bool[,]' $h, $w
    $stack = New-Object System.Collections.Generic.Stack[int[]]
    $stack.Push(@(0, 0)); $outside[0, 0] = $true
    while ($stack.Count -gt 0) {
        $cell = $stack.Pop()
        foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) {
            $ny = $cell[0] + $d[0]; $nx = $cell[1] + $d[1]
            if ($ny -lt 0 -or $nx -lt 0 -or $ny -ge $h -or $nx -ge $w -or $outside[$ny, $nx]) { continue }
            $inArt = $ny -ge 1 -and $nx -ge 1 -and $ny -le $art.Height -and $nx -le $art.Width
            if ($inArt -and $art.GetPixel($nx - 1, $ny - 1).A -ne 0) { continue }
            $outside[$ny, $nx] = $true
            $stack.Push(@($ny, $nx))
        }
    }
    , $outside
}
$outside = Get-Outside

# Outline colour = the darkest colour of the artwork (the navy).
$outline = $null
for ($y = 0; $y -lt $art.Height; $y++) { for ($x = 0; $x -lt $art.Width; $x++) {
    $p = $art.GetPixel($x, $y)
    if ($p.A -ne 0 -and ($null -eq $outline -or $p.GetBrightness() -lt $outline.GetBrightness())) { $outline = $p } } }

# HSL lightness inversion: keeps the hue and saturation, turns light into dark and vice versa.
function Invert-Lightness([System.Drawing.Color]$c) {
    $h = $c.GetHue() / 360; $s = $c.GetSaturation(); $l = 1 - $c.GetBrightness()
    if ($s -eq 0) { $v = [int][math]::Round($l * 255); return [System.Drawing.Color]::FromArgb($c.A, $v, $v, $v) }
    $q = if ($l -lt 0.5) { $l * (1 + $s) } else { $l + $s - $l * $s }
    $pp = 2 * $l - $q
    $channel = {
        param($t)
        if ($t -lt 0) { $t += 1 }; if ($t -gt 1) { $t -= 1 }
        $v = if ($t -lt 1 / 6) { $pp + ($q - $pp) * 6 * $t } elseif ($t -lt 0.5) { $q } elseif ($t -lt 2 / 3) { $pp + ($q - $pp) * (2 / 3 - $t) * 6 } else { $pp }
        [int][math]::Round($v * 255)
    }
    [System.Drawing.Color]::FromArgb($c.A, (& $channel ($h + 1 / 3)), (& $channel $h), (& $channel ($h - 1 / 3)))
}

# Dark-mode colours: the navy outline and the white interior swap places (white harpoon),
# every other colour (blue accents, highlights) gets its lightness inverted.
function Get-DarkColor([System.Drawing.Color]$c) {
    if ($c.ToArgb() -eq $outline.ToArgb()) { return [System.Drawing.Color]::White }
    if ($c.ToArgb() -eq [System.Drawing.Color]::White.ToArgb()) { return $outline }
    Invert-Lightness $c
}

function New-Canvas([bool]$paused, [bool]$dark) {
    $bmp = New-Object System.Drawing.Bitmap $canvas, $canvas, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $art.Height; $y++) { for ($x = 0; $x -lt $art.Width; $x++) {
        $p = $art.GetPixel($x, $y)
        if ($p.A -eq 0) {
            if ($outside[($y + 1), ($x + 1)]) { continue }
            $p = [System.Drawing.Color]::White                # enclosed: part of the harpoon
        }
        if ($dark) { $p = Get-DarkColor $p }
        if ($paused) {
            $g = [int](0.3 * $p.R + 0.59 * $p.G + 0.11 * $p.B)
            $g = [int](80 + $g * 0.6) # lift the greys so the paused icon reads as "inactive", not as "black"
            $p = [System.Drawing.Color]::FromArgb(150, $g, $g, $g)
        }
        $bmp.SetPixel($x + $ox, $y + $oy, $p) } }
    $bmp
}

function Resize-Nearest($src, [int]$size) {
    $dst = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $f = $size / $src.Width
    for ($y = 0; $y -lt $size; $y++) { for ($x = 0; $x -lt $size; $x++) {
        $dst.SetPixel($x, $y, $src.GetPixel([int][math]::Floor($x / $f), [int][math]::Floor($y / $f))) } }
    $dst
}

function Resize-Smooth($src, [int]$size) {
    $dst = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $dst
}

function Resize-Ink($src, [int]$size) {
    $dst = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $f = $src.Width / $size
    for ($y = 0; $y -lt $size; $y++) { for ($x = 0; $x -lt $size; $x++) {
        $best = $null; $bestLum = 999
        for ($sy = [int][math]::Floor($y * $f); $sy -lt [math]::Min($src.Height, [int][math]::Ceiling(($y + 1) * $f)); $sy++) {
            for ($sx = [int][math]::Floor($x * $f); $sx -lt [math]::Min($src.Width, [int][math]::Ceiling(($x + 1) * $f)); $sx++) {
                $p = $src.GetPixel($sx, $sy)
                if ($p.A -eq 0) { continue }
                $lum = 0.3 * $p.R + 0.59 * $p.G + 0.11 * $p.B
                if ($lum -lt $bestLum) { $bestLum = $lum; $best = $p } } }
        if ($best) { $dst.SetPixel($x, $y, $best) } } }
    $dst
}

function Get-Frames([bool]$paused, [bool]$dark) {
    $base = New-Canvas $paused $dark
    $big = Resize-Nearest $base 256
    $frames = [System.Collections.Generic.SortedDictionary[int, object]]::new()
    foreach ($s in 16, 20, 24) { $frames[$s] = if ($Small -eq 'ink') { Resize-Ink $base $s } else { Resize-Smooth $big $s } }
    $frames[32] = $base
    foreach ($s in 40, 48) { $frames[$s] = Resize-Smooth $big $s }
    foreach ($s in 64, 128) { $frames[$s] = Resize-Nearest $base $s }
    $frames[256] = $big
    $frames
}

# ───────────── 3. ICO writer ─────────────
function Get-DibBytes($bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $maskStride = [int]([math]::Ceiling($w / 32) * 4)
    # BITMAPINFOHEADER (height is doubled: colour bitmap + AND mask)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]($w * $h * 4 + $maskStride * $h)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $w; $x++) {
        $p = $bmp.GetPixel($x, $y); $bw.Write([byte]$p.B); $bw.Write([byte]$p.G); $bw.Write([byte]$p.R); $bw.Write([byte]$p.A) } }
    for ($y = $h - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $w; $x++) { if ($bmp.GetPixel($x, $y).A -eq 0) { $row[[int][math]::Floor($x / 8)] = $row[[int][math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) } }
        $bw.Write($row) }
    $bw.Flush(); $ms.ToArray()
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $ms.ToArray()
}

function Write-Ico($frames, [string]$path) {
    $images = @(); foreach ($k in $frames.Keys) { $images += , @($k, $(if ($k -ge 128) { Get-PngBytes $frames[$k] } else { Get-DibBytes $frames[$k] })) }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$images.Count)          # ICONDIR
    $offset = 6 + 16 * $images.Count
    foreach ($img in $images) {                                                          # ICONDIRENTRY
        $s = $img[0]; $data = $img[1]
        $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$data.Length); $bw.Write([int]$offset)
        $offset += $data.Length }
    foreach ($img in $images) { $bw.Write([byte[]]$img[1]) }
    $bw.Dispose()
}

$active = Get-Frames $false $false
Write-Ico $active (Join-Path $here 'GetOverHere.ico')
Write-Ico (Get-Frames $true $false) (Join-Path $here 'GetOverHere-paused.ico')
$activeDark = Get-Frames $false $true
Write-Ico $activeDark (Join-Path $here 'GetOverHere-dark.ico')
Write-Ico (Get-Frames $true $true) (Join-Path $here 'GetOverHere-dark-paused.ico')
$active[256].Save((Join-Path $here 'icon-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$activeDark[256].Save((Join-Path $here 'icon-256-dark.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$art.Dispose()
"Icons written: sizes $($active.Keys -join ', ')"
