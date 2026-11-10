// W-Guard - main window, settings and about pages, entry point
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
    class MainForm : Form
    {
        readonly NavBar nav;
        readonly HeaderBar header;
        readonly Panel body;
        readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        string currentId = "";

        public MainForm()
        {
            Text = AppInfo.Name + "  ·  © " + AppInfo.Year + " " + AppInfo.Owner + (Demo.On ? "  ·  DEMO" : "");
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = Theme.Body;
            BackColor = Theme.Bg;
            RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1320, 860);
            MinimumSize = new Size(1020, 660);
            AutoScaleMode = AutoScaleMode.None;

            nav = new NavBar { Dock = L.Ar ? DockStyle.Right : DockStyle.Left };
            nav.Items.Add(new NavItem("overview", Glyph.Home, L.T("نظرة عامة", "Overview")));
            nav.Items.Add(new NavItem(Scanners.Space, Glyph.Drive, L.T("المساحة", "Disk space")));
            nav.Items.Add(new NavItem(Scanners.Old, Glyph.History, L.T("ملفات قديمة", "Old files")));
            nav.Items.Add(new NavItem(Scanners.Games, Glyph.Game, L.T("الألعاب", "Games")));
            nav.Items.Add(new NavItem(Scanners.Programs, Glyph.Apps, L.T("البرامج", "Programs")));
            nav.Items.Add(new NavItem(Scanners.Security, Glyph.Shield, L.T("الأمان", "Security")));
            nav.Items.Add(new NavItem(Scanners.Background, Glyph.Pulse, L.T("الخلفية وبدء التشغيل", "Background & startup")));
            nav.Items.Add(new NavItem(Scanners.Cleanup, Glyph.Broom, L.T("التنظيف", "Cleanup")));
            nav.Items.Add(new NavItem(Scanners.Desktop, Glyph.Tiles, L.T("ترتيب سطح المكتب", "Desktop organizer")));
            nav.Items.Add(new NavItem("settings", Glyph.Settings, L.T("الإعدادات", "Settings")) { GapBefore = true });
            nav.Items.Add(new NavItem("about", Glyph.Info, L.T("عن البرنامج", "About")));
            nav.Navigate += Go;

            var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            header = new HeaderBar { Dock = DockStyle.Top };
            content.Controls.Add(body);
            content.Controls.Add(header);

            Controls.Add(content);
            Controls.Add(nav);

            pages["overview"] = new OverviewPage(this);
            foreach (string s in new[] { Scanners.Space, Scanners.Old, Scanners.Games, Scanners.Programs, Scanners.Security, Scanners.Background, Scanners.Cleanup, Scanners.Desktop })
                pages[s] = new ResultsPage(this, s);
            pages["settings"] = new SettingsPage(this);
            pages["about"] = new AboutPage(this);

            Go("overview");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // quick system read so the overview is filled right away (read-only)
            var th = new Thread(() =>
            {
                if (Demo.On) { Demo.Load(); try { BeginInvoke((Action)RefreshAll); if (Demo.Shots) BeginInvoke((Action)(() => Demo.TakeShots(this))); } catch { } return; }
                var info = SysInfo.Collect(null);
                if (Store.Info == null) Store.Info = info;
                try { BeginInvoke((Action)RefreshAll); } catch { }
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            if (Settings.CheckUpdates && !Demo.On) CheckUpdates(false);
        }

        public void CheckUpdates(bool manual)
        {
            Updater.CheckAsync(this, r =>
            {
                RefreshAll();
                if (manual || r.Tag != Settings.SkipVersion) ShowUpdate(r);
            }, msg =>
            {
                var sp = pages["settings"] as SettingsPage;
                if (sp != null) sp.SetUpdateStatus(msg);
                if (manual && msg == "latest") Msg.Info(this, L.T("أنت تستخدم أحدث إصدار ✓", "You are on the latest version ✓"));
                if (manual && msg.StartsWith("error:")) Msg.Show(this, L.T("تعذّر الاتصال بخادم التحديثات.\n", "Could not reach the update server.\n") + msg.Substring(6), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        public void ShowUpdate(ReleaseInfo r)
        {
            if (r == null) return;
            using (var f = new UpdateForm(r)) f.ShowDialog(this);
        }

        public void Go(string id)
        {
            Control page;
            if (!pages.TryGetValue(id, out page)) return;
            currentId = id;
            nav.Select(id);
            body.SuspendLayout();
            body.Controls.Clear();
            body.Controls.Add(page);
            body.ResumeLayout();
            var ip = (IPage)page;
            header.Set(ip.Title, ip.Subtitle, ip.HeaderActions);
            ip.OnShown();
        }

        public void RunScan(string section)
        {
            var steps = Demo.On ? Demo.Steps() : Scanners.StepsFor(section);
            if (steps.Count == 0) return;
            string title = section == Scanners.Overview ? L.T("جارٍ الفحص الشامل", "Running full scan") : L.T("جارٍ الفحص: ", "Scanning: ") + new ResultsPageTitle(section).Text;
            using (var f = new ScanForm(title, steps)) f.ShowDialog(this);
            RefreshAll();
        }

        public void SetBusy(bool b)
        {
            UseWaitCursor = b;
            nav.Enabled = !b;
            header.Enabled = !b;
        }

        public void RefreshBadges()
        {
            foreach (var it in nav.Items)
            {
                it.Badge = "";
                if (it.Id == Scanners.Security) { int n = Store.Count(Scanners.Security, 2); if (n > 0) { it.Badge = n.ToString(); it.BadgeColor = Theme.Danger; } }
                else if (it.Id == Scanners.Programs) { var l = Store.Get(Scanners.Programs); if (l != null) { int n = 0; foreach (var r in l) if (r.Level == 2) n++; if (n > 0) { it.Badge = n.ToString(); it.BadgeColor = Theme.Danger; } } }
                else if (it.Id == Scanners.Cleanup) { long t = Store.Total(Scanners.Cleanup); if (t > (1L << 30)) { it.Badge = U.GB(t) + " GB"; it.BadgeColor = Theme.Primary; } }
                else if (it.Id == "about" && Updater.Available != null) { it.Badge = L.T("جديد", "New"); it.BadgeColor = Theme.Primary; }
            }
            nav.Invalidate();
        }

        public void RefreshAll()
        {
            RefreshBadges();
            if (currentId.Length > 0) Go(currentId);
        }

        public void Restart()
        {
            try { Process.Start(Application.ExecutablePath, "--restart"); } catch { }
            Application.Exit();
        }
    }

    // ------------------------------------------------------------------ settings
    class SettingsPage : Panel, IPage
    {
        readonly MainForm main;
        readonly Label updStatus;

        public SettingsPage(MainForm owner)
        {
            main = owner;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            AutoScroll = true;
            Padding = new Padding(28, 0, 28, 20);

            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Color.White, Padding = new Padding(22, 16, 22, 16) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, t.Width - 1, t.Height - 1); };

            var lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Theme.Body };
            lang.Items.Add("العربية");
            lang.Items.Add("English");
            lang.SelectedIndex = L.Ar ? 0 : 1;
            lang.SelectedIndexChanged += (s, e) =>
            {
                string v = lang.SelectedIndex == 0 ? "ar" : "en";
                if (v == Settings.Lang) return;
                Settings.Lang = v; Settings.Save();
                if (Msg.Show(this, v == "ar" ? "سيُعاد تشغيل البرنامج لتطبيق اللغة العربية. متابعة؟" : "W-Guard will restart to switch to English. Continue?", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    main.Restart();
            };
            AddRow(t, L.T("اللغة / Language", "Language / اللغة"), lang);

            var upd = new CheckBox { Text = L.T("البحث عن تحديث عند كل تشغيل", "Check for updates at every start"), Checked = Settings.CheckUpdates, AutoSize = true, Font = Theme.Body };
            upd.CheckedChanged += (s, e) => { Settings.CheckUpdates = upd.Checked; Settings.Save(); };
            AddRow(t, L.T("التحديثات", "Updates"), upd);

            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var now = new FlatButton(L.T("تحقق الآن", "Check now"), FlatButton.Kind.Secondary, Glyph.Refresh);
            now.Click += (s, e) => { SetUpdateStatus(L.T("جارٍ التحقق...", "Checking...")); main.CheckUpdates(true); };
            updStatus = new Label { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, Margin = new Padding(12, 10, 0, 0) };
            flow.Controls.Add(now);
            flow.Controls.Add(updStatus);
            AddRow(t, "", flow);

            AddRow(t, L.T("الملف قديم إذا لم يُعدّل منذ (يوم)", "A file is old after (days)"), Num(30, 3650, Settings.OldDays, v => Settings.OldDays = v));
            AddRow(t, L.T("أقل حجم للملف القديم (MB)", "Minimum old file size (MB)"), Num(1, 100000, Settings.OldMinMB, v => Settings.OldMinMB = v));
            AddRow(t, L.T("الملف كبير إذا تجاوز (MB)", "A file is large above (MB)"), Num(50, 100000, Settings.BigMB, v => Settings.BigMB = v));

            var folders = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            var logs = new FlatButton(L.T("مجلد السجلات", "Logs folder"), FlatButton.Kind.Secondary, Glyph.Folder);
            logs.Click += (s, e) => U.Open(AppInfo.LogDir);
            var reps = new FlatButton(L.T("مجلد التقارير", "Reports folder"), FlatButton.Kind.Secondary, Glyph.Folder);
            reps.Click += (s, e) => U.Open(AppInfo.ReportDir);
            folders.Controls.Add(logs);
            folders.Controls.Add(reps);
            AddRow(t, L.T("الملفات", "Files"), folders);

            var note = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 70, Font = Theme.Small, ForeColor = Theme.Muted, Padding = new Padding(4, 14, 4, 0),
                Text = L.T("تُحفظ الإعدادات في: ", "Settings are saved in: ") + AppInfo.DataDir + "\n" + L.T("كل عملية حذف أو تعطيل تُسجَّل في السجل، ومدخلات السجل (Registry) تُنسخ احتياطياً قبل حذفها.", "Every delete/disable is logged, and registry values are backed up before removal.")
            };
            Controls.Add(note);
            Controls.Add(t);
        }

        void AddRow(TableLayoutPanel t, string label, Control c)
        {
            int r = t.RowCount;
            t.RowCount = r + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = label, AutoSize = true, Font = Theme.Bold, ForeColor = Theme.Text, Margin = new Padding(0, 14, 0, 14), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            c.Margin = new Padding(0, 8, 0, 8);
            t.Controls.Add(l, 0, r);
            t.Controls.Add(c, 1, r);
        }

        NumericUpDown Num(int min, int max, int val, Action<int> set)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, val)), Width = 120, Font = Theme.Body };
            n.ValueChanged += (s, e) => { set((int)n.Value); Settings.Save(); };
            return n;
        }

        public void SetUpdateStatus(string msg)
        {
            if (msg == "new") updStatus.Text = L.T("يتوفر إصدار جديد: ", "New version: ") + (Updater.Available != null ? Updater.Available.Tag : "");
            else if (msg == "latest") updStatus.Text = L.T("أنت على أحدث إصدار ✓", "You are up to date ✓");
            else if (msg.StartsWith("error:")) updStatus.Text = L.T("تعذّر التحقق (لا يوجد اتصال؟)", "Check failed (offline?)");
            else updStatus.Text = msg;
        }

        public string Title { get { return L.T("الإعدادات", "Settings"); } }
        public string Subtitle { get { return L.T("اللغة والتحديثات وحدود الفحص", "Language, updates and scan limits"); } }
        public Control[] HeaderActions { get { return new Control[0]; } }
        public void OnShown() { }
    }

    // ------------------------------------------------------------------ about
    class AboutPage : Panel, IPage
    {
        readonly MainForm main;
        Rectangle linkRect, ghRect;

        public AboutPage(MainForm owner)
        {
            main = owner;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public string Title { get { return L.T("عن البرنامج", "About"); } }
        public string Subtitle { get { return AppInfo.Name + " " + AppInfo.VersionText; } }
        public Control[] HeaderActions
        {
            get
            {
                var b = new FlatButton(L.T("البحث عن تحديث", "Check for updates"), FlatButton.Kind.Secondary, Glyph.Refresh);
                b.Click += (s, e) => main.CheckUpdates(true);
                return new Control[] { b };
            }
        }
        public void OnShown() { Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int W = Width;
            var card = new Rectangle(28, 0, Math.Min(W - 56, 860), 470);
            card = Theme.M(card, W);
            Theme.FillRound(g, Color.White, card, 14);
            Theme.StrokeRound(g, Theme.Border, card, 14);
            Func<Rectangle, Rectangle> M = r => Theme.M(new Rectangle(r.X + (L.Ar ? 0 : 0), r.Y, r.Width, r.Height), W);
            int x = 28 + 30;
            Logo.Draw(g, M(new Rectangle(x, 30, 96, 96)), Theme.Navy);
            int tx = x + 120, tw = Math.Min(W - 56, 860) - 150;
            Theme.Txt(g, AppInfo.Name, Theme.Huge, Theme.Text, M(new Rectangle(tx, 34, tw, 48)), Theme.Align.Start);
            Theme.Txt(g, L.T("فحص وتنظيف وحماية وترتيب الكمبيوتر", "Scan, clean, protect and organize your PC") + "  ·  " + L.T("الإصدار ", "Version ") + AppInfo.VersionText, Theme.Body, Theme.Muted, M(new Rectangle(tx, 84, tw, 24)), Theme.Align.Start);
            Theme.Txt(g, "© " + AppInfo.Year + " " + AppInfo.Owner + L.T(" - مفتوح المصدر بترخيص GPL-3.0", " - Open source under GPL-3.0"), Theme.Bold, Theme.Text, M(new Rectangle(tx, 112, tw, 24)), Theme.Align.Start);
            linkRect = M(new Rectangle(tx, 136, 160, 22));
            TextRenderer.DrawText(g, AppInfo.LinkText, Theme.Body, linkRect, Theme.Primary, TextFormatFlags.VerticalCenter | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));

            int y = 180;
            Theme.Txt(g, L.T("مبادئ الأمان في البرنامج", "Safety principles"), Theme.H2, Theme.Text, M(new Rectangle(x, y, tw + 120, 26)), Theme.Align.Start);
            y += 36;
            string[] items = {
                L.T("الفحص للقراءة فقط، ولا يُحذف أو يُعطَّل أي شيء قبل أن تختاره وتؤكّده.", "Scanning is read-only; nothing is deleted or disabled until you select and confirm it."),
                L.T("الحذف إلى سلة المحذوفات افتراضياً، ويرفض البرنامج لمس ملفات ويندوز والمجلدات الحساسة.", "Deletes go to the Recycle Bin by default; Windows files and sensitive folders are refused."),
                L.T("كل عملية تُسجَّل، ومدخلات السجل (Registry) تُنسخ احتياطياً قبل حذفها.", "Every action is logged; registry values are backed up before removal."),
                L.T("الاتصال الوحيد بالإنترنت هو التحقق من التحديثات على GitHub، ولا يُرسل أي بيانات عن جهازك.", "The only network use is the GitHub update check; no data about your PC is sent."),
                L.T("مفتوح المصدر بالكامل على GitHub - لا شيفرة مخفية، ويمكنك فحصه على VirusTotal في أي وقت.", "Fully open source on GitHub - no hidden code; check it on VirusTotal anytime.") };
            foreach (string s in items)
            {
                TextRenderer.DrawText(g, Glyph.Check, Theme.Icon, M(new Rectangle(x, y, 22, 26)), Theme.Primary, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                Theme.Txt(g, s, Theme.Body, Theme.Text, M(new Rectangle(x + 32, y, tw + 80, 26)), Theme.Align.Start);
                y += 34;
            }
            y += 10;
            ghRect = M(new Rectangle(x, y, 300, 22));
            TextRenderer.DrawText(g, "github.com/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName, Theme.Body, ghRect, Theme.Primary, TextFormatFlags.VerticalCenter | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));
        }

        protected override void OnMouseMove(MouseEventArgs e) { Cursor = (linkRect.Contains(e.Location) || ghRect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (linkRect.Contains(e.Location)) U.Open(AppInfo.Link);
            else if (ghRect.Contains(e.Location)) U.Open("https://github.com/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName + "/releases");
            base.OnMouseClick(e);
        }
    }

    // ------------------------------------------------------------------ entry point
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            Demo.Detect(args);
            using (var mtx = new Mutex(true, Demo.Shots ? "W-Guard.WAEL.DemoShots" : "W-Guard.WAEL.SingleInstance", out created))
            {
                if (!created)
                {
                    bool restart = args.Length > 0 && args[0] == "--restart";
                    bool got = false;
                    if (restart) { try { got = mtx.WaitOne(10000); } catch (AbandonedMutexException) { got = true; } }
                    if (!got) return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => { Log.Write("UI error: " + e.Exception); Msg.Show(null, e.Exception.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error); };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("fatal: " + e.ExceptionObject);
                Settings.Load();
                L.Ar = Settings.Lang == "ar";
                Updater.CleanupOld();
                Log.Write("===== W-Guard " + AppInfo.VersionText + " started (" + Settings.Lang + ")");
                Application.Run(new MainForm());
                Log.Write("===== closed");
            }
        }
    }
}
