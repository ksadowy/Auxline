<#
    Renders the Auxline logo ("wave to line") into the logo SVGs/PNGs and the .ico files, with transparent
    backgrounds. Re-run after changing the shapes below. From the repository root:

        powershell -ExecutionPolicy Bypass -File tools/build-icons.ps1

    Outputs:
        assets/logo/auxline-mark.svg, auxline-icon.svg    vector sources
        assets/logo/auxline-mark-512.png                  logo for the README / web
        assets/logo/auxline-icon-256.png
        Auxline/Assets/Auxline.ico                        app icon (16-256 px, blue gradient)
        Auxline/Assets/TrayLight.ico                      tray icon for dark taskbars (white)
        Auxline/Assets/TrayDark.ico                       tray icon for light taskbars (near black)
#>
param(
    [string]$LogoDirectory = 'assets/logo',
    [string]$IconDirectory = 'Auxline/Assets'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

# Shapes on a 64×64 grid, round caps and joins. The mark: a big swell, a smaller one, then the flat line.
# Humps are cubic curves with their control points a fifth in from each end: round tops without turning into loops.
$MarkPath = 'M6 32 C8.4 17.33 15.6 17.33 18 32 C20.4 46.67 27.6 46.67 30 32 C31.6 25.33 36.4 25.33 38 32 C39.6 38.67 44.4 38.67 46 32 L58 32'
$MarkStroke = 6
# Icon cut of the same shape: 1.5x taller wave and a thicker stroke, so it fills a square and stays readable at 16 px.
$IconPath = 'M6 32 C8.4 10 15.6 10 18 32 C20.4 54 27.6 54 30 32 C31.6 22 36.4 22 38 32 C39.6 42 44.4 42 46 32 L58 32'
$IconStroke = 7

$GradientStart = '#2F6FE0'
$GradientEnd = '#8FC1FF'
$TrayLightColor = '#FFFFFF'
$TrayDarkColor = '#1A1A1A'

function New-Brush([string]$color) {
    if ($color -ne 'gradient') { return [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString($color)) }
    # Bottom-left to top-right, like the SVG's objectBoundingBox gradient.
    $start = [System.Windows.Media.ColorConverter]::ConvertFromString($GradientStart)
    $end = [System.Windows.Media.ColorConverter]::ConvertFromString($GradientEnd)
    return [System.Windows.Media.LinearGradientBrush]::new($start, $end, [System.Windows.Point]::new(0, 1), [System.Windows.Point]::new(1, 0))
}

# Renders the shape into a square bitmap (straight, not premultiplied, alpha).
function Render-Shape([string]$data, [double]$stroke, [string]$color, [int]$size) {
    $pen = [System.Windows.Media.Pen]::new((New-Brush $color), $stroke)
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([System.Windows.Media.ScaleTransform]::new($size / 64, $size / 64))
    $context.DrawGeometry($null, $pen, [System.Windows.Media.Geometry]::Parse($data))
    $context.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    return [System.Windows.Media.Imaging.FormatConvertedBitmap]::new($bitmap, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
}

function Get-PngBytes($bitmap) {
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    $encoder.Save($stream)
    return $stream.ToArray()
}

# Classic icon image: BITMAPINFOHEADER, bottom-up 32-bit BGRA pixels, then an all-zero AND mask (alpha decides).
function Get-DibBytes($bitmap) {
    $size = $bitmap.PixelWidth
    $pixels = [byte[]]::new($size * $size * 4)
    $bitmap.CopyPixels($pixels, $size * 4, 0)
    $maskRowBytes = [int]([math]::Ceiling($size / 32) * 4)
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([int]40); $writer.Write([int]$size); $writer.Write([int]($size * 2))
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]0)
    $writer.Write([int]($pixels.Length + $maskRowBytes * $size))
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)
    for ($row = $size - 1; $row -ge 0; $row--) { $writer.Write($pixels, $row * $size * 4, $size * 4) }
    $writer.Write([byte[]]::new($maskRowBytes * $size))
    $writer.Flush()
    return $stream.ToArray()
}

# 256 px as PNG (as Windows expects), smaller sizes as classic bitmaps for every icon loader.
function Write-Icon([string]$path, [string]$data, [double]$stroke, [string]$color, [int[]]$sizes) {
    $images = foreach ($size in $sizes) {
        $bitmap = Render-Shape $data $stroke $color $size
        , @($size, $(if ($size -ge 256) { Get-PngBytes $bitmap } else { Get-DibBytes $bitmap }))
    }
    $stream = [System.IO.File]::Create($path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    $writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image[0] -ge 256) { 0 } else { $image[0] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]$image[1].Length); $writer.Write([int]$offset)
        $offset += $image[1].Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image[1]) }
    $writer.Dispose()
}

function Write-Svg([string]$path, [string]$data, [double]$stroke) {
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <defs>
    <linearGradient id="wave" x1="0" y1="1" x2="1" y2="0">
      <stop offset="0" stop-color="$GradientStart"/>
      <stop offset="1" stop-color="$GradientEnd"/>
    </linearGradient>
  </defs>
  <path d="$data" fill="none" stroke="url(#wave)" stroke-width="$stroke" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
"@
    [System.IO.File]::WriteAllText($path, $svg.Replace("`r`n", "`n") + "`n")
}

$root = (Get-Location).Path
$logoDirectory = Join-Path $root $LogoDirectory
$iconDirectory = Join-Path $root $IconDirectory
New-Item -ItemType Directory -Force $logoDirectory, $iconDirectory | Out-Null

Write-Svg (Join-Path $logoDirectory 'auxline-mark.svg') $MarkPath $MarkStroke
Write-Svg (Join-Path $logoDirectory 'auxline-icon.svg') $IconPath $IconStroke
[System.IO.File]::WriteAllBytes((Join-Path $logoDirectory 'auxline-mark-512.png'), (Get-PngBytes (Render-Shape $MarkPath $MarkStroke 'gradient' 512)))
[System.IO.File]::WriteAllBytes((Join-Path $logoDirectory 'auxline-icon-256.png'), (Get-PngBytes (Render-Shape $IconPath $IconStroke 'gradient' 256)))

# Tray sizes cover 100-300 % display scaling of the 16 px notification-area icon.
Write-Icon (Join-Path $iconDirectory 'Auxline.ico') $IconPath $IconStroke 'gradient' @(16, 20, 24, 32, 40, 48, 64, 256)
Write-Icon (Join-Path $iconDirectory 'TrayLight.ico') $IconPath $IconStroke $TrayLightColor @(16, 20, 24, 32, 40, 48)
Write-Icon (Join-Path $iconDirectory 'TrayDark.ico') $IconPath $IconStroke $TrayDarkColor @(16, 20, 24, 32, 40, 48)

Get-ChildItem $logoDirectory, $iconDirectory | Select-Object FullName, Length
