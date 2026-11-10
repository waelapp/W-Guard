// W-Guard - scan window (count-up / count-down timer), update window, HTML report
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WGuard
{
    // ------------------------------------------------------------------ scan window
    class ScanForm : Form
    {
        readonly List<ScanStep> steps;
        readonly ScanCtx ctx = new ScanCtx();
        readonly Stopwatch sw = new Stopwatch();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly FlatButton stop;
        readonly string heading;
        Thread worker;
        volatile int current;
        volatile bool finished;
        double shownRemaining = -1, lastTick;
        double demoElapsed = -1;
        bool frozen;

        // demo screenshots: show a fixed moment of the scan without running it
        public void DemoFreeze(int cur, double stepProgress, double elapsed, double remaining, long files, string path)
        {
            frozen = true;
            current = cur;
            ctx.StepProgress = stepProgress;
            ctx.Files = files;
            ctx.Current = path;
            demoElapsed = elapsed;
            shownRemaining = remaining;
            double total = 0, done = 0;
            for (int i = 0; i < steps.Count; i++) { total += steps[i].Weight; if (i < cur) done += steps[i].Weight; }
            done += steps[cur].Weight * stepProgress;
            progress = total > 0 ? done / total : 0;
        }

        public void ForceClose() { finished = true; Close(); }
        double progress;

        public ScanForm(string title, List<ScanStep> list)
        {
            steps = list;
            heading = title;
            Text = AppInfo.Name + " - " + title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = Theme.Body;
            RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No;
            ClientSize = new Size(640, 384 + steps.Count * 34);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            stop = new FlatButton(L.T("إيقاف الفحص", "Stop scan"), FlatButton.Kind.Secondary, Glyph.Stop);
            stop.Location = Theme.M(new Rectangle(32, ClientSize.Height - 62, stop.Width, 38), ClientSize.Width).Location;
            stop.Click += (s, e) => RequestStop();
            Controls.Add(stop);

            timer.Interval = 250;
            timer.Tick += (s, e) => OnTick();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (frozen) return;
            sw.Start();
            timer.Start();
            worker = new Thread(Work) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        void Work()
        {
            Log.Write("scan start: " + heading);
            for (int i = 0; i < steps.Count; i++)
            {
                if (ctx.Cancel) break;
                current = i;
                ctx.StepProgress = 0;
                try { steps[i].Run(ctx); }
                catch (Exception ex) { Log.Write("step failed: " + steps[i].Title + " | " + ex); }
            }
            current = steps.Count;
            finished = true;
            Log.Write("scan end: " + heading + " | files=" + ctx.Files + " | " + U.Clock(sw.Elapsed.TotalSeconds) + (ctx.Cancel ? " (stopped)" : ""));
            try { BeginInvoke((Action)(() => { timer.Stop(); DialogResult = ctx.Cancel ? DialogResult.Cancel : DialogResult.OK; Close(); })); } catch { }
        }

        void RequestStop()
        {
            ctx.Cancel = true;
            stop.Enabled = false;
            stop.Text = L.T("جارٍ الإيقاف...", "Stopping...");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!finished) { e.Cancel = true; RequestStop(); }
            base.OnFormClosing(e);
        }

        void OnTick()
        {
            double total = steps.Sum(s => s.Weight);
            double done = 0;
            int cur = current;
            for (int i = 0; i < cur && i < steps.Count; i++) done += steps[i].Weight;
            if (cur < steps.Count) done += steps[cur].Weight * Math.Max(0, Math.Min(1, ctx.StepProgress));
            progress = total > 0 ? done / total : 0;

            double el = sw.Elapsed.TotalSeconds;
            double dt = el - lastTick; lastTick = el;
            if (progress > 0.03 && el > 3)
            {
                double est = el * (1 - progress) / progress;
                if (shownRemaining < 0) shownRemaining = est;
                else
                {
                    shownRemaining = Math.Max(0, shownRemaining - dt);   // keeps counting down
                    shownRemaining += (est - shownRemaining) * 0.12;     // corrected gently toward the estimate
                }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int W = ClientSize.Width;
            Func<Rectangle, Rectangle> M = r => Theme.M(r, W);

            var ic = M(new Rectangle(32, 28, 48, 48));
            Logo.Draw(g, ic, Theme.Navy);
            int cur = Math.Min(current, steps.Count - 1);
            Theme.Txt(g, heading, Theme.H1, Theme.Text, M(new Rectangle(94, 26, W - 130, 30)), Theme.Align.Start);
            Theme.Txt(g, (finished ? L.T("اكتمل", "Completed") : L.T("الآن: ", "Now: ") + (cur >= 0 ? steps[cur].Title : "")), Theme.Body, Theme.Muted, M(new Rectangle(94, 56, W - 130, 22)), Theme.Align.Start);

            // timers
            int bw = (W - 64 - 14) / 2;
            var b1 = new Rectangle(32, 98, bw, 86);
            var b2 = new Rectangle(32 + bw + 14, 98, bw, 86);
            Theme.FillRound(g, Theme.Bg, M(b1), 14);
            Theme.FillRound(g, Theme.TealTint, M(b2), 14);
            Theme.Txt(g, L.T("الوقت المنقضي", "Elapsed"), Theme.Small, Theme.Muted, M(new Rectangle(b1.X + 16, b1.Y + 12, bw - 32, 20)), Theme.Align.Start);
            Theme.Txt(g, L.T("الوقت المتبقي تقريباً", "Time remaining (approx.)"), Theme.Small, Theme.Muted, M(new Rectangle(b2.X + 16, b2.Y + 12, bw - 32, 20)), Theme.Align.Start);
            DrawClock(g, U.Clock(demoElapsed >= 0 ? demoElapsed : sw.Elapsed.TotalSeconds), Theme.Text, M(new Rectangle(b1.X + 16, b1.Y + 34, bw - 32, 44)));
            DrawClock(g, shownRemaining < 0 ? (L.Ar ? "تقدير..." : "estimating...") : U.Clock(shownRemaining), Theme.Primary, M(new Rectangle(b2.X + 16, b2.Y + 34, bw - 32, 44)));

            // progress
            int py = 204;
            Theme.Txt(g, ((int)(progress * 100)) + "%", Theme.Bold, Theme.Text, M(new Rectangle(32, py, 80, 22)), Theme.Align.Start);
            Theme.Txt(g, U.Num(ctx.Files) + L.T(" ملف", " files"), Theme.Small, Theme.Muted, M(new Rectangle(W / 2, py, W / 2 - 32, 22)), Theme.Align.End);
            var track = new Rectangle(32, py + 28, W - 64, 12);
            Theme.FillRound(g, Theme.Track, M(track), 6);
            if (progress > 0) Theme.FillRound(g, Theme.Primary, M(new Rectangle(track.X, track.Y, Math.Max(12, (int)(track.Width * progress)), track.Height)), 6);
            string path = ctx.Current ?? "";
            TextRenderer.DrawText(g, path, Theme.Small, M(new Rectangle(32, py + 46, W - 64, 20)), Theme.Faint,
                TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));

            // steps
            int sy = py + 80;
            for (int i = 0; i < steps.Count; i++)
            {
                bool done = i < current, now = i == current && !finished;
                var dot = M(new Rectangle(32, sy + 3, 24, 24));
                using (var b = new SolidBrush(done ? Theme.Ok : now ? Theme.Primary : Theme.Track)) g.FillEllipse(b, dot);
                if (done) TextRenderer.DrawText(g, Glyph.Check, Theme.IconSmall, dot, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                else TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Small, dot, now ? Color.White : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Theme.Txt(g, steps[i].Title, now ? Theme.Bold : Theme.Body, done ? Theme.Muted : now ? Theme.Text : Theme.Faint, M(new Rectangle(68, sy, W - 100, 30)), Theme.Align.Start);
                sy += 34;
            }
            Theme.Txt(g, L.T("الفحص للقراءة فقط - لن يُحذف أو يتغير أي شيء دون تأكيدك", "Scanning is read-only - nothing is changed without your confirmation"), Theme.Small, Theme.Muted,
                M(new Rectangle(stop.Width + 48, ClientSize.Height - 62, W - stop.Width - 80, 38)), Theme.Align.Start);
        }

        static readonly Font ClockFont = new Font("Segoe UI Semibold", 24f);
        static void DrawClock(Graphics g, string text, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, text, ClockFont, r, c, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));
        }
    }

    // ------------------------------------------------------------------ update window (two steps: download from GitHub, then run it)
    class UpdateForm : Form
    {
        readonly ReleaseInfo rel;
        readonly bool installed;

        public UpdateForm(ReleaseInfo r)
        {
            rel = r;
            installed = Updater.IsInstalled;
            Text = AppInfo.Name + " - " + L.T("تحديث", "Update");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = Theme.Body;
            RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No;
            ClientSize = new Size(600, 520);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var notes = new TextBox
            {
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ScrollBars = ScrollBars.Vertical,
                Font = Theme.Body, ForeColor = Theme.Text, Text = string.IsNullOrEmpty(r.Notes) ? L.T("تحسينات وإصلاحات.", "Improvements and fixes.") : r.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                Bounds = new Rectangle(42, 118, 516, 120)
            };
            Controls.Add(notes);

            var go = new FlatButton(L.T("تنزيل التحديث", "Download update"), FlatButton.Kind.Primary, Glyph.Download);
            var later = new FlatButton(L.T("لاحقاً", "Later"), FlatButton.Kind.Secondary, "");
            var skip = new LinkLabel { Text = L.T("تخطّي هذا الإصدار", "Skip this version"), AutoSize = true, LinkColor = Theme.Muted, LinkBehavior = LinkBehavior.HoverUnderline };
            go.Location = Theme.M(new Rectangle(32, 456, go.Width, 38), 600).Location;
            later.Location = Theme.M(new Rectangle(32 + go.Width + 10, 456, later.Width, 38), 600).Location;
            skip.Location = Theme.M(new Rectangle(600 - 32 - 140, 466, 140, 20), 600).Location;
            go.Click += (s, e) =>
            {
                U.Open(Updater.DownloadUrl(rel, installed));
                Log.Write("update download opened in browser: " + rel.Tag);
                Close();
            };
            later.Click += (s, e) => Close();
            skip.LinkClicked += (s, e) => { Settings.SkipVersion = rel.Tag; Settings.Save(); Close(); };
            Controls.Add(go); Controls.Add(later); Controls.Add(skip);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int W = ClientSize.Width;
            Func<Rectangle, Rectangle> M = r => Theme.M(r, W);
            Logo.Draw(g, M(new Rectangle(32, 26, 48, 48)), Theme.Navy);
            Theme.Txt(g, L.T("يتوفر إصدار جديد", "A new version is available"), Theme.H1, Theme.Text, M(new Rectangle(94, 24, W - 126, 30)), Theme.Align.Start);
            TextRenderer.DrawText(g, AppInfo.VersionText + "  →  " + rel.Version.ToString(3) + (rel.Date.Length > 0 ? "   ·   " + rel.Date : ""), Theme.Body, M(new Rectangle(94, 54, W - 126, 22)), Theme.Muted,
                TextFormatFlags.VerticalCenter | (L.Ar ? TextFormatFlags.Right : TextFormatFlags.Left));
            Theme.Txt(g, L.T("ما الجديد", "What's new"), Theme.Bold, Theme.Text, M(new Rectangle(32, 88, W - 64, 22)), Theme.Align.Start);
            Theme.FillRound(g, Theme.Bg, M(new Rectangle(32, 112, W - 64, 132)), 12);

            // the two steps
            Theme.Txt(g, L.T("التحديث في خطوتين", "Update in two steps"), Theme.Bold, Theme.Text, M(new Rectangle(32, 258, W - 64, 22)), Theme.Align.Start);
            string s1 = L.T("اضغط «تنزيل التحديث» - يُنزَّل الملف من صفحة البرنامج الرسمية على GitHub في متصفحك.", "Press “Download update” - your browser downloads it from the official GitHub page.");
            string s2 = installed
                ? L.T("شغّل الملف المنزّل W-Guard-Setup.exe - يغلق W-Guard ويثبّت الإصدار الجديد مكانه مع الإبقاء على إعداداتك.", "Run the downloaded W-Guard-Setup.exe - it closes W-Guard and installs the new version, keeping your settings.")
                : L.T("أغلق W-Guard وضع الملف الجديد W-Guard.exe مكان القديم، ثم شغّله.", "Close W-Guard, put the new W-Guard.exe in place of the old one, then run it.");
            Step(g, 1, s1, 286, W);
            Step(g, 2, s2, 330, W);

            var note = new Rectangle(32, 384, W - 64, 52);
            Theme.FillRound(g, Theme.OkBg, M(note), 10);
            TextRenderer.DrawText(g, Glyph.Shield, Theme.Icon, M(new Rectangle(note.X + 10, note.Y, 26, note.Height)), Theme.Ok, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Txt(g, L.T("نزّل التحديثات من صفحة البرنامج الرسمية فقط. البرنامج لا يرسل أي بيانات عن جهازك، ويمكنك إيقاف التحقق من التحديثات من الإعدادات.",
                "Only download updates from the official page. W-Guard sends no data about your PC, and you can turn off update checks in Settings."),
                Theme.Small, Theme.Hex("1F4D36"), M(new Rectangle(note.X + 40, note.Y + 4, note.Width - 52, note.Height - 8)), Theme.Align.Start, true, true);
        }

        static void Step(Graphics g, int n, string text, int y, int W)
        {
            var dot = Theme.M(new Rectangle(32, y + 6, 24, 24), W);
            using (var b = new SolidBrush(Theme.Primary)) g.FillEllipse(b, dot);
            TextRenderer.DrawText(g, n.ToString(), Theme.Small, dot, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Theme.Txt(g, text, Theme.Body, Theme.Text, Theme.M(new Rectangle(66, y, W - 98, 40), W), Theme.Align.Start, true, true);
        }
    }

    // ------------------------------------------------------------------ HTML report
    static class Report
    {
        static string E(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        public static void Export(IWin32Window owner)
        {
            var sb = new StringBuilder();
            string dir = L.Ar ? "rtl" : "ltr";
            sb.Append("<!doctype html><html dir=\"" + dir + "\" lang=\"" + (L.Ar ? "ar" : "en") + "\"><head><meta charset=\"utf-8\"><title>W-Guard - " + E(Demo.PcName) + "</title><style>");
            sb.Append("body{font-family:'Segoe UI',Tahoma,sans-serif;background:#F3F5F8;color:#111827;margin:0;padding:32px}h1{margin:0 0 4px}p.m{color:#5B6577;margin:0 0 24px}");
            sb.Append("table{border-collapse:collapse;width:100%;background:#fff;margin-bottom:28px;border:1px solid #E3E7EE}th,td{border-bottom:1px solid #E3E7EE;padding:8px 10px;text-align:start;font-size:13px;vertical-align:top}");
            sb.Append("th{background:#111827;color:#fff}tr.w td{background:#FFF4E5}tr.d td{background:#FDECEB}h2{margin:28px 0 10px;font-size:18px}.k{color:#5B6577;width:180px}td.p{color:#5B6577;word-break:break-all}");
            sb.Append("footer{color:#8892A4;font-size:12px;margin-top:20px}a{color:#08876A}</style></head><body>");
            sb.Append("<h1>W-Guard · " + E(L.T("تقرير الجهاز", "PC report")) + "</h1><p class=\"m\">" + E(Demo.PcName) + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + "</p>");
            var info = Store.Info;
            List<Alert> alerts;
            int score = OverviewPage.Score(out alerts);
            sb.Append("<h2>" + E(L.T("صحة الجهاز", "PC health")) + ": " + score + " / 100</h2><table>");
            foreach (var a in alerts) sb.Append("<tr class=\"" + (a.Level == 2 ? "d" : a.Level == 1 ? "w" : "") + "\"><td>" + E(a.Text) + "</td></tr>");
            sb.Append("</table>");
            if (info != null)
            {
                sb.Append("<h2>" + E(L.T("المواصفات", "Specs")) + "</h2><table>");
                Action<string, string> kv = (kk, vv) => sb.Append("<tr><td class=\"k\">" + E(kk) + "</td><td>" + E(vv) + "</td></tr>");
                kv(L.T("النظام", "Windows"), info.OS + " · Build " + info.Build);
                kv(L.T("المعالج", "Processor"), info.Cpu);
                foreach (var gpu in info.Gpus) kv(L.T("كرت الشاشة", "Graphics"), gpu);
                kv(L.T("الذاكرة", "Memory"), Math.Round(info.RamBytes / 1073741824.0) + " GB · " + info.RamSpeed + " MHz · " + info.RamPart);
                kv(L.T("اللوحة الأم", "Motherboard"), info.Board + " · BIOS " + info.Bios);
                foreach (var d in info.Disks) kv(L.T("قرص", "Disk"), d);
                foreach (var v in info.Volumes) kv(v.Letter + ":", L.T("حر ", "Free ") + U.GB(v.Free) + " / " + U.GB(v.Size) + " GB " + v.Media);
                kv("Defender", info.DefenderKnown ? (info.RealTime ? L.T("الحماية الفورية تعمل", "Real-time on") : L.T("الحماية الفورية متوقفة", "Real-time OFF")) : "—");
                sb.Append("</table>");
            }
            var names = new[] { Scanners.Security, Scanners.Programs, Scanners.Background, Scanners.Cleanup, Scanners.Games, Scanners.Space, Scanners.Old, Scanners.Desktop };
            foreach (string s in names)
            {
                var rows = Store.Get(s);
                if (rows == null || rows.Count == 0) continue;
                sb.Append("<h2>" + E(new ResultsPageTitle(s).Text) + " (" + rows.Count + ")</h2><table><tr><th>" + E(L.T("الاسم", "Name")) + "</th><th>" + E(L.T("التفاصيل", "Details")) + "</th><th>" + E(L.T("الحجم", "Size")) + "</th><th>" + E(L.T("التاريخ", "Date")) + "</th><th>" + E(L.T("تنبيه", "Warning")) + "</th><th>" + E(L.T("المسار", "Path")) + "</th></tr>");
                foreach (var r in rows.Take(400))
                    sb.Append("<tr class=\"" + (r.Level == 2 ? "d" : r.Level == 1 ? "w" : "") + "\"><td>" + E(r.Name) + "</td><td>" + E(r.Detail) + "</td><td>" + E(U.Size(r.Bytes)) + "</td><td>" + E(r.Date) + "</td><td>" + E(r.Risk) + "</td><td class=\"p\">" + E(r.Path) + "</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("<footer>W-Guard " + AppInfo.VersionText + " · © " + AppInfo.Year + " " + AppInfo.Owner + " · <a href=\"" + AppInfo.Link + "\">" + AppInfo.LinkText + "</a></footer></body></html>");
            string file = Path.Combine(AppInfo.ReportDir, "W-Guard-report-" + DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".html");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
            Log.Write("report: " + file);
            U.Open(file);
        }
    }

    class ResultsPageTitle
    {
        public string Text;
        public ResultsPageTitle(string s)
        {
            switch (s)
            {
                case Scanners.Space: Text = L.T("المساحة", "Disk space"); break;
                case Scanners.Old: Text = L.T("ملفات قديمة", "Old files"); break;
                case Scanners.Games: Text = L.T("الألعاب", "Games"); break;
                case Scanners.Programs: Text = L.T("البرامج", "Programs"); break;
                case Scanners.Security: Text = L.T("الأمان", "Security"); break;
                case Scanners.Background: Text = L.T("الخلفية وبدء التشغيل", "Background & startup"); break;
                case Scanners.Cleanup: Text = L.T("التنظيف", "Cleanup"); break;
                case Scanners.Desktop: Text = L.T("ترتيب سطح المكتب", "Desktop organizer"); break;
                default: Text = s; break;
            }
        }
    }
}
