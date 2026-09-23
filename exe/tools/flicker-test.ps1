# 闪屏检测：高频采样真实屏幕，统计"全黑帧"与"帧间变化"
# 用法: pwsh -File flicker-test.ps1 [-Exe <path>] [-Seconds 25]
# 默认使用本脚本上一级目录里的 JiahaoBreach.exe，因此整个项目可以随意挪动
param(
    [string]$Exe = (Join-Path (Split-Path $PSScriptRoot -Parent) "JiahaoBreach.exe"),
    [int]$Seconds = 25,
    [int]$WarmupSec = 5
)

if (-not (Test-Path $Exe)) {
    Write-Host "[X] 找不到 exe: $Exe" -ForegroundColor Red
    Write-Host "    请先运行 build.bat 编译，或用 -Exe 指定路径。"
    exit 1
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$scr = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$w = $scr.Width
$h = $scr.Height

$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WarmupSec   # 跳过引导屏

$bmpA = New-Object System.Drawing.Bitmap($w, $h)
$bmpB = New-Object System.Drawing.Bitmap($w, $h)
$gA = [System.Drawing.Graphics]::FromImage($bmpA)
$gB = [System.Drawing.Graphics]::FromImage($bmpB)
$size = New-Object System.Drawing.Size($w, $h)

# 采样网格：步长 32px，兼顾速度与覆盖面
$step = 32
$pts = @()
for ($i = 0; $i -lt $w; $i += $step) { for ($j = 0; $j -lt $h; $j += $step) { $pts += , @($i, $j) } }

function Measure-Frame {
    param($bmp)
    $sum = 0.0; $n = 0; $mx = 0.0
    foreach ($pt in $pts) {
        $c = $bmp.GetPixel($pt[0], $pt[1])
        $v = ($c.R + $c.G + $c.B) / 3.0
        $sum += $v; $n++
        if ($v -gt $mx) { $mx = $v }
    }
    # 返回哈希表，避免数组解构问题
    return @{ Avg = $sum / $n; Max = $mx }
}

$gA.CopyFromScreen(0, 0, 0, 0, $size)

$blackFrames = 0
$totalFrames = 0
$movingFrames = 0
$avgList = @()
$blackTimes = @()
$sw = [System.Diagnostics.Stopwatch]::StartNew()

while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    Start-Sleep -Milliseconds 60
    $gB.CopyFromScreen(0, 0, 0, 0, $size)

    $mA = Measure-Frame $bmpA
    $mB = Measure-Frame $bmpB

    $totalFrames++
    $avgList += [math]::Round($mB.Avg, 1)

    if ($mB.Avg -lt 1.5) {
        $blackFrames++
        $blackTimes += [math]::Round($sw.Elapsed.TotalSeconds, 1)
    }

    # 帧间变化：平均亮度差 + 峰值差，任一明显变化即视为画面在动
    if ([math]::Abs($mB.Avg - $mA.Avg) -gt 0.3 -or [math]::Abs($mB.Max - $mA.Max) -gt 6) {
        $movingFrames++
    }

    # 交换缓冲
    $tb = $bmpA; $bmpA = $bmpB; $bmpB = $tb
    $tg = $gA; $gA = $gB; $gB = $tg
}

$gA.Dispose(); $gB.Dispose(); $bmpA.Dispose(); $bmpB.Dispose()
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue

$mean = ($avgList | Measure-Object -Average).Average
$minB = ($avgList | Measure-Object -Minimum).Minimum
$maxB = ($avgList | Measure-Object -Maximum).Maximum

Write-Host ""
Write-Host "=== 闪屏检测结果 ==="
Write-Host ("  采样帧数     : {0}" -f $totalFrames)
Write-Host ("  全黑帧(<1.5) : {0}" -f $blackFrames)
Write-Host ("  画面变化帧   : {0} / {1}  ({2:N0}%)" -f $movingFrames, $totalFrames, (100.0 * $movingFrames / $totalFrames))
Write-Host ("  整屏亮度     : 均值={0:N1}  最低={1:N1}  最高={2:N1}" -f $mean, $minB, $maxB)
if ($blackTimes.Count -gt 0) {
    Write-Host ("  黑帧出现时刻 : {0}" -f (($blackTimes | Select-Object -First 25) -join ", "))
}
Write-Host ""

if ($blackFrames -eq 0 -and $movingFrames -gt ($totalFrames * 0.5)) {
    Write-Host "[PASS] 无黑屏闪烁，且画面持续变化"
    exit 0
} elseif ($blackFrames -gt 0) {
    Write-Host ("[FAIL] 检测到 {0} 帧全黑 —— 仍在闪烁" -f $blackFrames)
    exit 1
} else {
    Write-Host "[WARN] 未检测到黑帧，但画面变化偏少，需人工确认"
    exit 2
}
