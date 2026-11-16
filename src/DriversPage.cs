// W-Guard - drivers: devices without a driver or with a problem, and the graphics driver's age.
// W-Guard never downloads drivers itself; it points to Windows Update or the maker's official tool.
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace WGuard
{
    class DevIssue { public string Name = "", DeviceId = "", Class = ""; public int Code; }
    class GpuInfo { public string Name = "", Version = "", DeviceId = ""; public DateTime Date = DateTime.MinValue; }
    class DriverInfo { public string Name = "", Group = "", Version = "", Provider = "", Maker = "", DeviceId = "", Class = ""; public DateTime Date = DateTime.MinValue; public bool Generic, Inbox; }

    static class Drivers
    {
        public static List<DevIssue> Issues = new List<DevIssue>();
        public static List<GpuInfo> Gpus = new List<GpuInfo>();
        public static List<DriverInfo> All = new List<DriverInfo>();
        public static bool Scanned;

        // the hardware groups that usually need the maker's own driver
        public static readonly string[] Groups = { "display", "audio", "network", "bluetooth", "chipset", "storage", "camera" };
        public static string GroupName(string g)
        {
            switch (g)
            {
                case "display": return L.T("كرت الشاشة", "Graphics");
                case "audio": return L.T("الصوت", "Audio");
                case "network": return L.T("الشبكة والإنترنت", "Network");
                case "bluetooth": return L.T("البلوتوث", "Bluetooth");
                case "chipset": return L.T("اللوحة الأم (Chipset)", "Motherboard (chipset)");
                case "storage": return L.T("التخزين", "Storage");
                case "camera": return L.T("الكاميرا", "Camera");
            }
            return g;
        }

        static string GroupOf(string cls, string id)
        {
            switch ((cls ?? "").ToUpperInvariant())
            {
                case "DISPLAY": return "display";
                case "MEDIA": return "audio";
                case "NET": return "network";
                case "BLUETOOTH": return id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) ? "" : "bluetooth";
                case "SYSTEM": return id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) || id.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase) ? "chipset" : "";
                case "SCSIADAPTER": case "HDC": return "storage";
                case "CAMERA": case "IMAGE": return "camera";
            }
            return "";
        }

        // every installed driver for real hardware, grouped (software and virtual devices are left out)
        static List<DriverInfo> ReadAll()
        {
            var list = new List<DriverInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in U.Wmi(@"root\cimv2", "SELECT DeviceName, DeviceClass, DriverVersion, DriverDate, DriverProviderName, Manufacturer, DeviceID FROM Win32_PnPSignedDriver"))
            {
                string id = U.S(o, "DeviceID"), cls = U.S(o, "DeviceClass"), name = U.S(o, "DeviceName");
                if (id.Length == 0 || name.Length == 0) continue;
                string up = id.ToUpperInvariant();
                if (up.StartsWith("ROOT\\") || up.StartsWith("SWD\\") || up.StartsWith("HTREE\\") || up.StartsWith("STORAGE\\")) continue;
                string g = GroupOf(cls, id);
                if (g.Length == 0) continue;
                // Bluetooth headsets add their own audio entries; they come with the headset, not a driver to install
                if (g == "audio" && (up.StartsWith("BTHENUM\\") || up.StartsWith("BTHHFENUM\\") || up.StartsWith("BTHLEDEVICE\\"))) continue;
                string low = name.ToLowerInvariant();
                if (low.Contains("wan miniport") || low.Contains("virtual") || low.Contains("kernel debug") || low.Contains("teredo") || low.Contains("loopback")) continue;
                var d = new DriverInfo { Name = name, Group = g, Class = cls, DeviceId = id, Version = U.S(o, "DriverVersion"), Provider = U.S(o, "DriverProviderName"), Maker = U.S(o, "Manufacturer"), Date = U.WmiDate(U.S(o, "DriverDate")) };
                // Intel's chipset files carry a placeholder date (1968), which means "not dated"
                if (d.Date.Year < 2000) d.Date = DateTime.MinValue;
                bool msDriver = d.Provider.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
                d.Inbox = msDriver;
                // chipset: Windows' own bridge drivers are normal, only the maker's drivers are worth listing
                if (g == "chipset" && msDriver) continue;
                string key = g + "|" + name + "|" + d.Version;
                if (!seen.Add(key)) continue;
                bool msHw = d.Maker.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0 || d.Maker.StartsWith("(", StringComparison.Ordinal);
                d.Generic = msDriver && (g == "display" || g == "audio" || g == "network" || g == "bluetooth" || g == "camera") && (!msHw || low.Contains("high definition audio device") || low.Contains("basic display"));
                list.Add(d);
            }
            list.Sort((a, b) => { int x = Array.IndexOf(Groups, a.Group).CompareTo(Array.IndexOf(Groups, b.Group)); return x != 0 ? x : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        public static int GenericCount { get { int n = 0; foreach (var d in All) if (d.Generic) n++; return n; } }

        public static void Scan()
        {
            var issues = new List<DevIssue>();
            foreach (var o in U.Wmi(@"root\cimv2", "SELECT Name, DeviceID, PNPClass, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
            {
                int code = (int)U.N(o, "ConfigManagerErrorCode");
                if (code == 0 || code == 45) continue;   // 45 = not connected right now
                var d = new DevIssue { Code = code, Name = U.S(o, "Name"), DeviceId = U.S(o, "DeviceID"), Class = U.S(o, "PNPClass") };
                if (d.Name.Length == 0) d.Name = L.T("جهاز غير معروف", "Unknown device");
                issues.Add(d);
            }
            issues.Sort((a, b) => Severity(b.Code).CompareTo(Severity(a.Code)));
            var gpus = new List<GpuInfo>();
            foreach (var o in U.Wmi(@"root\cimv2", "SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController"))
            {
                var g = new GpuInfo { Name = U.S(o, "Name"), Version = U.S(o, "DriverVersion"), DeviceId = U.S(o, "PNPDeviceID"), Date = U.WmiDate(U.S(o, "DriverDate")) };
                string n = g.Name.ToLowerInvariant();
                if (n.Contains("remote") || n.Contains("virtual") || n.Contains("parsec") || n.Contains("meta") || n.Contains("dameware") || n.Contains("citrix")) continue;
                gpus.Add(g);
            }
            List<DriverInfo> all;
            try { all = ReadAll(); } catch (Exception ex) { Log.Write("driver list: " + ex.Message); all = new List<DriverInfo>(); }
            Issues = issues;
            Gpus = gpus;
            All = all;
            Scanned = true;
            Log.Write("drivers: " + issues.Count + " device problem(s), " + gpus.Count + " display adapter(s), " + all.Count + " hardware drivers, " + GenericCount + " generic");
        }

        // asks Windows to look again for new or changed hardware (same as "Scan for hardware changes")
        public static void Rescan()
        {
            try
            {
                var psi = new ProcessStartInfo("pnputil.exe", "/scan-devices") { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) p.WaitForExit(60000);
            }
            catch (Exception ex) { Log.Write("pnputil: " + ex.Message); }
            Scan();
        }

        public static int Count(int severity) { int n = 0; foreach (var d in Issues) if (Severity(d.Code) == severity) n++; return n; }
        public static int ProblemCount { get { return Count(2) + Count(1) + GpuMissing; } }
        public static int GpuMissing { get { int n = 0; foreach (var g in Gpus) if (IsBasic(g)) n++; return n; } }
        public static bool IsBasic(GpuInfo g) { return g.Name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 || g.Name.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) >= 0; }

        // 2 = no / broken driver, 1 = device problem, 0 = disabled on purpose
        public static int Severity(int code)
        {
            switch (code)
            {
                case 1: case 18: case 28: case 31: case 32: case 37: case 39: case 40: case 48: case 52: return 2;
                case 22: case 29: return 0;
                default: return 1;
            }
        }

        public static string Status(int code)
        {
            if (code == 28 || code == 1) return L.T("بدون تعريف", "No driver");
            int s = Severity(code);
            if (s == 2) return L.T("مشكلة في التعريف", "Driver problem");
            if (s == 0) return L.T("معطّل", "Disabled");
            return L.T("فيه مشكلة", "Has a problem");
        }

        public static string Explain(int code)
        {
            switch (code)
            {
                case 1: case 28: return L.T("التعريف غير مثبّت", "The driver is not installed");
                case 3: return L.T("التعريف تالف أو الذاكرة لا تكفي", "The driver may be corrupted");
                case 10: return L.T("الجهاز لا يقدر يشتغل", "The device cannot start");
                case 12: return L.T("الموارد المتاحة لا تكفي للجهاز", "Not enough free resources");
                case 14: return L.T("يحتاج إعادة تشغيل الجهاز", "A restart is needed");
                case 18: return L.T("التعريف يحتاج إعادة تثبيت", "The driver needs reinstalling");
                case 19: return L.T("إعدادات الجهاز في السجل ناقصة أو تالفة", "Registry settings are incomplete");
                case 22: return L.T("معطّل من إدارة الأجهزة", "Disabled in Device Manager");
                case 24: return L.T("الجهاز غير موجود أو لا يعمل كما يجب", "Device is missing or not working");
                case 29: return L.T("معطّل من إعدادات BIOS", "Disabled in the firmware (BIOS)");
                case 31: return L.T("ويندوز لا يقدر يحمّل التعريف", "Windows cannot load the driver");
                case 32: return L.T("خدمة التعريف معطّلة", "The driver service is disabled");
                case 37: case 39: return L.T("التعريف مفقود أو تالف", "The driver is missing or corrupted");
                case 40: return L.T("معلومات التعريف في السجل مفقودة", "Driver registry information is missing");
                case 43: return L.T("ويندوز أوقف الجهاز لأنه أبلغ عن مشكلة", "Windows stopped the device because it reported a problem");
                case 48: return L.T("التعريف محظور لأنه غير متوافق", "The driver is blocked as incompatible");
                case 52: return L.T("التعريف غير موقّع رقمياً", "The driver is not digitally signed");
            }
            return L.T("مشكلة في الجهاز", "Device problem");
        }

        public static string Fix(DevIssue d)
        {
            string v = VendorName(d.DeviceId);
            switch (d.Code)
            {
                case 14: return L.T("أعد تشغيل الجهاز.", "Restart the PC.");
                case 22: return L.T("لا تحتاج شيء إذا عطّلته بنفسك. لتفعيله افتح إدارة الأجهزة.", "Nothing to do if you disabled it yourself. To enable it, open Device Manager.");
                case 29: return L.T("فعّله من إعدادات BIOS إذا كنت تحتاجه.", "Enable it in the BIOS settings if you need it.");
                case 43: case 10: return L.T("افصل الجهاز وركّبه في منفذ ثاني. إذا استمر، أعد تثبيت تعريفه من الموقع الرسمي.", "Reconnect it to another port. If it continues, reinstall its driver from the official source.");
            }
            if (v.Length > 0) return L.T("ابحث في Windows Update، أو نزّل التعريف من موقع " + v + " الرسمي.", "Check Windows Update, or get the driver from " + v + "'s official site.");
            return L.T("ابحث في Windows Update، أو من موقع الشركة المصنّعة للجهاز أو اللوحة الأم.", "Check Windows Update, or the device / motherboard maker's official site.");
        }

        static string Ven(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string u = id.ToUpperInvariant();
            int i = u.IndexOf("VEN_");
            if (i < 0) i = u.IndexOf("VID_");
            return i >= 0 && u.Length >= i + 8 ? u.Substring(i + 4, 4) : "";
        }

        public static string VendorName(string id)
        {
            switch (Ven(id))
            {
                case "10DE": return "NVIDIA";
                case "1002": case "1022": return "AMD";
                case "8086": case "8087": return "Intel";
                case "10EC": case "0BDA": return "Realtek";
                case "14E4": return "Broadcom";
                case "168C": case "17CB": return "Qualcomm";
                case "1B21": return "ASMedia";
            }
            return "";
        }

        // official driver tools only - never a third-party "driver updater"
        public static string VendorUrl(string id)
        {
            switch (VendorName(id))
            {
                case "NVIDIA": return "https://www.nvidia.com/software/nvidia-app/";
                case "AMD": return "https://www.amd.com/en/support/download/drivers.html";
                case "Intel": return "https://www.intel.com/content/www/us/en/support/detect.html";
            }
            return "";
        }

        public static void OpenWindowsUpdate()
        {
            try { Process.Start("ms-settings:windowsupdate-optionalupdates"); }
            catch { U.Open("ms-settings:windowsupdate"); }
        }

        public static void LoadDemo()
        {
            Issues = new List<DevIssue>
            {
                new DevIssue { Name = "Ethernet Controller", DeviceId = @"PCI\VEN_10EC&DEV_8125&SUBSYS_86771043", Code = 28, Class = "Net" },
                new DevIssue { Name = "PCI Simple Communications Controller", DeviceId = @"PCI\VEN_8086&DEV_7AE8&SUBSYS_86941043", Code = 28, Class = "" },
                new DevIssue { Name = "USB Audio Device", DeviceId = @"USB\VID_0D8C&PID_0012\5&2F3A0C1&0&4", Code = 43, Class = "MEDIA" },
            };
            Gpus = new List<GpuInfo> { new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Version = "32.0.15.5186", DeviceId = @"PCI\VEN_10DE&DEV_2504", Date = DateTime.Now.AddDays(-212) } };
            All = new List<DriverInfo>
            {
                new DriverInfo { Group = "display", Name = "NVIDIA GeForce RTX 3060", Provider = "NVIDIA", Version = "32.0.15.5186", DeviceId = @"PCI\VEN_10DE&DEV_2504", Date = DateTime.Now.AddDays(-212) },
                new DriverInfo { Group = "audio", Name = "High Definition Audio Device", Provider = "Microsoft", Maker = "Realtek", Version = "10.0.22621.1", DeviceId = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897", Date = DateTime.Now.AddDays(-900), Generic = true },
                new DriverInfo { Group = "audio", Name = "NVIDIA High Definition Audio", Provider = "NVIDIA Corporation", Version = "1.4.0.1", DeviceId = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_00A4", Date = DateTime.Now.AddDays(-400) },
                new DriverInfo { Group = "network", Name = "Intel(R) Wi-Fi 6 AX201 160MHz", Provider = "Intel", Version = "23.40.0.4", DeviceId = @"PCI\VEN_8086&DEV_A0F0", Date = DateTime.Now.AddDays(-150) },
                new DriverInfo { Group = "bluetooth", Name = "Intel(R) Wireless Bluetooth(R)", Provider = "Intel Corporation", Version = "23.40.0.2", DeviceId = @"USB\VID_8087&PID_0026", Date = DateTime.Now.AddDays(-150) },
                new DriverInfo { Group = "chipset", Name = "Intel(R) Management Engine Interface", Provider = "Intel", Version = "2251.4.2.0", DeviceId = @"PCI\VEN_8086&DEV_06E0", Date = DateTime.Now.AddDays(-480) },
                new DriverInfo { Group = "storage", Name = "Standard NVM Express Controller", Provider = "Microsoft", Version = "10.0.22621.2506", DeviceId = @"PCI\VEN_2646&DEV_2263", Date = DateTime.Now.AddDays(-700) },
            };
            Scanned = true;
        }
    }

    class DriversPage : Panel, IPage
    {
        readonly MainForm main;
        readonly Panel scroll;
        readonly DriversCanvas canvas;
        static bool busy;

        public DriversPage(MainForm owner)
        {
            main = owner;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(28, 0, 28, 16);
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            canvas = new DriversCanvas(this) { Dock = DockStyle.Top };
            scroll.Controls.Add(canvas);
            canvas.MouseEnter += (s, e) => scroll.Focus();
            Controls.Add(scroll);
        }

        public bool Busy { get { return busy; } }

        public void Scan(bool rescan)
        {
            if (busy) return;
            if (Demo.On) { Drivers.LoadDemo(); main.RefreshAll(); return; }
            busy = true;
            canvas.Relayout(); canvas.Invalidate();
            var th = new Thread(() =>
            {
                try { if (rescan) Drivers.Rescan(); else Drivers.Scan(); } catch (Exception ex) { Log.Write("drivers scan: " + ex.Message); }
                busy = false;
                try { BeginInvoke((Action)main.RefreshAll); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        public string Title { get { return L.T("التعريفات", "Drivers"); } }
        public string Subtitle { get { return L.T("الأجهزة اللي بدون تعريف أو فيها مشكلة، وحالة تعريف كرت الشاشة", "Devices without a driver or with a problem, and your graphics driver"); } }

        public Control[] HeaderActions
        {
            get
            {
                var re = new FlatButton(L.T("إعادة الفحص", "Re-scan"), FlatButton.Kind.Secondary, Glyph.Refresh);
                re.Enabled = !busy;
                re.Click += (s, e) => Scan(true);
                var dm = new FlatButton(L.T("إدارة الأجهزة", "Device Manager"), FlatButton.Kind.Secondary, Glyph.Settings);
                dm.Click += (s, e) => U.Open("devmgmt.msc");
                var wu = new FlatButton(L.T("البحث في Windows Update", "Check Windows Update"), FlatButton.Kind.Primary, Glyph.Download);
                wu.Click += (s, e) => Drivers.OpenWindowsUpdate();
                return new Control[] { re, dm, wu };
            }
        }

        public void OnShown() { if (!Drivers.Scanned) Scan(false); canvas.Relayout(); canvas.Invalidate(); }
    }

    class DriversCanvas : Control
    {
        readonly DriversPage page;
        readonly List<KeyValuePair<Rectangle, Action>> hits = new List<KeyValuePair<Rectangle, Action>>();
        static readonly Font Mono = new Font("Consolas", 8.5f);
        int contentH = 600;

        public DriversCanvas(DriversPage p)
        {
            page = p;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = 600;
        }

        public void Relayout()
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                int h = Draw(g, false);
                if (h != contentH) contentH = h;
                if (Height != contentH) Height = contentH;
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Draw(e.Graphics, true); }

        Rectangle M(Rectangle r) { return Theme.M(r, Width); }

        void Link(Graphics g, bool paint, ref int x, int y, string text, Action act)
        {
            int w = TextRenderer.MeasureText(text, Theme.Bold).Width + 4;
            var r = M(new Rectangle(x, y, w, 22));
            if (paint) Theme.Txt(g, text, Theme.Bold, Theme.Link, r, Theme.Align.Center);
            hits.Add(new KeyValuePair<Rectangle, Action>(r, act));
            x += w + 18;
        }

        // one pass both measures (paint = false) and draws, so the layout cannot drift between the two
        int Draw(Graphics g, bool paint)
        {
            hits.Clear();
            int W = Math.Max(Width, 400);
            int y = 0;

            if (page.Busy || !Drivers.Scanned)
            {
                var br = new Rectangle(0, 0, W - 1, 52);
                if (paint)
                {
                    Theme.FillRound(g, Theme.TealTint, br, 12);
                    Theme.Txt(g, L.T("جارٍ فحص الأجهزة والتعريفات...", "Checking devices and drivers..."), Theme.Body, Theme.Text, M(new Rectangle(18, 0, W - 36, 52)), Theme.Align.Start);
                }
                return 60;
            }

            // three summary cards
            int red = Drivers.Count(2) + Drivers.GpuMissing, amber = Drivers.Count(1);
            int kw = (W - 32) / 3;
            string gpuName = Drivers.Gpus.Count > 0 ? Drivers.Gpus[0].Name : L.T("غير معروف", "Unknown");
            string gpuSub = "";
            if (Drivers.Gpus.Count > 0)
            {
                var g0 = Drivers.Gpus[0];
                if (Drivers.IsBasic(g0)) gpuSub = L.T("بدون تعريف", "No driver");
                else if (g0.Date > DateTime.MinValue) gpuSub = L.T("عمر التعريف: ", "Driver age: ") + Age(g0.Date);
            }
            string[] kt = { L.T("أجهزة بدون تعريف", "Devices without a driver"), L.T("أجهزة فيها مشكلة", "Devices with a problem"), L.T("كرت الشاشة", "Graphics card") };
            string[] kv = { red.ToString(), amber.ToString(), gpuName };
            Color[] kc = { red > 0 ? Theme.Danger : Theme.Ok, amber > 0 ? Theme.WarnDot : Theme.Ok, Theme.Text };
            for (int i = 0; i < 3; i++)
            {
                var r = M(new Rectangle(i * (kw + 16), y, kw, 96));
                if (!paint) continue;
                Theme.FillRound(g, Theme.Card, r, 14);
                Theme.StrokeRound(g, Theme.Border, r, 14);
                var inner = new Rectangle(r.X + 18, r.Y + 12, r.Width - 36, 22);
                Theme.Txt(g, kt[i], Theme.Body, Theme.Muted, inner, Theme.Align.Start);
                Theme.Txt(g, kv[i], i < 2 ? Theme.Big : Theme.H2, kc[i], new Rectangle(r.X + 18, r.Y + 36, r.Width - 36, i < 2 ? 46 : 28), Theme.Align.Start);
                if (i == 2 && gpuSub.Length > 0) Theme.Txt(g, gpuSub, Theme.Small, Theme.Muted, new Rectangle(r.X + 18, r.Y + 66, r.Width - 36, 20), Theme.Align.Start);
            }
            y += 112;

            // device list
            var list = Drivers.Issues;
            int pad = 18;
            if (list.Count == 0)
            {
                var r = M(new Rectangle(0, y, W - 1, 64));
                if (paint)
                {
                    Theme.FillRound(g, Theme.OkBg, r, 14);
                    TextRenderer.DrawText(g, Glyph.Check, Theme.IconBig, M(new Rectangle(18, y, 30, 64)), Theme.Ok, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    Theme.Txt(g, L.T("كل الأجهزة تعمل بتعريفاتها", "Every device has a working driver"), Theme.H2, Theme.Ok, M(new Rectangle(58, y, W - 80, 64)), Theme.Align.Start);
                }
                y += 80;
            }
            else
            {
                int cardTop = y;
                int innerW = W - pad * 2;
                int c1 = (int)(innerW * 0.36), c2 = 130, c3 = innerW - c1 - c2;
                int hy = y + 14;
                if (paint)
                {
                    // measure the rows first so the card can be painted behind them
                    int total = 38 + 6 + 14;
                    foreach (var d in list)
                    {
                        int th0 = Theme.TextHeight(g, Drivers.Fix(d), Theme.Body, c3 - 10);
                        int nh0 = Theme.TextHeight(g, d.Name, Theme.Bold, c1 - 10);
                        total += Math.Max(nh0 + 26, th0 + 22 + 28) + 14;
                    }
                    var card = M(new Rectangle(0, cardTop, W - 1, total));
                    Theme.FillRound(g, Theme.Card, card, 14);
                    Theme.StrokeRound(g, Theme.Border, card, 14);
                }
                if (paint) Theme.Txt(g, L.T("الأجهزة اللي تحتاج انتباه", "Devices that need attention"), Theme.H2, Theme.Text, M(new Rectangle(pad, hy, innerW, 26)), Theme.Align.Start);
                hy += 38;
                foreach (var d in list)
                {
                    string fix = Drivers.Fix(d);
                    int th = Theme.TextHeight(g, fix, Theme.Body, c3 - 10);
                    int nh = Theme.TextHeight(g, d.Name, Theme.Bold, c1 - 10);
                    int rh = Math.Max(nh + 26, th + 22 + 28) + 14;
                    if (paint)
                    {
                        using (var p = new Pen(Theme.Border)) g.DrawLine(p, pad, hy, W - pad, hy);
                        Theme.Txt(g, d.Name, Theme.Bold, Theme.Text, M(new Rectangle(pad, hy + 10, c1 - 10, nh)), Theme.Align.Start, false, true);
                        TextRenderer.DrawText(g, d.DeviceId.Length > 48 ? d.DeviceId.Substring(0, 48) + "…" : d.DeviceId, Mono, M(new Rectangle(pad, hy + 12 + nh, c1 - 10, 18)), Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));
                        int sev = Drivers.Severity(d.Code);
                        Color sc = sev == 2 ? Theme.Danger : sev == 1 ? Theme.WarnDot : Theme.Faint;
                        var dot = M(new Rectangle(pad + c1, hy + 18, 9, 9));
                        using (var b = new SolidBrush(sc)) g.FillEllipse(b, dot);
                        Theme.Txt(g, Drivers.Status(d.Code), Theme.Bold, sc, M(new Rectangle(pad + c1 + 14, hy + 10, c2 - 14, 24)), Theme.Align.Start);
                        Theme.Txt(g, fix, Theme.Body, Theme.Text, M(new Rectangle(pad + c1 + c2, hy + 10, c3 - 10, th)), Theme.Align.Start, false, true);
                        Theme.Txt(g, Drivers.Explain(d.Code) + "  (" + L.T("الرمز ", "code ") + d.Code + ")", Theme.Small, Theme.Muted, M(new Rectangle(pad + c1 + c2, hy + 12 + th, c3 - 10, 18)), Theme.Align.Start);
                    }
                    int lx = pad + c1 + c2, ly = hy + 34 + th;
                    var dd = d;
                    if (Drivers.Severity(d.Code) > 0)
                    {
                        Link(g, paint, ref lx, ly, "Windows Update", Drivers.OpenWindowsUpdate);
                        string vu = Drivers.VendorUrl(d.DeviceId);
                        if (vu.Length > 0) Link(g, paint, ref lx, ly, L.T("موقع ", "") + Drivers.VendorName(d.DeviceId) + L.T(" الرسمي", " official site"), () => U.Open(vu));
                    }
                    Link(g, paint, ref lx, ly, L.T("إدارة الأجهزة", "Device Manager"), () => U.Open("devmgmt.msc"));
                    Link(g, paint, ref lx, ly, L.T("نسخ المعرّف", "Copy ID"), () => { try { Clipboard.SetText(dd.DeviceId); } catch { } });
                    hy += rh;
                }
                hy += 6;
                y = hy + 16;
            }

            // graphics card(s)
            foreach (var gpu in Drivers.Gpus)
            {
                bool basic = Drivers.IsBasic(gpu);
                int days = gpu.Date > DateTime.MinValue ? (int)(DateTime.Now - gpu.Date).TotalDays : -1;
                bool old = !basic && days > 180;
                string pill = basic ? L.T("بدون تعريف", "No driver") : old ? L.T("قديم", "Outdated") : L.T("حديث", "Recent");
                Color pf = basic ? Theme.Danger : old ? Theme.Warn : Theme.Ok, pb = basic ? Theme.DangerBg : old ? Theme.WarnBg : Theme.OkBg;
                string vu = Drivers.VendorUrl(gpu.DeviceId);
                string vn = Drivers.VendorName(gpu.DeviceId);
                var r = M(new Rectangle(0, y, W - 1, 112));
                if (paint)
                {
                    Theme.FillRound(g, Theme.Card, r, 14);
                    Theme.StrokeRound(g, Theme.Border, r, 14);
                    Theme.Txt(g, L.T("كرت الشاشة", "Graphics card"), Theme.Small, Theme.Muted, M(new Rectangle(pad, y + 12, W - 200, 20)), Theme.Align.Start);
                    Theme.Txt(g, gpu.Name, Theme.H2, Theme.Text, M(new Rectangle(pad, y + 32, W - 200, 26)), Theme.Align.Start);
                    string det = (gpu.Version.Length > 0 ? "Driver " + gpu.Version : "") + (gpu.Date > DateTime.MinValue ? "  ·  " + U.D(gpu.Date) : "");
                    TextRenderer.DrawText(g, det, Theme.Small, M(new Rectangle(pad, y + 60, W - 200, 18)), Theme.Muted, TextFormatFlags.NoPrefix | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));
                    int pw = TextRenderer.MeasureText(pill, Theme.Small).Width + 20;
                    var pr = M(new Rectangle(W - pad - pw, y + 16, pw, 24));
                    Theme.FillRound(g, pb, pr, 12);
                    Theme.Txt(g, pill, Theme.Small, pf, pr, Theme.Align.Center);
                }
                int lx = pad, ly = y + 82;
                if (vu.Length > 0) Link(g, paint, ref lx, ly, L.T("تحديث من " + vn + " الرسمي", "Update from " + vn), () => U.Open(vu));
                Link(g, paint, ref lx, ly, "Windows Update", Drivers.OpenWindowsUpdate);
                y += 128;
            }

            // every hardware driver, grouped
            if (Drivers.All.Count > 0)
            {
                if (paint) Theme.Txt(g, L.T("كل تعريفات الجهاز", "All hardware drivers") + "  (" + Drivers.All.Count + ")", Theme.H2, Theme.Text, M(new Rectangle(0, y + 4, W, 28)), Theme.Align.Start);
                y += 40;
                int gen = Drivers.GenericCount;
                if (gen > 0)
                {
                    string gt = L.T(gen + " جهاز يعمل بتعريف عام من ويندوز. تعريف الشركة المصنّعة يعطي غالباً أداءً وميزات أفضل (مثل إعدادات الصوت والشبكة).",
                        gen + " device(s) use a generic Windows driver. The maker's driver usually gives better performance and features (audio, network settings).");
                    int gh = Theme.TextHeight(g, gt, Theme.Body, W - 80) + 24;
                    if (paint)
                    {
                        Theme.FillRound(g, Theme.WarnBg, M(new Rectangle(0, y, W - 1, gh)), 12);
                        TextRenderer.DrawText(g, Glyph.Info, Theme.Icon, M(new Rectangle(14, y, 26, gh)), Theme.Warn, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        Theme.Txt(g, gt, Theme.Body, Theme.Warn, M(new Rectangle(48, y + 12, W - 66, gh - 24)), Theme.Align.Start, false, true);
                    }
                    y += gh + 12;
                }
                foreach (string grp in Drivers.Groups)
                {
                    var rows = new List<DriverInfo>();
                    foreach (var d in Drivers.All) if (d.Group == grp) rows.Add(d);
                    if (rows.Count == 0) continue;
                    const int rh = 58;
                    int ch = 44 + rows.Count * rh + 6;
                    if (paint)
                    {
                        var card = M(new Rectangle(0, y, W - 1, ch));
                        Theme.FillRound(g, Theme.Card, card, 14);
                        Theme.StrokeRound(g, Theme.Border, card, 14);
                        Theme.Txt(g, Drivers.GroupName(grp), Theme.Bold, Theme.Text, M(new Rectangle(pad, y + 10, W - pad * 2, 26)), Theme.Align.Start);
                    }
                    int ry = y + 44;
                    foreach (var d in rows)
                    {
                        int years = d.Date > DateTime.MinValue ? (int)((DateTime.Now - d.Date).TotalDays / 365) : 0;
                        // chipset and storage drivers rarely need updating, so their age is not flagged
                        bool old = !d.Generic && !d.Inbox && years >= 3 && d.Group != "chipset" && d.Group != "storage";
                        string pill = d.Generic ? L.T("تعريف عام", "Generic") : old ? L.T("قديم", "Old") : d.Inbox ? L.T("من ويندوز", "Windows") : L.T("من الشركة", "Maker's");
                        Color pf = d.Generic || old ? Theme.Warn : Theme.Ok, pb = d.Generic || old ? Theme.WarnBg : Theme.OkBg;
                        string vu = Drivers.VendorUrl(d.DeviceId);
                        int pw = TextRenderer.MeasureText(pill, Theme.Small).Width + 20;
                        if (paint)
                        {
                            using (var p = new Pen(Theme.Border)) g.DrawLine(p, pad, ry, W - pad, ry);
                            Theme.Txt(g, d.Name, Theme.Bold, Theme.Text, M(new Rectangle(pad, ry + 8, W - pad * 2 - pw - 220, 22)), Theme.Align.Start);
                            string det = (d.Provider.Length > 0 ? d.Provider : "?") + "  ·  " + d.Version + (d.Date > DateTime.MinValue ? "  ·  " + U.D(d.Date) : "");
                            TextRenderer.DrawText(g, det, Theme.Small, M(new Rectangle(pad, ry + 32, W - pad * 2 - pw - 220, 18)), Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));
                            var pr = M(new Rectangle(W - pad - pw, ry + 17, pw, 24));
                            Theme.FillRound(g, pb, pr, 12);
                            Theme.Txt(g, pill, Theme.Small, pf, pr, Theme.Align.Center);
                        }
                        // the fix link sits between the name and the label
                        if (d.Generic || old)
                        {
                            string vn = Drivers.VendorName(d.DeviceId);
                            string lt = vu.Length > 0 ? L.T("تعريف " + vn + " الرسمي", vn + " driver") : "Windows Update";
                            int linkW = TextRenderer.MeasureText(lt, Theme.Bold).Width + 4;
                            var lr = M(new Rectangle(W - pad - pw - 16 - linkW, ry + 18, linkW, 22));
                            if (paint) Theme.Txt(g, lt, Theme.Bold, Theme.Link, lr, Theme.Align.Center);
                            string url = vu;
                            hits.Add(new KeyValuePair<Rectangle, Action>(lr, () => { if (url.Length > 0) U.Open(url); else Drivers.OpenWindowsUpdate(); }));
                        }
                        ry += rh;
                    }
                    y += ch + 14;
                }
            }

            // safety note
            string note = L.T("لا تستخدم برامج «تحديث التعريفات» التجارية؛ كثير منها يثبّت برامج دعائية. W-Guard يدلّك على المصدر الرسمي فقط: Windows Update أو موقع الشركة المصنّعة.",
                "Avoid commercial \"driver updater\" tools - many install adware. W-Guard only points you to official sources: Windows Update or the hardware maker.");
            int nh2 = Theme.TextHeight(g, note, Theme.Body, W - 80) + 28;
            var nr = M(new Rectangle(0, y, W - 1, nh2));
            if (paint)
            {
                Theme.FillRound(g, Theme.WarnBg, nr, 12);
                TextRenderer.DrawText(g, Glyph.Warning, Theme.Icon, M(new Rectangle(14, y, 26, nh2)), Theme.Warn, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Theme.Txt(g, note, Theme.Body, Theme.Warn, M(new Rectangle(48, y + 14, W - 66, nh2 - 28)), Theme.Align.Start, false, true);
            }
            y += nh2 + 8;
            return y;
        }

        static string Age(DateTime d)
        {
            int days = (int)(DateTime.Now - d).TotalDays;
            if (days < 31) return L.T("أقل من شهر", "under a month");
            if (days < 365)
            {
                int m = days / 30;
                return L.T(m == 1 ? "شهر" : m == 2 ? "شهرين" : m <= 10 ? m + " أشهر" : m + " شهراً", m + (m == 1 ? " month" : " months"));
            }
            int y = days / 365;
            return L.T(y == 1 ? "أكثر من سنة" : y == 2 ? "أكثر من سنتين" : "أكثر من " + y + " سنوات", y + "+ year" + (y == 1 ? "" : "s"));
        }

        Action HitAt(Point p) { for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Key.Contains(p)) return hits[i].Value; return null; }
        protected override void OnMouseMove(MouseEventArgs e) { Cursor = HitAt(e.Location) != null ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { var a = HitAt(e.Location); if (a != null) a(); }
            base.OnMouseClick(e);
        }
    }
}
