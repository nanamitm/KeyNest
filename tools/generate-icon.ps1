# KeyNest のアプリアイコン（src/KeyNest.App/Assets/KeyNest.ico）を作る。
# 青いグラデーションの角丸の四角に白い鍵。小さいサイズでもつぶれないよう、サイズごとに直接描いて PNG 形式の ICO にまとめる。
#   pwsh -File tools/generate-icon.ps1
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\src\KeyNest.App\Assets\KeyNest.ico")
)

Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 背景: 角丸の四角（上が明るい青、下が濃い青）
    $s = [float]$size
    $inset = [Math]::Max(0.5, $s * 0.02)
    $background = New-RoundedRect $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ($s * 0.22)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $s),
        ([System.Drawing.Color]::FromArgb(0x4A, 0xA3, 0xF0)), ([System.Drawing.Color]::FromArgb(0x16, 0x56, 0xA8))
    $g.FillPath($gradient, $background)

    # 鍵: 100x100 の設計座標で縦向きに描き、中心で 45 度回転する
    $scale = $s / 100.0
    $g.TranslateTransform($s / 2, $s / 2)
    $g.RotateTransform(45)
    $g.ScaleTransform($scale, $scale)
    $g.TranslateTransform(-50, -50)

    # 小さいサイズでは線を太く、穴を大きめにして形が残るようにする
    $small = $size -le 24
    $headRadius = if ($small) { 21 } else { 19 }
    $holeRadius = if ($small) { 8 } else { 7 }
    $shaftHalf = if ($small) { 8 } else { 6.5 }

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillEllipse($white, 50 - $headRadius, 30 - $headRadius, $headRadius * 2, $headRadius * 2)          # 頭
    $g.FillRectangle($white, 50 - $shaftHalf, 40, $shaftHalf * 2, 48)                                     # 軸
    $g.FillRectangle($white, 50 + $shaftHalf - 1, 68, 13, $(if ($small) { 8 } else { 7 }))                # 歯1
    if (-not $small) { $g.FillRectangle($white, 50 + $shaftHalf - 1, 79, 9, 7) }                          # 歯2（小さいサイズでは省く）
    # 穴: 背景と同じグラデーションで塗る（回転していても背景の模様と一致させるため、変換を外して描く）
    $holeCenter = New-Object System.Drawing.PointF 50, 30
    $points = [System.Drawing.PointF[]]@($holeCenter)
    $g.Transform.TransformPoints($points)
    $g.ResetTransform()
    $r = $holeRadius * $scale
    $g.FillEllipse($gradient, $points[0].X - $r, $points[0].Y - $r, $r * 2, $r * 2)

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

# ICO: ヘッダー（6 バイト）＋ 各サイズの目録（16 バイトずつ）＋ PNG データ
$images = foreach ($size in $sizes) { , (New-IconPng $size) }
$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $output
$writer.Write([UInt16]0)                 # 予約
$writer.Write([UInt16]1)                 # 種類: アイコン
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }  # 256 は 0 と書く
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)               # パレット数
    $writer.Write([byte]0)               # 予約
    $writer.Write([UInt16]1)             # プレーン数
    $writer.Write([UInt16]32)            # ビット数
    $writer.Write([UInt32]$images[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()

$fullPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force (Split-Path $fullPath) | Out-Null
[System.IO.File]::WriteAllBytes($fullPath, $output.ToArray())
Write-Output "作成しました: $fullPath ($($sizes -join ', ') px)"
