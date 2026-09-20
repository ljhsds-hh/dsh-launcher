<#
    DshLauncher logo 生成器 —— 重新生成 Assets\app.ico 与 Assets\logo.png。

    用法（本机 pwsh 不在 PATH、且执行策略禁止跑 .ps1，所以这样调；
      cwd 放在仓库根或 Code 目录下都行，脚本会自己往上找资源目录）：
        Invoke-Expression (Get-Content -Raw -Encoding UTF8 'Tools\generate-logo.ps1')

    设计：蓝色渐变"应用砖" + 三段向上推进的白色胶囊（清理 / 构建 / 启动）+ 左上柔光；
          ≤32px 换成同构图的实心阶梯剪影（16px 上三条细胶囊会被抗锯齿糊成一团）。
          输出 9 帧 ICO：16/20/24/32/40/48 用 BMP(DIB)，64/128/256 用 PNG。
          脚本末尾会逐像素自检（梯度方向、胶囊与缝隙、阶梯档位、ICO 容器完整性）。

    改配色/比例：改 New-Drawing 里的渐变停靠点与 $heights / $alphas / $barW / $gap 即可。
    注意几何全部用 0..1 比例 ×$s 换算 —— 别把"像素值"和"比例值"混着乘
    （踩过一次：整块标记被算到画布外，渲染出来一个白板，什么报错都没有）。
#>
param(
    # 输出目录；不给就自己找（见下）
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

if (-not $OutDir) {
    # 注意：用 Invoke-Expression 跑本脚本时 $PSScriptRoot 是空的（没有脚本文件作用域），
    # 所以不能只依赖它 —— 从当前目录逐级往上找项目根。
    # 本仓库（dsh-launcher 独立仓库）资源在 Code\src\DshLauncher\Assets；
    # 迁库前作为 work_use 的子目录时是 DshLauncher\Code\src\DshLauncher\Assets —— 两个布局都认。
    $relatives = @(
        'Code\src\DshLauncher\Assets',
        'DshLauncher\Code\src\DshLauncher\Assets'
    )
    $probe = Get-Location
    $found = $null
    while ($probe) {
        foreach ($rel in $relatives) {
            $candidate = Join-Path $probe.Path $rel
            if (Test-Path $candidate) { $found = $candidate; break }
        }
        if ($found) { break }
        $parent = Split-Path $probe.Path -Parent
        if ($parent -eq $probe.Path) { break }
        $probe = Get-Item $parent
    }
    if (-not $found) {
        throw '找不到 Assets 目录（Code\src\DshLauncher\Assets）—— 请在仓库里运行本脚本，或用 -OutDir 指定'
    }
    $OutDir = $found
}
Write-Output ("输出目录：{0}" -f $OutDir)

# ══════════════════════════════════════════════════════════════════════
# DshLauncher logo 生成器
#   概念：蓝色渐变"应用砖" + 三段向上推进的白色胶囊（清理 / 构建 / 启动）
#         + 左上柔光。小尺寸（≤32）换成同构图的实心阶梯剪影 ——
#         16px 上"三条细胶囊 + 两道细缝"会被抗锯齿糊成一团。
#   几何全部按 0..1 比例定义，按目标像素换算，不依赖缩放变换。
# ══════════════════════════════════════════════════════════════════════

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function C([int]$r, [int]$g, [int]$b, [double]$a) {
    return [System.Windows.Media.Color]::FromArgb([int][Math]::Round($a * 255), $r, $g, $b)
}

function New-Drawing([double]$s, [bool]$detail) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()

    # ── 底板 ──
    $tileRect = New-Object System.Windows.Rect(($s * 0.03125), ($s * 0.03125), ($s * 0.9375), ($s * 0.9375))
    $tileGeo = New-Object System.Windows.Media.RectangleGeometry($tileRect, ($s * 0.21875), ($s * 0.21875))

    $g = New-Object System.Windows.Media.LinearGradientBrush
    $g.StartPoint = New-Object System.Windows.Point(0, 0)
    $g.EndPoint = New-Object System.Windows.Point(1, 1)
    $g.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C 0x6C 0x86 0xFF 1), 0.0)))
    $g.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C 0x4D 0x6B 0xFE 1), 0.5)))
    $g.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C 0x2A 0x3F 0xB8 1), 1.0)))
    $dc.DrawGeometry($g, $null, $tileGeo)

    # ── 左上柔光 ──
    if ($detail) {
        $dc.PushClip($tileGeo)
        $rg = New-Object System.Windows.Media.RadialGradientBrush
        $rg.Center = New-Object System.Windows.Point(0.24, 0.18)
        $rg.GradientOrigin = New-Object System.Windows.Point(0.24, 0.18)
        $rg.RadiusX = 0.8
        $rg.RadiusY = 0.8
        $rg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C 255 255 255 0.20), 0.0)))
        $rg.GradientStops.Add((New-Object System.Windows.Media.GradientStop((C 255 255 255 0.0), 1.0)))
        $dc.DrawEllipse($rg, $null, (New-Object System.Windows.Point(($s * 0.5), ($s * 0.5))), ($s * 0.5), ($s * 0.5))
        $dc.Pop()
    }

    # ── 三段推进（用 RectangleGeometry，和底板同一套调用方式）──
    $baseline = 0.7578
    $heights = @(0.21875, 0.35156, 0.50781)   # 56 / 90 / 130
    $alphas = @(0.72, 0.86, 1.0)
    if ($detail) { $barW = 0.15625; $gap = 0.09375; $radius = 0.07812 }
    else { $barW = 0.21875; $gap = 0.0; $radius = 0.0234 }

    $total = $barW * 3 + $gap * 2
    $x = 0.5 - $total / 2
    for ($i = 0; $i -lt 3; $i++) {
        $h = $heights[$i]
        $rect = New-Object System.Windows.Rect(($x * $s), (($baseline - $h) * $s), ($barW * $s), ($h * $s))
        $geo = New-Object System.Windows.Media.RectangleGeometry($rect, ($radius * $s), ($radius * $s))
        $brush = New-Object System.Windows.Media.SolidColorBrush((C 255 255 255 $alphas[$i]))
        $dc.DrawGeometry($brush, $null, $geo)
        $x += $barW + $gap
    }

    $dc.Close()
    return $dv
}

function Render([int]$n, [bool]$detail) {
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($n, $n, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32))
    $rtb.Render((New-Drawing ([double]$n) $detail))
    $stride = $n * 4
    $buf = New-Object 'byte[]' ($stride * $n)
    $rtb.CopyPixels($buf, $stride, 0)
    return [pscustomobject]@{ N = $n; Stride = $stride; Px = $buf; Rtb = $rtb }
}
function At($f, [int]$x, [int]$y) {
    $o = $y * $f.Stride + $x * 4
    return [pscustomobject]@{ B = $f.Px[$o]; G = $f.Px[$o + 1]; R = $f.Px[$o + 2]; A = $f.Px[$o + 3] }
}
# "亮" = 明显比渐变底亮（胶囊是白/浅蓝）
function IsLight($p) { return ($p.A -gt 200) -and ($p.R -ge 180) -and ($p.B -ge 245) }
function IsBlue($p) { return ($p.A -gt 200) -and ($p.B -gt $p.R) -and ($p.R -lt 170) }
function Hex($p) { return ('#{0:X2}{1:X2}{2:X2}' -f $p.R, $p.G, $p.B) }

# ══ 先诊断：DrawRoundedRectangle vs RectangleGeometry ═════════════════
Write-Output '════════ API 诊断（各画一个 40x56 圆角矩形，数非透明像素）════════'
foreach ($mode in 'DrawRoundedRectangle', 'RectangleGeometry') {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $r = New-Object System.Windows.Rect(12, 4, 40, 56)
    $b = New-Object System.Windows.Media.SolidColorBrush((C 255 255 255 1))
    if ($mode -eq 'DrawRoundedRectangle') { $dc.DrawRoundedRectangle($b, $null, $r, 20, 20) }
    else { $dc.DrawGeometry($b, $null, (New-Object System.Windows.Media.RectangleGeometry($r, 20, 20))) }
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(64, 64, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32))
    $rtb.Render($dv)
    $stride = 64 * 4
    $buf = New-Object 'byte[]' ($stride * 64)
    $rtb.CopyPixels($buf, $stride, 0)
    $ink = 0
    for ($i = 3; $i -lt $buf.Length; $i += 4) { if ($buf[$i] -gt 16) { $ink++ } }
    Write-Output ("  {0,-22} 非透明像素 = {1}" -f $mode, $ink)
}

# ══ 渲染 ══════════════════════════════════════════════════════════════
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @{}
foreach ($s in $sizes) { $frames[$s] = Render $s ($s -ge 40) }

Write-Output ''
Write-Output '════════ 逐尺寸渲染检查 ════════'
foreach ($s in $sizes) {
    $f = $frames[$s]
    $corner = At $f 0 0
    $center = At $f ([int]($s / 2)) ([int]($s / 2))
    $tl = At $f ([int]($s * 0.22)) ([int]($s * 0.2))
    $br = At $f ([int]($s * 0.85)) ([int]($s * 0.85))
    $light = 0; $blue = 0; $clear = 0
    for ($y = 0; $y -lt $s; $y++) { for ($x = 0; $x -lt $s; $x++) {
        $p = At $f $x $y
        if ($p.A -lt 16) { $clear++; continue }
        if (IsLight $p) { $light++ } elseif (IsBlue $p) { $blue++ }
    } }
    Write-Output ("  {0,3}px {1}  角α={2,3} 心α={3,3} 亮标记={4,4} 蓝底={5,4} 透明={6,3}  左上{7} 右下{8}" -f `
        $s, $(if ($s -ge 40) { 'detail' } else { 'silhou' }), $corner.A, $center.A, $light, $blue, $clear, (Hex $tl), (Hex $br))
}

Write-Output ''
Write-Output '════════ 结构探针（这个才有意义）════════'
$f = $frames[256]
# 三个胶囊中心 x（256 空间）：44+20=64 / 108+20=128 / 172+20=192；缝隙 x=94、158
$a1 = At $f 64 180; $a2 = At $f 128 180; $a3 = At $f 192 180
$g1 = At $f 94 180; $g2 = At $f 158 180
Write-Output ("  256 y=180（三根杆都在的高度）：杆1={0} 缝={1} 杆2={2} 缝={3} 杆3={4}" -f (Hex $a1), (Hex $g1), (Hex $a2), (Hex $g2), (Hex $a3))
Write-Output ("    三杆都比缝亮: {0}   亮度递增(杆1<杆2<杆3): {1}" -f `
    ((((IsLight $a1) -and (IsLight $a2) -and (IsLight $a3)) -and (IsBlue $g1) -and (IsBlue $g2))), `
    ((($a1.R + $a1.G) -lt ($a2.R + $a2.G)) -and (($a2.R + $a2.G) -lt ($a3.R + $a3.G))))
# 高度递增：y=80 时只有第三根亮（第一根顶在 138、第二根 108、第三根 64）
$h1 = At $f 64 80; $h2 = At $f 128 80; $h3 = At $f 192 80
Write-Output ("  256 y=80（只有最高的那根够得着）：杆1={0} 杆2={1} 杆3={2}  → 阶梯成立: {3}" -f `
    (Hex $h1), (Hex $h2), (Hex $h3), (((IsBlue $h1) -and (IsBlue $h2)) -and (IsLight $h3)))

$f16 = $frames[16]
# 16 空间：剪影三段 x≈2..5 / 6..9 / 10..13，底边 y≈12
$low1 = At $f16 3 14; $low2 = At $f16 8 14; $low3 = At $f16 12 14
$hi1 = At $f16 3 5; $hi3 = At $f16 12 5
Write-Output ("  16 y=14 三段: {0} | {1} | {2}   三段都亮: {3}" -f (Hex $low1), (Hex $low2), (Hex $low3), (((IsLight $low1) -and (IsLight $low2)) -and (IsLight $low3)))
Write-Output ("  16 y=5 : 左段={0} 右段={1}  → 只有右段亮（阶梯）: {2}" -f (Hex $hi1), (Hex $hi3), (((IsBlue $hi1)) -and (IsLight $hi3)))
$rowCounts = @()
for ($y = 0; $y -lt 16; $y++) {
    $c = 0
    for ($x = 0; $x -lt 16; $x++) { if (IsLight (At $f16 $x $y)) { $c++ } }
    $rowCounts += $c
}
Write-Output ("  16 逐行亮像素数（上→下）: {0}" -f ($rowCounts -join ','))
Write-Output ("    不同档位 {0} 档（三阶应为 3）" -f (@($rowCounts | Where-Object { $_ -gt 0 } | Sort-Object -Unique).Count))

# ══ 打包 ICO ══════════════════════════════════════════════════════════
function To-IcoBmp($f) {
    $s = $f.N
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($s * $s * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $p = At $f $x $y
            $r = 0; $g = 0; $b = 0
            if ($p.A -gt 0) {
                $r = [int][Math]::Min(255, [Math]::Round($p.R * 255.0 / $p.A))
                $g = [int][Math]::Min(255, [Math]::Round($p.G * 255.0 / $p.A))
                $b = [int][Math]::Min(255, [Math]::Round($p.B * 255.0 / $p.A))
            }
            $bw.Write([byte]$b); $bw.Write([byte]$g); $bw.Write([byte]$r); $bw.Write([byte]$p.A)
        }
    }
    $rowBytes = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write((New-Object 'byte[]' ($rowBytes * $s)), 0, ($rowBytes * $s))
    $bw.Flush()
    return $ms.ToArray()
}
function To-Png($f) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($f.Rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    return $ms.ToArray()
}

$payloads = @()
foreach ($s in $sizes) {
    if ($s -ge 64) { $payloads += [pscustomobject]@{ N = $s; Data = (To-Png $frames[$s]) } }
    else { $payloads += [pscustomobject]@{ N = $s; Data = (To-IcoBmp $frames[$s]) } }
}

$ms2 = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ms2)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$payloads.Count)
$off = 6 + 16 * $payloads.Count
foreach ($p in $payloads) {
    $dim = if ($p.N -ge 256) { 0 } else { $p.N }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$p.Data.Length); $w.Write([int]$off)
    $off += $p.Data.Length
}
foreach ($p in $payloads) { $w.Write($p.Data, 0, $p.Data.Length) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $ms2.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'logo.png'), (To-Png $frames[256]))

Write-Output ''
Write-Output '════════ ICO 容器回读 ════════'
$bytes = [System.IO.File]::ReadAllBytes((Join-Path $OutDir 'app.ico'))
$count = [BitConverter]::ToUInt16($bytes, 4)
Write-Output ("  type={0} 帧数={1} 总大小={2} 字节" -f [BitConverter]::ToUInt16($bytes, 2), $count, $bytes.Length)
$okAll = $true
for ($i = 0; $i -lt $count; $i++) {
    $e = 6 + $i * 16
    $wd = $bytes[$e]; $ht = $bytes[$e + 1]
    $sz = [BitConverter]::ToInt32($bytes, $e + 8)
    $off = [BitConverter]::ToInt32($bytes, $e + 12)
    $isPng = ($bytes[$off] -eq 0x89) -and ($bytes[$off + 1] -eq 0x50)
    if (($off + $sz) -gt $bytes.Length) { $okAll = $false }
    Write-Output ("    {0,3}x{1,-3} offset={2,7} size={3,7} {4}" -f $(if ($wd -eq 0) { 256 } else { $wd }), $(if ($ht -eq 0) { 256 } else { $ht }), $off, $sz, $(if ($isPng) { 'PNG' } else { 'BMP(DIB)' }))
}
Write-Output ("  所有帧数据都在文件范围内: {0}" -f $okAll)
Write-Output ("  logo.png = {0} 字节" -f (Get-Item (Join-Path $OutDir 'logo.png')).Length)
