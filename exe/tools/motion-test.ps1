# 定点运动检测：只采代码雨区域，连续对比相邻帧的像素差异
# 代码雨是持续下落的，若画面正常，相邻帧必然有大量像素变化
# 用法: pwsh -File motion-test.ps1 [-Exe <path>] [-Seconds 20]
# 默认使用本脚本上一级目录里的 JiaHaoBreach.exe，因此整个项目可以随意挪动
param(
    [string]$Exe = (Join-Path (Split-Path $PSScriptRoot -Parent) "JiaHaoBreach.exe"),
    [int]$Seconds = 20,
    [int]$WarmupSec = 6
)

if (-not (Test-Path $Exe)) {
    Write-Host "[X] 找不到 exe: $Exe" -ForegroundColor Red
    Write-Host "    请先运行 build.bat 编译，或用 -Exe 指定路径。"
    exit 1
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$scr = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
# 取左侧代码雨竖条（终端面板之外，必然有雨）
$rx = 20
$ry = [int]($scr.Height * 0.10)
$rw = 150
$rh = [int]($scr.Height * 0.70)

$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WarmupSec

$bA = New-Object System.Drawing.Bitmap($rw, $rh)
$bB = New-Object System.Drawing.Bitmap($rw, $rh)
$gA = [System.Drawing.Graphics]::FromImage($bA)
$gB = [System.Drawing.Graphics]::FromImage($bB)
$size = New-Object System.Drawing.Size($rw, $rh)

# 采样点
$pts = @()
for ($i = 0; $i -lt $rw; $i += 6) { for ($j = 0; $j -lt $rh; $j += 6) { $pts += , @($i, $j) } }
$total = $pts.Count

$gA.CopyFromScreen($rx, $ry, 0, 0, $size)

$diffs = @()
$blackFrames = 0
$sw = [System.Diagnostics.Stopwatch]::StartNew()

while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    Start-Sleep -Milliseconds 70
    $gB.CopyFromScreen($rx, $ry, 0, 0, $size)

    $changed = 0
    $sum = 0.0
    foreach ($pt in $pts) {
        $ca = $bA.GetPixel($pt[0], $pt[1])
        $cb = $bB.GetPixel($pt[0], $pt[1])
        $va = ($ca.R + $ca.G + $ca.B) / 3.0
        $vb = ($cb.R + $cb.G + $cb.B) / 3.0
        if ([math]::Abs($va - $vb) -gt 10) { $changed++ }
        $sum += $vb
    }
    $avg = $sum / $total
    if ($avg -lt 1.5) { $blackFrames++ }
    $diffs += $changed

    $tb = $bA; $bA = $bB; $bB = $tb
    $tg = $gA; $gA = $gB; $gB = $tg
}

$gA.Dispose(); $gB.Dispose(); $bA.Dispose(); $bB.Dispose()
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue

$frames = $diffs.Count
$moving = ($diffs | Where-Object { $_ -gt ($total * 0.01) }).Count
$avgDiff = ($diffs | Measure-Object -Average).Average

Write-Host ""
Write-Host "=== 代码雨运动检测 ==="
Write-Host ("  采样帧数        : {0}" -f $frames)
Write-Host ("  全黑帧          : {0}" -f $blackFrames)
Write-Host ("  有运动的帧      : {0} / {1}  ({2:N0}%)" -f $moving, $frames, (100.0 * $moving / $frames))
Write-Host ("  平均变化像素    : {0:N1} / {1}  ({2:N1}%)" -f $avgDiff, $total, (100.0 * $avgDiff / $total))
Write-Host ""

if ($blackFrames -eq 0 -and $moving -gt ($frames * 0.9)) {
    Write-Host "[PASS] 无黑屏 + 代码雨持续运动"
    exit 0
} elseif ($blackFrames -gt 0) {
    Write-Host "[FAIL] 检测到 $blackFrames 帧全黑"
    exit 1
} else {
    Write-Host "[WARN] 运动帧偏少: $moving/$frames"
    exit 2
}
