# 嘉豪 · 黑客帝国装逼终端 —— 原生 exe 版

**双击 `JiaHaoBreach.exe`** 即可。全屏、置顶、无边框，音效自动播放。

- 纯 .NET Framework 4.x + Win32，**Windows 10/11 自带运行时，不用装任何东西**
- 不联网、不写注册表、不碰系统文件，就是个全屏动画窗口
- 自己绘制画面（GDI+），自己合成音频（内存 WAV），零外部素材
- 单文件 36 KB，无任何依赖

## 快捷键

| 按键 | 作用 |
|---|---|
| `空格` | 立刻灌一大批代码，滚动更疯 |
| `R` | 重播一遍 |
| `H` | 隐藏界面，只看代码雨 |
| `S` | 静音 / 恢复 |
| `ESC` | 退出 |
| `Alt + F4` | 退出 |

## 目录结构

```
exe/
├─ JiaHaoBreach.exe       编译产物，直接双击运行
├─ build.bat              一键重新编译（用 Windows 自带的 csc.exe）
├─ src/
│  └─ JiaHaoBreach.cs     全部源码，单文件
└─ tools/
   ├─ flicker-test.ps1    闪屏自检
   └─ motion-test.ps1     运动自检
```

## 重新编译

双击 `build.bat` 即可。它用的是 Windows 自带的 C# 编译器，**不需要 Visual Studio 或 .NET SDK**。

等价的手工命令：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /codepage:65001 /optimize+ ^
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  /out:JiaHaoBreach.exe src\JiaHaoBreach.cs
```

> `/codepage:65001` 必须加，否则源码里的中文会变乱码。

## 想改内容

编辑 `src\JiaHaoBreach.cs` 后重新编译：

| 想改什么 | 找哪里 |
|---|---|
| 名字 / 主机名 | `JiaHao`、`JiaHao-PC`、`JiaHao\administrator` |
| 六阶段台词 | `static readonly string[][] PHASES` |
| 滚动的假代码 | `string FakeLine()` |
| 结尾报告 | `static readonly string[] ENDING` |
| 引导屏 | `void BuildBoot()` |
| 开场音效 | `void InitAudio()` 里的 `Synth` 部分 |
| 滚动速度 | `if (rnd.NextDouble() < 0.55)` 那一行 |
| 雨滴密度 | `const int CW = 13, CH = 17;`（调小更密，但绘制量上升） |
| 拖尾长度 | `OnPaint` 里的 `Color.FromArgb(state == St.Boot ? 255 : 22, ...)`，数值越小拖尾越长 |
| 配色 | `ColOk` / `ColHi` / `ColTxt` 等静态色值 |

## 自检工具

改完源码重新编译后，跑这两个脚本验证画面是否正常（会短暂启动 exe 并采样真实屏幕像素）：

```powershell
# 闪屏检测：统计"全黑帧"，0 帧才算通过
pwsh -File tools\flicker-test.ps1 -Seconds 25

# 运动检测：确认代码雨真的在动
# 注意采样间隔需 >= 300ms，否则雨滴位移不足一格会误判为静止
pwsh -File tools\motion-test.ps1 -Seconds 18
```

两个脚本默认使用上一级目录的 `JiaHaoBreach.exe`，所以整个项目可以随意挪动。

## 诊断模式

程序内置了诊断模式，用来量化资源与性能（正常使用不需要）：

```powershell
.\JiaHaoBreach.exe --diag D:\diag.txt 60
```

每秒记录一行，读法：

| 字段 | 正常表现 | 异常含义 |
|---|---|---|
| `GDI` / `USER` | 全程恒定 | 持续上涨 = **句柄泄漏** |
| `托管` | 周期性回落 | 只涨不落 = **内存泄漏** |
| `帧均` | 前后一致 | 随行数上涨 = **绘制量过大**（不是泄漏） |

还有 `--shot <路径> <秒数>` 模式：跑到指定秒数就把自己渲染的位图存成 PNG 再退出，
用于在屏幕锁定等环境下检查画面（不依赖抓屏）。

---

## 开发记录：踩过的三个坑

留在这里，免得以后重蹈覆辙。

### 1. 全屏持续闪黑

`OnPaint` 用 `CreateGraphics()` 直接画窗口 DC，而窗体同时开了 `OptimizedDoubleBuffer`。
于是每次 `WM_PAINT`，系统都会把**空的后台缓冲**贴上来，盖掉刚画好的画面 —— 两者来回抢，屏幕就持续闪黑。

**正确写法**：整帧画进内存位图 → 一次性画到 `e.Graphics`（双缓冲后台），既无闪烁也无撕裂。

```csharp
SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
// ...
e.Graphics.DrawImageUnscaled(frame, 0, 0);   // 不要用 CreateGraphics()
```

> 这个 bug 特别阴险：当时的 `--shot` 自截图是直接存内存位图、**绕过了出图路径**，
> 所以截图张张完美、实际窗口却在闪。验证方法本身有盲区，等于没测到真正出问题的那条路。

### 2. 背景越来越卡

实测每帧从 12ms 涨到 **200ms**（只剩 5fps）。一开始怀疑资源泄漏，诊断数据直接推翻：
`GDI` 句柄全程恒定 24、托管内存周期性回落 —— **没有泄漏**，是绘制量问题。

根因：每帧要画约 **1728 次带透明通道的 `DrawImage`**（108 列 × 平均 15 个拖尾字符）。

三处优化，**200ms → 31ms（约 6.5 倍）**：

| 优化 | 做法 | 效果 |
|---|---|---|
| 位图格式 | `Format32bppArgb` → `Format32bppPArgb`（预乘 alpha） | 200 → 61 ms |
| 拖尾渲染 | 不再逐字符画拖尾，只画头部、让残影自然形成拖尾 | 61 → 30 ms |
| 静态层 | 暗角 + 扫描线预渲染成一张叠加层 | 稳定 ~31 ms |

> 拖尾那个改法其实是**回归经典矩阵雨的原始做法**，视觉上反而更自然
> （残影拉出连续渐隐的尾巴，比逐字符画的"虚线感"更好看）。

### 3. 启动器在 cmd 里解析错乱

`.bat` 用了 LF 行尾时，`cmd.exe` 在 `for` / `if` 括号块里会解析错乱，报一堆莫名的语法错误。

**规矩**：Windows 批处理一律 **CRLF 行尾 + 纯 ASCII**，且尽量用 `goto` 代替括号块。

---

## 注意

- 纯本地动画，不联网、不写注册表、不碰系统文件。
- 若被杀软误报（无签名的小 exe 常见），加信任即可，或改用 `../web/` 版本。
