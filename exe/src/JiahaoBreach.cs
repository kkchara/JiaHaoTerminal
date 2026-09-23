// ============================================================
//  佳豪 · 黑客帝国装逼终端  —  原生 Win32/.NET 版（无浏览器）
//  编译: csc.exe /target:winexe /out:JiahaoBreach.exe JiahaoBreach.cs
//  依赖: 仅 .NET Framework 4.x 自带程序集，Windows 10/11 开箱即用
// ============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Text;
using System.Windows.Forms;

namespace JiahaoBreach
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            BreachForm f = new BreachForm();
            // 自检模式：跑到指定秒数就把自己渲染的位图存成 PNG，然后退出
            if (args.Length >= 2 && args[0] == "--shot")
            {
                f.ShotPath = args[1];
                f.ShotAt = args.Length >= 3 ? double.Parse(args[2]) : 9.0;
            }
            // 诊断模式：每秒记录 GDI/USER 句柄、内存、每帧绘制耗时，用于定位性能退化
            if (args.Length >= 2 && args[0] == "--diag")
            {
                f.DiagPath = args[1];
                f.DiagSeconds = args.Length >= 3 ? double.Parse(args[2]) : 60.0;
            }
            Application.Run(f);
        }
    }

    // ============================================================
    //  音频合成：开场音效全部实时生成 PCM，只播一次
    // ============================================================
    class Synth
    {
        public const int Rate = 44100;
        float[] buf;
        Random rnd = new Random(20240607);

        public Synth(double seconds) { buf = new float[(int)(seconds * Rate) + Rate]; }

        int Idx(double t) { return (int)(t * Rate); }

        static double SoftClip(double x)
        {
            if (x > 3.0) return 1.0;
            if (x < -3.0) return -1.0;
            return x * (27.0 + x * x) / (27.0 + 9.0 * x * x);   // 三次软限幅
        }

        // type: 0=sin 1=square 2=saw 3=triangle
        public void Tone(double at, double dur, double f0, double f1, int type, double vol, double decay)
        {
            int i0 = Idx(at), n = (int)(dur * Rate);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                int k = i0 + i;
                if (k < 0 || k >= buf.Length) break;
                double u = (double)i / n;
                double f = f0 * Math.Pow(f1 / f0, u);
                phase += 2.0 * Math.PI * f / Rate;
                double w = phase / (2.0 * Math.PI);
                double s;
                switch (type)
                {
                    case 1: s = Math.Sin(phase) >= 0 ? 1 : -1; break;
                    case 2: s = 2.0 * (w - Math.Floor(w)) - 1.0; break;
                    case 3: s = 1.0 - 4.0 * Math.Abs((w - Math.Floor(w)) - 0.5); break;
                    default: s = Math.Sin(phase); break;
                }
                double atk = u < 0.04 ? u / 0.04 : 1.0;
                double env = atk * Math.Exp(-decay * u);
                buf[k] += (float)(s * env * vol);
            }
        }

        public void Boom(double at, double dur, double f0, double f1, double vol)
        {
            Tone(at, dur, f0, f1, 0, vol, 2.2);
        }

        // 噪声 + 低通扫频 = 经典 whoosh
        public void Whoosh(double at, double dur, double f0, double f1, double vol)
        {
            int i0 = Idx(at), n = (int)(dur * Rate);
            double lp = 0;
            for (int i = 0; i < n; i++)
            {
                int k = i0 + i;
                if (k < 0 || k >= buf.Length) break;
                double u = (double)i / n;
                double fc = f0 * Math.Pow(f1 / f0, u);
                double coef = 1.0 - Math.Exp(-2.0 * Math.PI * fc / Rate);
                if (coef > 1.0) coef = 1.0;
                double white = rnd.NextDouble() * 2.0 - 1.0;
                lp += (white - lp) * coef;
                double atk = u < 0.03 ? u / 0.03 : 1.0;
                buf[k] += (float)(lp * atk * Math.Exp(-2.0 * u) * vol);
            }
        }

        public byte[] ToWav(double gain)
        {
            int n = buf.Length;
            int dataLen = n * 2;
            byte[] outB = new byte[44 + dataLen];
            int p = 0;
            Action<string> str = delegate(string s) { foreach (char c in s) outB[p++] = (byte)c; };
            Action<int> i32 = delegate(int v) { outB[p++] = (byte)(v & 0xFF); outB[p++] = (byte)((v >> 8) & 0xFF); outB[p++] = (byte)((v >> 16) & 0xFF); outB[p++] = (byte)((v >> 24) & 0xFF); };
            Action<short> i16 = delegate(short v) { outB[p++] = (byte)(v & 0xFF); outB[p++] = (byte)((v >> 8) & 0xFF); };
            str("RIFF"); i32(36 + dataLen); str("WAVEfmt "); i32(16); i16(1); i16(1);
            i32(Rate); i32(Rate * 2); i16(2); i16(16); str("data"); i32(dataLen);
            for (int i = 0; i < n; i++)
            {
                double v = SoftClip(buf[i] * gain);
                short s = (short)(v * 31500.0);
                outB[p++] = (byte)(s & 0xFF);
                outB[p++] = (byte)((s >> 8) & 0xFF);
            }
            return outB;
        }
    }

    // ============================================================
    //  终端行
    // ============================================================
    class Line
    {
        public string Text = "";
        public Color Col = Color.White;
        public Font Fnt;
        public bool Logo;
        public bool Typing;
        public int Shown;
        public double Speed = 60;      // 字/秒
        public float Height { get { return Fnt.Height + 1; } }
    }

    // ============================================================
    //  主窗体
    // ============================================================
    class BreachForm : Form
    {
        // ---------- 画布 ----------
        Bitmap frame;
        Graphics fg;

        // ---------- 矩阵雨 ----------
        const int CW = 13, CH = 17;
        string GLYPHS = "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン0123456789ABCDEFXYZ<>/\\[]{}$#%&*+=-_";
        Bitmap atlasHead;
        int cols;
        float[] dropY, spd;
        int[] trail;
        Random rnd = new Random();

        // ---------- 状态机 ----------
        enum St { Boot, Flash, Run }
        St state = St.Boot;
        double tState, tScript, tTotal;
        System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        double lastT;

        // ---------- 内容 ----------
        List<Line> lines = new List<Line>();
        Queue<double[]> script = new Queue<double[]>();     // 占位，实际用 Step
        List<Step> steps = new List<Step>();
        int stepIdx;
        double tGranted = -1, tGlitch = -1, flashT = -1, shakeT = -1;
        string[] bootLines;
        int bootShown;
        double bootCharT;

        int pct, packets = 0, threads = 512;
        float bCpu, bMem, bNet;
        double hudT;
        bool infinite;
        double infiniteT;
        string banner = "ACCESS GRANTED";
        bool hideUI;
        bool soundOn = true;

        // 自检截图（--shot 模式用，正常运行时为 null）
        public string ShotPath;
        public double ShotAt = -1;
        bool shotDone;

        // 诊断模式（--diag）：记录资源与耗时趋势
        public string DiagPath;
        public double DiagSeconds = 60;
        double diagNext;
        System.Text.StringBuilder diagLog = new System.Text.StringBuilder();
        double paintMsSum; int paintMsCount; double paintMsMax;

        // ---------- 字体 ----------
        Font fMono, fMonoB, fBig, fBigBox, fTiny, fHud, fTitle;
        FontFamily monoFamily;

        // ---------- 音频 ----------
        SoundPlayer introPlayer;

        class Step { public double At; public Action Act; }

        public BreachForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            TopMost = true;
            BackColor = Color.Black;
            KeyPreview = true;
            // 标准无闪烁组合：整帧先在内存位图里画好，再一次性画进 e.Graphics（双缓冲后台）。
            // 曾经这里用 CreateGraphics() 直接画窗口 DC —— 那样系统双缓冲会把空的后台缓冲贴上来盖掉画面，导致持续闪黑。
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            Text = "JIAHAO // SYSTEM BREACH";

            monoFamily = PickFamily(new string[] { "Consolas", "Cascadia Mono", "Courier New" }, FontFamily.GenericMonospace);
            fMono = new Font(monoFamily, 12.5f, FontStyle.Regular);
            fMonoB = new Font(monoFamily, 12.5f, FontStyle.Bold);
            fTiny = new Font(monoFamily, 9.5f, FontStyle.Regular);
            fHud = new Font(monoFamily, 10.5f, FontStyle.Regular);
            fTitle = new Font(monoFamily, 10.5f, FontStyle.Regular);
            FontFamily bigFam = PickFamily(new string[] { "Arial Black", "Segoe UI", "Arial" }, FontFamily.GenericSansSerif);
            fBig = new Font(bigFam, 30f, FontStyle.Bold);
            fBigBox = new Font(bigFam, 30f, FontStyle.Bold);

            BuildAtlas();
            BuildBoot();
            BuildScript();

            Cursor.Hide();
            clock.Start();
            lastT = 0;
        }

        static FontFamily PickFamily(string[] names, FontFamily fallback)
        {
            foreach (string n in names)
            {
                try
                {
                    FontFamily f = new FontFamily(n);
                    return f;
                }
                catch { }
            }
            return fallback;
        }

        // ============================================================
        //  字形图集：把辉光烘焙进去，之后每帧只做贴图（GDI+ DrawString 太慢）
        // ============================================================
        Bitmap BuildStrip(Color core)
        {
            int n = GLYPHS.Length;
            Bitmap strip = new Bitmap(CW * n, CH, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(strip))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (Font f = new Font(monoFamily, 11.5f, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    for (int i = 0; i < n; i++)
                    {
                        string s = GLYPHS[i].ToString();
                        RectangleF r = new RectangleF(i * CW, 0, CW, CH);
                        // 烘焙外发光
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            for (int dy = -2; dy <= 2; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                double d = Math.Sqrt(dx * dx + dy * dy);
                                if (d > 2.3) continue;
                                int a = (int)(70.0 * (1.0 - d / 2.3));
                                if (a <= 0) continue;
                                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 0, 255, 136)))
                                    g.DrawString(s, f, b, new RectangleF(r.X + dx, r.Y + dy, r.Width, r.Height), sf);
                            }
                        }
                        using (SolidBrush b = new SolidBrush(core))
                            g.DrawString(s, f, b, r, sf);
                    }
                }
            }
            return strip;
        }

        void BuildAtlas()
        {
            atlasHead = BuildStrip(Color.FromArgb(255, 240, 255, 248));
        }

        // ============================================================
        //  内容
        // ============================================================
        void BuildBoot()
        {
            bootLines = new string[] {
                "JIAHAO-NET SECURE BOOT v9.9.9",
                "Copyright (c) 佳豪 网络空间作战部",
                "",
                "  [ OK ] 检测到操作者生物特征 ...... 佳豪",
                "  [ OK ] 加载内核模块 ghost.sys",
                "  [ OK ] 初始化 512 条加密隧道",
                "  [ OK ] 关闭审计日志 / 反取证就绪",
                "",
                "  >>> 正在突破目标系统，请稍候 ..."
            };
        }

        static readonly string[][] PHASES = new string[][] {
            new string[] { "[PHASE 01] 解析 JIAHAO 生物特征指纹 ...", "指纹哈希匹配度 99.97%", ">> 生物特征: 佳豪级 · 唯一" },
            new string[] { "[PHASE 02] 注入内核态提权模块 ...", "ring0 hook @ 0x7A3F91C2", ">> 提权完成: NT AUTHORITY\\SYSTEM" },
            new string[] { "[PHASE 03] 绕过硬编码防火墙规则 ...", "iptables -F && ufw disable", ">> 防火墙: 已臣服" },
            new string[] { "[PHASE 04] 建立 512 条加密隧道 ...", "AES-256-GCM · 隧道握手 OK", ">> 链路: 幽灵模式" },
            new string[] { "[PHASE 05] 克隆全盘数据镜像 ...", "镜像体积 742 GB", ">> 数据: 全部到手" },
            new string[] { "[PHASE 06] 抹除全部入侵痕迹 ...", "shred -n 7 -z /var/log/*", ">> 痕迹: 0 条残留" }
        };

        string Hex(int n) { string s = ""; for (int i = 0; i < n; i++) s += "0123456789ABCDEF"[rnd.Next(16)]; return s; }
        int Ri(int a, int b) { return rnd.Next(a, b + 1); }
        double Rd(double a, double b) { return a + rnd.NextDouble() * (b - a); }
        string Ip() { return Ri(10, 223) + "." + Ri(0, 255) + "." + Ri(0, 255) + "." + Ri(1, 254); }
        string Pad(int n, int w) { return n.ToString().PadLeft(w, '0'); }

        string FakeLine()
        {
            switch (rnd.Next(11))
            {
                case 0:
                    return "0x" + Hex(8) + "  " + Pick(new string[] { "mov", "push", "jmp", "call", "xor", "lea", "test", "cmp", "add", "sub" })
                        + "  " + Pick(new string[] { "eax", "ebx", "ecx", "edx", "esi", "edi", "r8", "r9" })
                        + ", " + Pick(new string[] { "0x" + Hex(4), "[" + Pick(new string[] { "ebp-4", "esp+8", "eax+12" }) + "]", Ri(0, 4095).ToString() });
                case 1:
                    return "[+] node " + Pad(Ri(1, 999), 3) + " -> " + Ip() + ":" + Pick(new string[] { "22", "80", "443", "3389", "8080" })
                        + "  RTT " + Rd(1, 90).ToString("0.0") + "ms  OK";
                case 2:
                    return "[*] decrypting block 0x" + Hex(6) + " ... " + Ri(80, 100) + "% " + Pick(new string[] { "OK", "OK", "OK", "RETRY" });
                case 3:
                    return "thread-" + Pad(Ri(1, 512), 3) + " :: " + Pick(new string[] { "cracking", "injecting", "sniffing", "exfiltrating", "spoofing" }) + " -> " + Ri(1, 100) + "%";
                case 4:
                    return "[WARN] intrusion detection triggered -> countermeasure: SILENCE";
                case 5:
                    return "[ OK ] 佳豪 权限等级提升至 " + Pick(new string[] { "ROOT", "ADMIN", "GOD", "SUPERUSER", "OMNIPOTENT" });
                case 6:
                    return "[ + ] 捕获数据包 " + Ri(100000, 999999) + " bytes from " + Ip();
                case 7:
                    return "[ !! ] 目标已无反抗能力 ... 继续碾压";
                case 8:
                    return "C:\\> " + Pick(new string[] { "dir /s /b", "net user", "tasklist", "whoami /priv", "reg query HKLM" }) + "  -> " + Ri(2, 900) + " 项";
                case 9:
                    return "[ ok ] 加密通道 " + Hex(4) + " 已建立 (AES-256-GCM / 幽灵模式)";
                default:
                    return ">>> 当前操作者: 佳豪 >>> 无人可挡 >>>";
            }
        }

        string Pick(string[] a) { return a[rnd.Next(a.Length)]; }
        Color PickC(Color[] a) { return a[rnd.Next(a.Length)]; }

        static readonly string[] ENDING = new string[] {
            "============================ 入侵报告 ============================",
            "  操作者 .......... 佳豪 (JIAHAO)  [权限: GOD]",
            "  目标主机 ........ JIAHAO-PC / 10.0.0.7",
            "  突破用时 ........ 4.21 秒",
            "  获取数据 ........ 742 GB",
            "  加密隧道 ........ 512 条 (全部存活)",
            "  痕迹残留 ........ 0",
            "  结论 ............ 系统已被彻底接管。",
            "==================================================================",
            "",
            "  >>> ACCESS GRANTED — 欢迎回来，佳豪。 <<<"
        };

        void BuildScript()
        {
            double t = 0;
            Action<double, Action> at = delegate(double d, Action a) { t += d; Step s = new Step(); s.At = t; s.Act = a; steps.Add(s); };

            at(0.00, delegate { Prompt("whoami"); });
            at(0.30, delegate { Out("jiahao\\administrator  (权限等级: GOD)", ColHi); });
            at(0.35, delegate { Prompt("net session"); });
            at(0.30, delegate { Out("访问被拒绝 —— 因为对方已经不配拒绝了。", ColWarn); });
            at(0.60, delegate { });

            for (int i = 0; i < PHASES.Length; i++)
            {
                int idx = i;
                at(0.10, delegate
                {
                    Prompt(Pick(new string[] { "ghost.exe --phase " + (idx + 1), "python breach.py -s " + (idx + 1), "ghost.exe --phase " + (idx + 1) }));
                });
                at(0.15, delegate { Type(PHASES[idx][0], ColHi); });
                at(1.10, delegate { Out(PHASES[idx][1], ColOk); });
                at(0.22, delegate { Out(PHASES[idx][2], ColOk); });
                at(0.45, delegate
                {
                    pct = (int)Math.Round((idx + 1) * 100.0 / PHASES.Length);
                    if (idx + 1 == PHASES.Length)
                    {
                        tGranted = tTotal;
                        tGlitch = tTotal;
                        shakeT = tTotal;
                        pct = 100;
                    }
                });
            }

            at(0.30, delegate { });
        }

        // ============================================================
        //  颜色
        // ============================================================
        static readonly Color ColOk = Color.FromArgb(57, 255, 158);
        static readonly Color ColHi = Color.FromArgb(216, 255, 233);
        static readonly Color ColWarn = Color.FromArgb(255, 209, 102);
        static readonly Color ColDim = Color.FromArgb(47, 122, 85);
        static readonly Color ColTxt = Color.FromArgb(125, 255, 176);
        static readonly Color ColBad = Color.FromArgb(255, 95, 87);

        // ============================================================
        //  终端操作
        // ============================================================
        const int MAXL = 200;

        void Push(Line l)
        {
            lines.Add(l);
            if (lines.Count > MAXL) lines.RemoveAt(0);
        }
        void Out(string s, Color c) { Line l = new Line(); l.Text = s; l.Col = c; l.Fnt = fMono; Push(l); }
        void Prompt(string cmd)
        {
            Line l = new Line();
            l.Text = "PS  C:\\JIAHAO\\breach> " + cmd;
            l.Col = ColHi; l.Fnt = fMonoB; Push(l);
        }
        void Type(string s, Color c)
        {
            Line l = new Line(); l.Text = s; l.Col = c; l.Fnt = fMono; l.Typing = true; l.Shown = 0; l.Speed = 42; Push(l);
        }
        void Logo()
        {
            Line l = new Line(); l.Logo = true; l.Fnt = fBig; Push(l);
        }

        // ============================================================
        //  音频
        // ============================================================
        void InitAudio()
        {
            try
            {
                // 开场音效：警报 x3 + 电流 whoosh + 重低音 + 上行琶音，全部混成一个 WAV，只播一次
                Synth s = new Synth(3.0);
                for (int i = 0; i < 3; i++)
                {
                    double b = i * 0.34;
                    s.Tone(b, 0.17, 1046, 1046, 1, 0.055, 3.0);
                    s.Tone(b + 0.17, 0.17, 784, 784, 1, 0.055, 3.0);
                }
                s.Whoosh(0.0, 0.55, 4200, 500, 0.55);
                s.Tone(0.0, 0.45, 1500, 90, 2, 0.16, 2.4);
                s.Boom(0.50, 1.6, 150, 28, 0.85);
                double[] scale = new double[] { 261.63, 329.63, 392.0, 523.25, 659.25, 783.99, 1046.5, 1318.5 };
                for (int i = 0; i < scale.Length; i++)
                    s.Tone(0.52 + i * 0.075, 0.30, scale[i], scale[i], 3, 0.20, 3.2);
                s.Tone(1.30, 0.80, 1568, 1568, 0, 0.16, 2.6);

                MemoryStream ms = new MemoryStream(s.ToWav(0.72));
                introPlayer = new SoundPlayer(ms);
                introPlayer.Load();
            }
            catch { introPlayer = null; }
        }

        void PlayIntro()
        {
            if (!soundOn || introPlayer == null) return;
            try { introPlayer.Play(); } catch { }
        }

        // ============================================================
        //  主循环
        // ============================================================
        static readonly string LogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jiahao_debug.log");
        public static void Log(string s)
        {
            try { System.IO.File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n"); } catch { }
        }
        Timer timer;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            InitAudio();
            timer = new Timer();
            timer.Interval = 30;
            timer.Tick += delegate { try { Tick(); } catch (Exception ex) { Log("TICK 异常: " + ex); } };
            timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (timer != null) timer.Stop();
            base.OnFormClosed(e);
        }

        double Now() { return clock.Elapsed.TotalSeconds; }

        void Tick()
        {
            double now = Now();
            double dt = now - lastT;
            lastT = now;
            if (dt > 0.25) dt = 0.25;
            tTotal += dt;
            tState += dt;

            // --- 引导屏逐字显示 ---
            if (state == St.Boot)
            {
                bootCharT += dt;
                if (bootCharT > 0.03)
                {
                    bootCharT = 0;
                    int total = 0;
                    foreach (string s in bootLines) total += s.Length + 1;
                    if (bootShown < total) bootShown += 3;
                }
                if (tState > 2.8)
                {
                    state = St.Flash; tState = 0; flashT = tTotal;
                    PlayIntro();                 // ← 全场唯一一次音效
                }
            }
            else if (state == St.Flash)
            {
                if (tState > 0.55) { state = St.Run; tState = 0; }
            }
            else
            {
                // 脚本推进
                tScript += dt;
                while (stepIdx < steps.Count && steps[stepIdx].At <= tScript)
                {
                    steps[stepIdx].Act();
                    stepIdx++;
                }

                // 打字机
                for (int i = 0; i < lines.Count; i++)
                {
                    Line l = lines[i];
                    if (l.Typing && l.Shown < l.Text.Length)
                    {
                        l.Shown += (int)Math.Max(1, l.Speed * dt);
                        if (l.Shown > l.Text.Length) l.Shown = l.Text.Length;
                    }
                }

                // 脚本放完 → 无限疯狂滚动
                if (!infinite && stepIdx >= steps.Count && tScript > steps[steps.Count - 1].At + 0.5)
                {
                    infinite = true;
                    infiniteT = tTotal;
                }
                if (infinite)
                {
                    infiniteT += 0;
                    if (tTotal - infiniteT > 0.05)
                    {
                        infiniteT = tTotal;
                        int n = Ri(4, 7);
                        for (int i = 0; i < n; i++)
                        {
                            Out(FakeLine(), PickC(new Color[] { ColTxt, ColOk, ColDim, ColWarn, ColHi }));
                            packets += Ri(1200, 9800);
                        }
                        if (rnd.NextDouble() < 0.004)
                        {
                            for (int i = 0; i < ENDING.Length; i++) Out(ENDING[i], ENDING[i].IndexOf("ACCESS GRANTED") > -1 ? ColHi : ColOk);
                        }
                    }
                    else
                    {
                        // 高速模式：每帧都灌，制造疯狂滚动的观感
                        if (rnd.NextDouble() < 0.55)
                        {
                            Out(FakeLine(), PickC(new Color[] { ColTxt, ColOk, ColDim, ColWarn, ColHi }));
                            packets += Ri(400, 3200);
                        }
                    }
                }
            }

            // --- HUD 数据 ---
            hudT += dt;
            if (hudT > 0.35)
            {
                hudT = 0;
                bCpu = (float)Rd(55, 99); bMem = (float)Rd(48, 92); bNet = (float)Rd(30, 100);
                if (rnd.NextDouble() < 0.25) threads = Ri(480, 1024);
                hudClock = DateTime.Now.ToString("HH:mm:ss");
            }

            Invalidate();

            // 自检截图：直接保存自己渲染的位图（不依赖屏幕是否解锁）
            if (!shotDone && ShotPath != null && tTotal >= ShotAt)
            {
                shotDone = true;
                try { frame.Save(ShotPath, ImageFormat.Png); } catch (Exception ex) { Log("截图失败: " + ex.Message); }
                BeginInvoke(new Action(Close));
            }

            // 诊断模式：每秒记录一次资源与耗时
            if (DiagPath != null && tTotal >= diagNext)
            {
                diagNext = tTotal + 1.0;
                int gdi = 0, user = 0;
                try { gdi = GetGuiResources(GetCurrentProcess(), 0); user = GetGuiResources(GetCurrentProcess(), 1); } catch { }
                double avgPaint = paintMsCount > 0 ? paintMsSum / paintMsCount : 0;
                diagLog.AppendLine(string.Format(
                    "{0,5:0.0}s  GDI={1,-6} USER={2,-6} 托管={3,7:N1}MB  帧均={4,6:N2}ms 帧峰={5,6:N2}ms  帧数={6,-5} 行数={7}",
                    tTotal, gdi, user, GC.GetTotalMemory(false) / 1048576.0, avgPaint, paintMsMax, paintMsCount, lines.Count));
                paintMsSum = 0; paintMsCount = 0; paintMsMax = 0;

                if (DiagSeconds > 0 && tTotal >= DiagSeconds)
                {
                    try { System.IO.File.WriteAllText(DiagPath, diagLog.ToString()); } catch { }
                    BeginInvoke(new Action(Close));
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        // ============================================================
        //  绘制
        // ============================================================
        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void EnsureFrame()
        {
            if (frame == null || frame.Width != ClientSize.Width || frame.Height != ClientSize.Height)
            {
                if (fg != null) fg.Dispose();
                if (frame != null) frame.Dispose();
                int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
                frame = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                fg = Graphics.FromImage(frame);
                fg.SmoothingMode = SmoothingMode.AntiAlias;
                fg.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                InitRain(w, h);
            }
        }

        void InitRain(int w, int h)
        {
            cols = w / CW + 1;
            dropY = new float[cols]; spd = new float[cols]; trail = new int[cols];
            for (int i = 0; i < cols; i++)
            {
                dropY[i] = (float)(-rnd.NextDouble() * (h / (double)CH));
                spd[i] = (float)Rd(0.22, 0.75);
                trail[i] = Ri(6, 24);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* 自己管背景 */ }

        protected override void OnPaint(PaintEventArgs e)
        {
            long swPaint = clock.ElapsedTicks;
            try
            {
            EnsureFrame();
            int W = frame.Width, H = frame.Height;
            Graphics g = fg;

            // ---------- 1. 矩阵雨（带残影） ----------
            using (SolidBrush fade = new SolidBrush(Color.FromArgb(state == St.Boot ? 255 : 22, 0, 0, 0)))
                g.FillRectangle(fade, 0, 0, W, H);

            if (state != St.Boot)
            {
                int gn = GLYPHS.Length;
                for (int i = 0; i < cols; i++)
                {
                    float x = i * CW;
                    float y = dropY[i] * CH;

                    // 只画"头部"一个字符：拖尾由每帧的半透明黑叠加自然形成（经典矩阵雨做法）。
                    // 以前这里还显式画 trail[i] 个拖尾字符，每帧上千次 DrawImage，是卡顿的主因。
                    if (y > -CH && y < H + CH)
                    {
                        int head = rnd.Next(gn);
                        g.DrawImage(atlasHead, new Rectangle((int)x, (int)y, CW, CH), head * CW, 0, CW, CH, GraphicsUnit.Pixel);
                    }

                    dropY[i] += spd[i] * (float)(1.0 + (tGranted > 0 && tTotal - tGranted < 1.2 ? 1.4 : 0));
                    if (y > H + 60)
                    {
                        dropY[i] = -Ri(2, 30);
                        spd[i] = (float)Rd(0.22, 0.75);
                        trail[i] = Ri(6, 24);
                    }
                }
            }

            // ---------- 2. 引导屏 ----------
            if (state == St.Boot)
            {
                float bx = W * 0.06f, by = H * 0.10f;
                int left = bootShown;
                for (int i = 0; i < bootLines.Length; i++)
                {
                    string s = bootLines[i];
                    if (left <= 0) break;
                    string show = s.Length <= left ? s : s.Substring(0, left);
                    left -= s.Length + 1;
                    using (SolidBrush b = new SolidBrush(s.StartsWith("  [ OK ]") ? ColOk : ColTxt))
                        g.DrawString(show, fMono, b, bx, by + i * (fMono.Height + 3));
                }
                e.Graphics.DrawImageUnscaled(frame, 0, 0);
                return;
            }

            // ---------- 3. 终端窗口 ----------
            if (!hideUI)
            {
                float pw = Math.Min(1180f, W * 0.94f);
                float ph = Math.Min(820f, H * 0.90f);
                float px = (W - pw) / 2f, py = (H - ph) / 2f;

                if (shakeT > 0 && tTotal - shakeT < 0.6)
                {
                    double k = 1.0 - (tTotal - shakeT) / 0.6;
                    px += (float)(Math.Sin(tTotal * 60) * 7 * k);
                    py += (float)(Math.Cos(tTotal * 71) * 5 * k);
                }

                RectangleF pr = new RectangleF(px, py, pw, ph);

                // 外发光
                for (int i = 9; i >= 1; i--)
                {
                    RectangleF rr = RectangleF.Inflate(pr, i * 1.6f, i * 1.6f);
                    using (GraphicsPath p = RoundRect(rr, 9f + i))
                    using (Pen pen = new Pen(Color.FromArgb(5, 0, 255, 136), i * 2.0f))
                        g.DrawPath(pen, p);
                }

                using (GraphicsPath p = RoundRect(pr, 9f))
                {
                    // 完全不透明：真实终端本就不透，也确保背后代码雨绝不干扰阅读
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 0, 10, 6)))
                        g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(120, 0, 255, 140), 1.2f))
                        g.DrawPath(pen, p);
                }

                float titleH = 36f, tabH = 26f, sideW = 236f;

                // 标题栏
                using (GraphicsPath p = RoundRect(new RectangleF(px, py, pw, titleH + 10), 9f))
                {
                    g.SetClip(new RectangleF(px, py, pw, titleH));
                    using (LinearGradientBrush b = new LinearGradientBrush(
                        new RectangleF(px, py, pw, titleH), Color.FromArgb(60, 0, 255, 140), Color.FromArgb(18, 0, 255, 140), LinearGradientMode.Vertical))
                        g.FillPath(b, p);
                    g.ResetClip();
                }

                // 红黄绿灯
                Color[] dots = new Color[] { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
                for (int i = 0; i < 3; i++)
                {
                    using (SolidBrush b = new SolidBrush(dots[i]))
                        g.FillEllipse(b, px + 13 + i * 20, py + 13, 12, 12);
                }

                string title = "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe";
                SizeF ts = g.MeasureString(title, fTitle);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 138, 255, 193)))
                    g.DrawString(title, fTitle, b, px + (pw - ts.Width) / 2f, py + 11);

                // 标签条
                using (Pen pen = new Pen(Color.FromArgb(45, 0, 255, 140), 1f))
                    g.DrawLine(pen, px + 1, py + titleH, px + pw - 1, py + titleH);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 138, 255, 193)))
                    g.DrawString("Windows PowerShell", fTiny, b, px + 14, py + titleH + 6);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 63, 143, 102)))
                    g.DrawString("+", fTiny, b, px + 14 + 130, py + titleH + 6);
                string pid = "PID 1337";
                SizeF ps = g.MeasureString(pid, fTiny);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 63, 143, 102)))
                    g.DrawString(pid, fTiny, b, px + pw - ps.Width - 14, py + titleH + 6);

                // 分隔线
                using (Pen pen = new Pen(Color.FromArgb(45, 0, 255, 140), 1f))
                    g.DrawLine(pen, px + 1, py + titleH + tabH, px + pw - 1, py + titleH + tabH);

                float cx0 = px + 14, cy0 = py + titleH + tabH + 10;
                float cw = pw - sideW - 30, chh = ph - titleH - tabH - 22;
                float sideX = px + pw - sideW;

                // ---- 终端文本 ----
                g.SetClip(new RectangleF(cx0, cy0, cw, chh));
                float total = 0;
                int first = lines.Count;
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    float lh = lines[i].Logo ? fBig.Height + 8 : lines[i].Height;
                    if (total + lh > chh) break;
                    total += lh; first = i;
                }
                float ty = cy0;   // 从顶部往下填，跟真实终端一致
                for (int i = first; i < lines.Count; i++)
                {
                    Line l = lines[i];
                    if (l.Logo)
                    {
                        DrawLogo(g, cx0, ty);
                        ty += fBig.Height + 8;
                    }
                    else
                    {
                        string s = l.Typing ? l.Text.Substring(0, Math.Min(l.Shown, l.Text.Length)) : l.Text;
                        using (SolidBrush b = new SolidBrush(l.Col))
                            g.DrawString(s, l.Fnt, b, cx0, ty);
                        ty += l.Height;
                    }
                }
                g.ResetClip();

                // ---- 侧栏 ----
                using (Pen pen = new Pen(Color.FromArgb(60, 0, 255, 140), 1f))
                    g.DrawLine(pen, sideX, cy0 - 4, sideX, cy0 + chh);
                DrawSidebar(g, sideX + 14, cy0, sideW - 28, chh);
            }

            // ---------- 4. 全屏特效 ----------
            // 闪光
            if (flashT > 0 && tTotal - flashT < 0.55)
            {
                double k = 1.0 - (tTotal - flashT) / 0.55;
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(235 * k), 201, 255, 228)))
                    g.FillRectangle(b, 0, 0, W, H);
            }
            // 故障色闪
            if (tGlitch > 0 && tTotal - tGlitch < 1.6)
            {
                double k = 1.0 - (tTotal - tGlitch) / 1.6;
                int a = (int)(70 * k);
                if ((int)(tTotal * 24) % 2 == 0)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 255, 0, 60)))
                        g.FillRectangle(b, 0, 0, W / 3, H);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 0, 220, 255)))
                        g.FillRectangle(b, W / 3, 0, W / 3, H);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 0, 255, 140)))
                        g.FillRectangle(b, 2 * W / 3, 0, W - 2 * W / 3, H);
                }
            }
            // ACCESS GRANTED 大字
            if (tGranted > 0)
            {
                double gel = tTotal - tGranted;
                if (gel < 3.4)
                {
                    double k = gel < 0.35 ? gel / 0.35 : 1.0;
                    double fade = gel > 2.7 ? 1.0 - (gel - 2.7) / 0.7 : 1.0;
                    int al = (int)(255 * k * fade);
                    if (al > 0)
                    {
                        float scale = (float)(0.72 + 0.28 * Math.Min(1.0, gel / 0.35) + (gel > 0.35 && gel < 0.5 ? 0.05 : 0));
                        Font bf = new Font(fBig.FontFamily, 54f * scale, FontStyle.Bold);
                        SizeF bs = g.MeasureString(banner, bf);
                        float bxp = (W - bs.Width) / 2f, byp = H * 0.16f;
                        // 辉光
                        for (int r = 12; r >= 2; r -= 2)
                        {
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(al / 12, 0, 255, 136)))
                                g.DrawString(banner, bf, b, bxp - r, byp);
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(al / 12, 0, 255, 136)))
                                g.DrawString(banner, bf, b, bxp + r, byp);
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(al / 12, 0, 255, 136)))
                                g.DrawString(banner, bf, b, bxp, byp - r);
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(al / 12, 0, 255, 136)))
                                g.DrawString(banner, bf, b, bxp, byp + r);
                        }
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(al, 255, 0, 80)))
                            g.DrawString(banner, bf, b, bxp - 3, byp);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(al, 0, 220, 255)))
                            g.DrawString(banner, bf, b, bxp + 3, byp);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(al, 234, 255, 243)))
                            g.DrawString(banner, bf, b, bxp, byp);
                        bf.Dispose();
                    }
                }
            }

            // 暗角 + 扫描线
            DrawVignette(g, W, H);

            // 底部 HUD
            if (!hideUI && state == St.Run)
            {
                DrawHud(g, W, H);
            }

            // 整帧一次性贴到窗口（走 e.Graphics，不碰 CreateGraphics，避免与系统双缓冲打架）
            e.Graphics.DrawImageUnscaled(frame, 0, 0);
            }
            finally
            {
                double ms = (clock.ElapsedTicks - swPaint) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                paintMsSum += ms; paintMsCount++;
                if (ms > paintMsMax) paintMsMax = ms;
            }
        }

        void DrawLogo(Graphics g, float x, float y)
        {
            string a = "JIAHAO", b = "PRO";
            for (int r = 8; r >= 2; r -= 2)
            {
                using (SolidBrush br = new SolidBrush(Color.FromArgb(16, 0, 255, 136)))
                    g.DrawString(a, fBig, br, x - r, y);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(16, 0, 255, 136)))
                    g.DrawString(a, fBig, br, x + r, y);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(16, 0, 255, 136)))
                    g.DrawString(a, fBig, br, x, y - r);
                using (SolidBrush br = new SolidBrush(Color.FromArgb(16, 0, 255, 136)))
                    g.DrawString(a, fBig, br, x, y + r);
            }
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 234, 255, 243)))
                g.DrawString(a, fBig, br, x, y);

            SizeF sa = g.MeasureString(a, fBig);
            float bx = x + sa.Width + 6;
            SizeF sb = g.MeasureString(b, fBig);
            RectangleF box = new RectangleF(bx, y + 2, sb.Width + 22, fBig.Height + 2);
            for (int r = 10; r >= 2; r -= 2)
            {
                using (GraphicsPath p = RoundRect(RectangleF.Inflate(box, r, r), 8))
                using (SolidBrush br = new SolidBrush(Color.FromArgb(9, 57, 255, 158)))
                    g.FillPath(br, p);
            }
            using (GraphicsPath p = RoundRect(box, 6))
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 57, 255, 158)))
                g.FillPath(br, p);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 3, 21, 12)))
                g.DrawString(b, fBig, br, bx + 11, y + 2);
        }

        void DrawSidebar(Graphics g, float x, float y, float w, float h)
        {
            float cy = y;
            cy = SideBarSection(g, x, cy, w, "CPU LOAD", bCpu);
            cy = SideBarSection(g, x, cy, w, "MEMORY", bMem);
            cy = SideBarSection(g, x, cy, w, "NET I/O", bNet);
            cy += 14;

            using (SolidBrush b = new SolidBrush(ColDim)) g.DrawString("TARGET", fTiny, b, x, cy);
            cy += 15;
            cy = Kv(g, x, cy, w, "HOST", "JIAHAO-PC", ColHi);
            cy = Kv(g, x, cy, w, "IP", "10.0.0.7", ColHi);
            cy = Kv(g, x, cy, w, "FIREWALL", "BYPASSED", ColOk);
            cy = Kv(g, x, cy, w, "UPLINK", "STABLE", ColOk);
            cy += 14;

            using (SolidBrush b = new SolidBrush(ColDim)) g.DrawString("PROGRESS", fTiny, b, x, cy);
            cy += 15;
            cy = Kv(g, x, cy, w, "CRACK", pct + "%", ColHi);
            using (GraphicsPath p = RoundRect(new RectangleF(x, cy, w, 7), 3.5f))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 0, 255, 140))) g.FillPath(b, p);
                if (pct > 0)
                {
                    using (GraphicsPath p2 = RoundRect(new RectangleF(x, cy, Math.Max(4f, w * pct / 100f), 7), 3.5f))
                    using (SolidBrush b = new SolidBrush(ColOk))
                        g.FillPath(b, p2);
                }
            }
            cy += 14;
            cy = Kv(g, x, cy, w, "PACKETS", packets.ToString("N0"), ColHi);
            cy = Kv(g, x, cy, w, "THREADS", threads.ToString(), ColHi);

            using (SolidBrush b = new SolidBrush(ColDim))
            {
                g.DrawString("[空格]灌代码 [R]重播", fTiny, b, x, y + h - 32);
                g.DrawString("[H]隐藏 [S]静音 [ESC]退出", fTiny, b, x, y + h - 17);
            }
        }

        float SideBarSection(Graphics g, float x, float y, float w, string label, float v)
        {
            using (SolidBrush b = new SolidBrush(ColDim)) g.DrawString(label, fTiny, b, x, y);
            y += 15;
            using (GraphicsPath p = RoundRect(new RectangleF(x, y, w, 7), 3.5f))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 0, 255, 140))) g.FillPath(b, p);
                using (GraphicsPath p2 = RoundRect(new RectangleF(x, y, Math.Max(4f, w * v / 100f), 7), 3.5f))
                using (SolidBrush b = new SolidBrush(ColOk))
                    g.FillPath(b, p2);
            }
            return y + 20;
        }

        float Kv(Graphics g, float x, float y, float w, string k, string v, Color c)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 79, 214, 147)))
                g.DrawString(k, fTiny, b, x, y);
            SizeF s = g.MeasureString(v, fTiny);
            using (SolidBrush b = new SolidBrush(c))
                g.DrawString(v, fTiny, b, x + w - s.Width, y);
            return y + 16;
        }

        // 暗角 + 扫描线是静态的：预渲染成一张叠加层，每帧只贴一次。
        // （之前每帧画 ~680 次暗角 + ~288 次扫描线 FillRectangle，纯浪费）
        Bitmap overlay;
        void BuildOverlay(int W, int H)
        {
            if (overlay != null) overlay.Dispose();
            overlay = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(overlay))
            {
                int band = Math.Max(60, W / 12);
                for (int i = 0; i < band; i += 2)
                {
                    int a = (int)(70.0 * (1.0 - i / (double)band));
                    if (a <= 0) continue;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 0, 0, 0)))
                    {
                        g.FillRectangle(b, 0, i, W, 1);
                        g.FillRectangle(b, 0, H - 1 - i, W, 1);
                        g.FillRectangle(b, i, 0, 1, H);
                        g.FillRectangle(b, W - 1 - i, 0, 1, H);
                    }
                }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(26, 0, 0, 0)))
                {
                    for (int yy = 0; yy < H; yy += 4) g.FillRectangle(b, 0, yy, W, 1);
                }
            }
        }

        void DrawVignette(Graphics g, int W, int H)
        {
            if (overlay == null || overlay.Width != W || overlay.Height != H) BuildOverlay(W, H);
            g.DrawImageUnscaled(overlay, 0, 0);
        }

        string hudClock = "--:--:--";
        void DrawHud(Graphics g, int W, int H)
        {
            string left = "● REC   JIAHAO-NET // NODE 07";
            string mid = "UPLINK ENCRYPTED · AES-256-GCM";
            string right = hudClock;      // 每秒更新一次，不必每帧格式化
            float by = H - 30;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillRectangle(b, 0, by - 8, W, 40);
            using (SolidBrush b = new SolidBrush(ColOk))
                g.DrawString(left, fHud, b, 18, by);
            SizeF ms = g.MeasureString(mid, fHud);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 63, 143, 102)))
                g.DrawString(mid, fHud, b, (W - ms.Width) / 2f, by);
            SizeF rs = g.MeasureString(right, fHud);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 138, 255, 193)))
                g.DrawString(right, fHud, b, W - rs.Width - 18, by);
        }

        // ============================================================
        //  交互
        // ============================================================
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode == Keys.Space)
            {
                for (int i = 0; i < 26; i++) Out(FakeLine(), PickC(new Color[] { ColTxt, ColOk, ColDim }));
            }
            else if (e.KeyCode == Keys.H)
            {
                hideUI = !hideUI;
            }
            else if (e.KeyCode == Keys.S)
            {
                soundOn = !soundOn;
            }
            else if (e.KeyCode == Keys.R)
            {
                lines.Clear(); steps.Clear(); stepIdx = 0; tScript = 0; infinite = false;
                pct = 0; packets = 0; tGranted = -1; tGlitch = -1; shakeT = -1;
                BuildScript();
                state = St.Boot; tState = 0; bootShown = 0;
                dropY = null;
                EnsureFrame();
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Out("[!] 别点，看着就行 —— 佳豪", ColWarn);
            base.OnMouseClick(e);
        }
    }
}
