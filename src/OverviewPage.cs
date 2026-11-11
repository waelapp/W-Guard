// W-Guard - overview dashboard (custom painted)
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace WGuard
{
    class Alert
    {
        public int Level; // 2 danger, 1 warn, 0 info, 3 ok
        public string Text, Target;
        public Alert(int level, string text, string target) { Level = level; Text = text; Target = target; }
    }

    class OverviewPage : Panel, IPage
    {
        readonly MainForm main;
        readonly Canvas canvas;
        Control[] actions;

        public OverviewPage(MainForm owner)
        {
            main = owner;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            BackColor = Theme.Bg;
            canvas = new Canvas(this);
            Controls.Add(canvas);
            var scan = new FlatButton(L.T("فحص شامل", "Full scan"), FlatButton.Kind.Primary, Glyph.Search);
            scan.Click += (s, e) => main.RunScan(Scanners.Overview);
            var rep = new FlatButton(L.T("حفظ تقرير", "Save report"), FlatButton.Kind.Secondary, Glyph.Save);
            rep.Click += (s, e) => Report.Export(this);
            var look = new FlatButton(Theme.Dark ? L.T("الوضع الفاتح", "Light mode") : L.T("الوضع الداكن", "Dark mode"), FlatButton.Kind.Secondary, Theme.Dark ? "\uE706" : "\uE708");
            look.Click += (s, e) => main.SetDark(!Theme.Dark);
            actions = new Control[] { look, rep, scan };
            Resize += (s, e) => canvas.Relayout();
            SizeChanged += (s, e) => { if (IsHandleCreated) BeginInvoke((Action)canvas.Relayout); };
        }

        public string Title { get { return L.T("نظرة عامة", "Overview"); } }
        public string Subtitle
        {
            get
            {
                string pc = Demo.PcName;
                if (Store.LastFullScan == DateTime.MinValue) return pc + "  ·  " + L.T("لم يُجرَ فحص شامل بعد", "No full scan yet");
                int d = Store.DaysSinceFull;
                string ago = d <= 0 ? L.T("اليوم", "today") : d == 1 ? L.T("أمس", "yesterday") : L.T("منذ " + d + " " + (d <= 10 ? "أيام" : "يوماً"), d + " days ago");
                return pc + "  ·  " + L.T("آخر فحص شامل: ", "Last full scan: ") + Store.LastFullScan.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " (" + ago + ")";
            }
        }
        public Control[] HeaderActions { get { return actions; } }
        public void OnShown() { canvas.Relayout(); canvas.Invalidate(); }
        public void RefreshView() { canvas.Relayout(); canvas.Invalidate(); }

        // ------------------------------------------------ health
        public static int Score(out List<Alert> alerts)
        {
            alerts = new List<Alert>();
            int score = 100;
            var info = Store.Info;
            if (info != null)
            {
                if (info.DefenderKnown && !info.RealTime) { score -= 25; alerts.Add(new Alert(2, L.T("الحماية الفورية في Microsoft Defender متوقفة", "Microsoft Defender real-time protection is off"), "defender")); }
                if (info.DefenderKnown && info.SigUpdated > DateTime.MinValue && (DateTime.Now - info.SigUpdated).TotalDays > 7) { score -= 8; alerts.Add(new Alert(1, L.T("تعريفات الحماية قديمة (أكثر من أسبوع)", "Protection definitions are over a week old"), "defender")); }
                foreach (var v in info.Volumes)
                {
                    if (v.Size > 0 && (double)v.Free / v.Size < 0.10) { score -= 10; alerts.Add(new Alert(1, L.T("القرص ", "Drive ") + v.Letter + L.T(": المساحة الحرة أقل من 10%", ": less than 10% free space"), Scanners.Space)); }
                }
                if (info.RamRated > 0 && info.RamSpeed > 0 && info.RamRated > info.RamSpeed + 200)
                    alerts.Add(new Alert(1, L.T("الرام مصنّفة " + info.RamRated + " وتعمل على " + info.RamSpeed + " MHz - فعّل XMP/EXPO أو اضبط السرعة يدوياً من BIOS", "RAM is rated " + info.RamRated + " but runs at " + info.RamSpeed + " MHz - enable XMP/EXPO or set it manually in BIOS"), ""));
                if (info.HasHdd) alerts.Add(new Alert(0, L.T("يوجد قرص ميكانيكي (HDD) - لا تضع عليه الألعاب أو النظام", "A mechanical disk (HDD) is present - keep games and Windows off it"), ""));
                if (info.Uptime.TotalDays > 7) alerts.Add(new Alert(0, L.T("الجهاز يعمل منذ ", "Running for ") + (int)info.Uptime.TotalDays + L.T(" يوماً دون إعادة تشغيل", " days without a restart"), ""));
            }
            int sec = Store.Count(Scanners.Security, 2);
            if (sec > 0) { score -= Math.Min(30, sec * 8); alerts.Add(new Alert(2, sec + L.T(" عنصر خطير في التشغيل التلقائي/المهام", " dangerous autostart/task items"), Scanners.Security)); }
            int warnSec = Store.Count(Scanners.Security, 1) - sec;
            if (warnSec > 0) { score -= Math.Min(10, warnSec * 2); alerts.Add(new Alert(1, warnSec + L.T(" عنصر مشبوه أو بقايا في التشغيل التلقائي", " suspicious or leftover autostart items"), Scanners.Security)); }
            var progs = Store.Get(Scanners.Programs);
            if (progs != null)
            {
                int pup = progs.Count(r => r.Level == 2);
                if (pup > 0) { score -= Math.Min(12, pup * 4); alerts.Add(new Alert(2, pup + L.T(" برنامج دعائي/غير مرغوب مثبت", " adware / unwanted programs installed"), Scanners.Programs)); }
                int left = progs.Count(r => r.Action == "RemoveEntry");
                if (left > 0) alerts.Add(new Alert(0, left + L.T(" بقايا برامج في قائمة التثبيت", " leftover program entries"), Scanners.Programs));
            }
            long clean = Store.Total(Scanners.Cleanup);
            if (clean > (1L << 30)) { score -= clean > (5L << 30) ? 6 : 3; alerts.Add(new Alert(0, U.Size(clean) + L.T(" ملفات مؤقتة يمكن تنظيفها بأمان", " of temporary files can be cleared safely"), Scanners.Cleanup)); }
            long old = Store.Total(Scanners.Old);
            if (old > (1L << 30)) alerts.Add(new Alert(0, U.Size(old) + L.T(" ملفات قديمة لم تُفتح منذ مدة طويلة", " of old files not touched for a long time"), Scanners.Old));
            var bg = Store.Get(Scanners.Background);
            if (bg != null)
            {
                int st = bg.Count(r => r.Action == "DisableStartup");
                if (st > 6) { score -= 4; alerts.Add(new Alert(1, st + L.T(" برنامج يقلع مع ويندوز - تعطيل غير الضروري يسرّع الإقلاع", " apps start with Windows - disabling extras speeds up boot"), Scanners.Background)); }
            }
            if (alerts.Count == 0) alerts.Add(new Alert(3, L.T("لا توجد مشاكل - جهازك بحالة ممتازة", "No issues - your PC is in great shape"), ""));
            return Math.Max(0, Math.Min(100, score));
        }

        // ------------------------------------------------ painted surface
        class Canvas : Control
        {
            readonly OverviewPage page;
            readonly List<KeyValuePair<Rectangle, string>> hot = new List<KeyValuePair<Rectangle, string>>();
            const int Pad = 28, GapW = 18;

            public Canvas(OverviewPage p)
            {
                page = p;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                Location = new Point(0, 0);
            }

            public void Relayout()
            {
                int w = page.ClientSize.Width;
                if (w < 200) return;
                int h = Measure(w);
                Size = new Size(w, Math.Max(h, page.ClientSize.Height));
                Location = new Point(0, page.AutoScrollPosition.Y);
                Invalidate();
            }

            int Measure(int w)
            {
                using (var bmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bmp)) return Draw(g, w, false);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                hot.Clear();
                Draw(e.Graphics, Width, true);
            }

            void Card(Graphics g, Rectangle r)
            {
                Theme.FillRound(g, Theme.Card, r, 14);
                Theme.StrokeRound(g, Theme.Border, r, 14);
            }

            Rectangle M(Rectangle r) { return Theme.M(r, Width); }

            int Draw(Graphics g, int W, bool paint)
            {
                int y = 0;
                int inner = W - Pad * 2;
                int cols = inner >= 900 ? 3 : 1;
                int cw = (inner - GapW * (cols - 1)) / cols;
                var info = Store.Info;
                List<Alert> alerts;
                int score = Score(out alerts);

                // update banner
                if (Updater.Available != null)
                {
                    var br = new Rectangle(Pad, y, inner, 56);
                    if (paint)
                    {
                        Theme.FillRound(g, Theme.TealTint, M(br), 12);
                        TextRenderer.DrawText(g, Glyph.Download, Theme.Icon, M(new Rectangle(br.X + 16, br.Y, 24, br.Height)), Theme.Primary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        Theme.Txt(g, L.T("يتوفر إصدار جديد: ", "A new version is available: ") + Updater.Available.Tag, Theme.Bold, Theme.Text, M(new Rectangle(br.X + 48, br.Y, br.Width - 220, br.Height)), Theme.Align.Start);
                        var bb = M(new Rectangle(br.Right - 150, br.Y + 10, 136, 36));
                        Theme.FillRound(g, Theme.Primary, bb, 10);
                        Theme.Txt(g, L.T("عرض التحديث", "View update"), Theme.Bold, Color.White, bb, Theme.Align.Center);
                        hot.Add(new KeyValuePair<Rectangle, string>(M(br), "update"));
                    }
                    y += 56 + GapW;
                }

                // full-scan call to action (until the first full scan)
                bool prelim = Store.LastFullScan == DateTime.MinValue;
                if (prelim)
                {
                    var fb = new Rectangle(Pad, y, inner, 92);
                    if (paint)
                    {
                        Theme.FillRound(g, Theme.Navy, M(fb), 14);
                        var lg = new Rectangle(fb.X + 22, fb.Y + 20, 52, 52);
                        Logo.Draw(g, M(lg), Theme.Navy2);
                        int btnW = 210;
                        var bt = new Rectangle(fb.Right - 22 - btnW, fb.Y + 24, btnW, 44);
                        Theme.Txt(g, L.T("لم يُجرَ الفحص الشامل بعد - التقييم الحالي مبدئي", "No full scan yet - the current rating is preliminary"), Theme.H2, Color.White, M(new Rectangle(lg.Right + 16, fb.Y + 16, bt.X - lg.Right - 32, 28)), Theme.Align.Start);
                        Theme.Txt(g, L.T("التقييم الآن يعتمد على الحماية والأقراص فقط. الفحص الشامل يفحص الملفات والبرامج والأمان والخلفية ثم يعطيك التقييم الكامل - وهو للقراءة فقط ولا يغيّر شيئاً.",
                            "Right now the rating only covers protection and drives. A full scan checks files, programs, security and background apps, then gives the complete rating - read-only, nothing is changed."),
                            Theme.Small, Theme.NavText, M(new Rectangle(lg.Right + 16, fb.Y + 46, bt.X - lg.Right - 32, 40)), Theme.Align.Start, false, true);
                        Theme.FillRound(g, Theme.Teal, M(bt), 12);
                        TextRenderer.DrawText(g, Glyph.Search, Theme.Icon, M(new Rectangle(bt.X + 18, bt.Y, 22, bt.Height)), Theme.Navy, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                        Theme.Txt(g, L.T("ابدأ الفحص الشامل", "Start full scan"), Theme.Bold, Theme.Navy, M(new Rectangle(bt.X + 40, bt.Y, bt.Width - 52, bt.Height)), Theme.Align.Center);
                        hot.Add(new KeyValuePair<Rectangle, string>(M(fb), "scan"));
                    }
                    y += 92 + GapW;
                }
                else if (Store.ScanIsOld)
                {
                    // the saved results are getting old: suggest a fresh scan without forcing it
                    var ob = new Rectangle(Pad, y, inner, 64);
                    if (paint)
                    {
                        Theme.FillRound(g, Theme.WarnBg, M(ob), 12);
                        TextRenderer.DrawText(g, Glyph.Search, Theme.Icon, M(new Rectangle(ob.X + 16, ob.Y, 24, ob.Height)), Theme.Warn, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        int ds = Store.DaysSinceFull;
                        Theme.Txt(g, L.T("مرّ " + ds + " " + (ds <= 10 ? "أيام" : "يوماً") + " على آخر فحص شامل - ننصح بفحص جديد لتحديث النتائج", ds + " days since the last full scan - a new scan is recommended to refresh the results"),
                            Theme.Bold, Theme.Warn, M(new Rectangle(ob.X + 48, ob.Y, ob.Width - 230, ob.Height)), Theme.Align.Start, true, true);
                        var bb = M(new Rectangle(ob.Right - 170, ob.Y + 13, 156, 38));
                        Theme.FillRound(g, Theme.Primary, bb, 10);
                        Theme.Txt(g, L.T("افحص الآن", "Scan now"), Theme.Bold, Color.White, bb, Theme.Align.Center);
                        hot.Add(new KeyValuePair<Rectangle, string>(M(ob), "scan"));
                    }
                    y += 64 + GapW;
                }

                // ---- row 1: health / freeable / protection
                int h1 = 176;
                var c1 = new Rectangle(Pad, y, cw, h1);
                var c2 = cols == 3 ? new Rectangle(Pad + cw + GapW, y, cw, h1) : new Rectangle(Pad, y + h1 + GapW, cw, h1);
                var c3 = cols == 3 ? new Rectangle(Pad + (cw + GapW) * 2, y, cw, h1) : new Rectangle(Pad, y + (h1 + GapW) * 2, cw, h1);
                if (paint)
                {
                    // health ring
                    Card(g, M(c1));
                    var ring = new Rectangle(c1.X + 22, c1.Y + 28, 120, 120);
                    Color sc = prelim ? Theme.Faint : score >= 80 ? Theme.Ok : score >= 55 ? Theme.WarnDot : Theme.Danger;
                    var rr = M(ring);
                    using (var pen = new Pen(Theme.Track, 12)) g.DrawArc(pen, rr.X + 6, rr.Y + 6, rr.Width - 12, rr.Height - 12, 0, 360);
                    using (var pen = new Pen(sc, 12) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, rr.X + 6, rr.Y + 6, rr.Width - 12, rr.Height - 12, -90, Math.Max(1, 360f * score / 100f));
                    Theme.Txt(g, prelim ? "?" : score.ToString(), Theme.Big, prelim ? Theme.Faint : Theme.Text, rr, Theme.Align.Center);
                    var tx = new Rectangle(ring.Right + 18, c1.Y + 40, c1.Right - ring.Right - 34, 30);
                    Theme.Txt(g, L.T("صحة الجهاز", "PC health"), Theme.Small, Theme.Muted, M(tx), Theme.Align.Start);
                    string verdict = prelim ? L.T("بانتظار الفحص الشامل", "Waiting for full scan") : score >= 80 ? L.T("ممتازة", "Great") : score >= 55 ? L.T("تحتاج انتباه", "Needs attention") : L.T("تحتاج تدخل", "Needs action");
                    Theme.Txt(g, verdict, prelim ? Theme.Bold : Theme.H2, sc, M(new Rectangle(tx.X, tx.Y + 24, tx.Width, 36)), Theme.Align.Start, false, true);
                    Theme.Txt(g, Store.LastFullScan == DateTime.MinValue ? L.T("مبدئياً: " + score + " من 100 (الحماية والأقراص فقط)", "Preliminary: " + score + "/100 (protection & drives only)") : alerts.Count(x => x.Level == 2 || x.Level == 1) + L.T(" تنبيهات تحتاج مراجعة", " alerts to review"),
                        Theme.Small, Theme.Muted, M(new Rectangle(tx.X, tx.Y + 60, tx.Width, 44)), Theme.Align.Start, false, true);

                    // freeable
                    Card(g, M(c2));
                    long clean = Store.Total(Scanners.Cleanup), old = Store.Total(Scanners.Old);
                    var games = Store.Get(Scanners.Games);
                    long idle = games == null ? 0 : games.Where(r => r.Level == 1).Sum(r => r.Bytes);
                    long total = clean + old + idle;
                    Theme.Txt(g, L.T("مساحة يمكن تحريرها", "Space you can free"), Theme.Small, Theme.Muted, M(new Rectangle(c2.X + 22, c2.Y + 20, c2.Width - 44, 22)), Theme.Align.Start);
                    Theme.Txt(g, Store.LastFullScan == DateTime.MinValue && total == 0 ? "—" : U.GB(total) + " GB", Theme.Huge, Theme.Text, M(new Rectangle(c2.X + 22, c2.Y + 42, c2.Width - 44, 52)), Theme.Align.Start);
                    int ly = c2.Y + 102;
                    Line(g, c2, ref ly, L.T("ملفات مؤقتة", "Temporary files"), clean, Scanners.Cleanup);
                    Line(g, c2, ref ly, L.T("ملفات قديمة", "Old files"), old, Scanners.Old);
                    Line(g, c2, ref ly, L.T("ألعاب لم تُلعب منذ مدة", "Games not played lately"), idle, Scanners.Games);

                    // protection
                    Card(g, M(c3));
                    bool on = info != null && info.DefenderKnown && info.RealTime;
                    bool known = info != null && info.DefenderKnown;
                    Color pc = !known ? Theme.Faint : on ? Theme.Ok : Theme.Danger;
                    var ic = M(new Rectangle(c3.X + 22, c3.Y + 22, 44, 44));
                    Theme.FillRound(g, !known ? Theme.Track : on ? Theme.OkBg : Theme.DangerBg, ic, 12);
                    TextRenderer.DrawText(g, Glyph.Shield, Theme.IconBig, ic, pc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    var pt = new Rectangle(c3.X + 78, c3.Y + 22, c3.Width - 100, 22);
                    Theme.Txt(g, "Microsoft Defender", Theme.Small, Theme.Muted, M(pt), Theme.Align.Start);
                    Theme.Txt(g, info == null ? L.T("جارٍ القراءة...", "Reading...") : !known ? L.T("غير متاح (برنامج حماية آخر؟)", "Unavailable (another antivirus?)") : on ? L.T("الحماية الفورية تعمل", "Real-time protection on") : L.T("الحماية الفورية متوقفة!", "Real-time protection OFF!"),
                        Theme.H2, pc, M(new Rectangle(pt.X, pt.Y + 20, pt.Width, 28)), Theme.Align.Start);
                    int py = c3.Y + 86;
                    if (known)
                    {
                        KV(g, c3, ref py, L.T("آخر تحديث للتعريفات", "Definitions updated"), info.SigUpdated > DateTime.MinValue ? U.D(info.SigUpdated) : "—");
                        KV(g, c3, ref py, L.T("آخر فحص سريع", "Last quick scan"), info.LastQuick > DateTime.MinValue ? U.D(info.LastQuick) : "—");
                        KV(g, c3, ref py, L.T("آخر فحص كامل", "Last full scan"), info.LastFull > DateTime.MinValue ? U.D(info.LastFull) : L.T("لم يُجرَ", "Never"));
                    }
                    hot.Add(new KeyValuePair<Rectangle, string>(M(c3), "defender"));
                }
                y = (cols == 3 ? c1.Bottom : c3.Bottom) + GapW;

                // ---- row 2: disks + alerts
                int cols2 = inner >= 900 ? 2 : 1;
                int cw2 = (inner - GapW * (cols2 - 1)) / cols2;
                int vols = info == null ? 1 : Math.Max(1, info.Volumes.Count);
                int hd = 64 + vols * 62;
                int ha = 64 + alerts.Count * 50;
                int h2 = cols2 == 2 ? Math.Max(hd, ha) : 0;
                var d = new Rectangle(Pad, y, cw2, cols2 == 2 ? h2 : hd);
                var a = cols2 == 2 ? new Rectangle(Pad + cw2 + GapW, y, cw2, h2) : new Rectangle(Pad, y + hd + GapW, cw2, ha);
                if (paint)
                {
                    Card(g, M(d));
                    Theme.Txt(g, L.T("الأقراص", "Drives"), Theme.H2, Theme.Text, M(new Rectangle(d.X + 22, d.Y + 18, d.Width - 44, 26)), Theme.Align.Start);
                    int dy = d.Y + 58;
                    if (info == null) Theme.Txt(g, L.T("جارٍ القراءة...", "Reading..."), Theme.Body, Theme.Muted, M(new Rectangle(d.X + 22, dy, d.Width - 44, 24)), Theme.Align.Start);
                    else foreach (var v in info.Volumes)
                        {
                            double used = v.Size > 0 ? 1 - (double)v.Free / v.Size : 0;
                            string name = v.Letter + ":  " + (string.IsNullOrEmpty(v.Label) ? "" : v.Label + "  ") + (v.Media.Length > 0 ? "· " + v.Media : "");
                            Theme.Txt(g, name, Theme.Bold, Theme.Text, M(new Rectangle(d.X + 22, dy, d.Width / 2, 22)), Theme.Align.Start);
                            Theme.Txt(g, L.T("حر ", "Free ") + U.GB(v.Free) + L.T(" من ", " of ") + U.GB(v.Size) + " GB", Theme.Small, Theme.Muted, M(new Rectangle(d.X + d.Width / 2, dy, d.Width / 2 - 22, 22)), Theme.Align.End);
                            var track = new Rectangle(d.X + 22, dy + 28, d.Width - 44, 10);
                            Theme.FillRound(g, Theme.Track, M(track), 5);
                            Color bc = used > 0.9 ? Theme.Danger : used > 0.75 ? Theme.WarnDot : Theme.Primary;
                            var fill = new Rectangle(track.X, track.Y, Math.Max(10, (int)(track.Width * used)), track.Height);
                            Theme.FillRound(g, bc, M(fill), 5);
                            hot.Add(new KeyValuePair<Rectangle, string>(M(new Rectangle(d.X, dy - 6, d.Width, 56)), Scanners.Space));
                            dy += 62;
                        }

                    Card(g, M(a));
                    Theme.Txt(g, L.T("التنبيهات والتوصيات", "Alerts & recommendations"), Theme.H2, Theme.Text, M(new Rectangle(a.X + 22, a.Y + 18, a.Width - 44, 26)), Theme.Align.Start);
                    int ay = a.Y + 56;
                    foreach (var al in alerts)
                    {
                        Color fc = al.Level == 2 ? Theme.Danger : al.Level == 1 ? Theme.WarnDot : al.Level == 3 ? Theme.Ok : Theme.Primary;
                        string gl = al.Level == 2 ? Glyph.Error : al.Level == 1 ? Glyph.Warning : al.Level == 3 ? Glyph.Check : Glyph.Info;
                        var row = new Rectangle(a.X + 12, ay, a.Width - 24, 46);
                        if (!string.IsNullOrEmpty(al.Target)) hot.Add(new KeyValuePair<Rectangle, string>(M(row), al.Target));
                        TextRenderer.DrawText(g, gl, Theme.Icon, M(new Rectangle(row.X + 8, row.Y, 24, row.Height)), fc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        Theme.Txt(g, al.Text, Theme.Body, Theme.Text, M(new Rectangle(row.X + 40, row.Y + 3, row.Width - 40 - (al.Target == "" ? 8 : 40), row.Height - 4)), Theme.Align.Start, false, true);
                        if (!string.IsNullOrEmpty(al.Target))
                            Theme.Txt(g, L.Ar ? "‹" : "›", Theme.H2, Theme.Faint, M(new Rectangle(row.Right - 30, row.Y, 22, row.Height)), Theme.Align.Center);
                        ay += 50;
                    }
                }
                y = (cols2 == 2 ? d.Bottom : a.Bottom) + GapW;

                // ---- row 3: specs
                var specs = new List<KeyValuePair<string, string>>();
                if (info != null)
                {
                    specs.Add(new KeyValuePair<string, string>(L.T("النظام", "Windows"), info.OS + "  ·  Build " + info.Build));
                    specs.Add(new KeyValuePair<string, string>(L.T("المعالج", "Processor"), info.Cpu));
                    foreach (var gpu in info.Gpus) specs.Add(new KeyValuePair<string, string>(L.T("كرت الشاشة", "Graphics"), gpu));
                    specs.Add(new KeyValuePair<string, string>(L.T("الذاكرة", "Memory"), Math.Round(info.RamBytes / 1073741824.0) + " GB  ·  " + info.RamSticks + L.T(" شريحة", " sticks") + "  ·  " + info.RamSpeed + " MHz" + (info.RamPart.Length > 0 ? "  ·  " + info.RamPart : "")));
                    specs.Add(new KeyValuePair<string, string>(L.T("اللوحة الأم", "Motherboard"), info.Board + "  ·  BIOS " + info.Bios));
                    foreach (var dk in info.Disks) specs.Add(new KeyValuePair<string, string>(L.T("قرص", "Disk"), dk));
                    if (info.PowerPlan.Length > 0) specs.Add(new KeyValuePair<string, string>(L.T("خطة الطاقة", "Power plan"), info.PowerPlan));
                    specs.Add(new KeyValuePair<string, string>(L.T("مدة التشغيل", "Uptime"), (int)info.Uptime.TotalHours + L.T(" ساعة منذ آخر إعادة تشغيل", " hours since last restart")));
                }
                var sr = new Rectangle(Pad, y, inner, 64 + Math.Max(1, specs.Count) * 30);
                if (paint)
                {
                    Card(g, M(sr));
                    Theme.Txt(g, L.T("مواصفات الجهاز", "System specs"), Theme.H2, Theme.Text, M(new Rectangle(sr.X + 22, sr.Y + 18, sr.Width - 44, 26)), Theme.Align.Start);
                    int sy = sr.Y + 56;
                    if (specs.Count == 0) Theme.Txt(g, L.T("جارٍ القراءة...", "Reading..."), Theme.Body, Theme.Muted, M(new Rectangle(sr.X + 22, sy, sr.Width - 44, 24)), Theme.Align.Start);
                    foreach (var kv in specs)
                    {
                        Theme.Txt(g, kv.Key, Theme.Body, Theme.Muted, M(new Rectangle(sr.X + 22, sy, 150, 26)), Theme.Align.Start);
                        Theme.Txt(g, kv.Value, Theme.Body, Theme.Text, M(new Rectangle(sr.X + 180, sy, sr.Width - 202, 26)), Theme.Align.Start);
                        sy += 30;
                    }
                }
                y = sr.Bottom + 24;
                return y;
            }

            void Line(Graphics g, Rectangle c, ref int y, string label, long bytes, string target)
            {
                Theme.Txt(g, label, Theme.Body, Theme.Muted, M(new Rectangle(c.X + 22, y, c.Width - 140, 22)), Theme.Align.Start);
                Theme.Txt(g, bytes > 0 ? U.Size(bytes) : "—", Theme.Bold, Theme.Text, M(new Rectangle(c.Right - 140, y, 118, 22)), Theme.Align.End);
                hot.Add(new KeyValuePair<Rectangle, string>(M(new Rectangle(c.X, y - 2, c.Width, 24)), target));
                y += 22;
            }

            void KV(Graphics g, Rectangle c, ref int y, string k, string v)
            {
                Theme.Txt(g, k, Theme.Body, Theme.Muted, M(new Rectangle(c.X + 22, y, c.Width - 150, 22)), Theme.Align.Start);
                Theme.Txt(g, v, Theme.Bold, Theme.Text, M(new Rectangle(c.Right - 140, y, 118, 22)), Theme.Align.End);
                y += 24;
            }

            string Hit(Point p)
            {
                for (int i = hot.Count - 1; i >= 0; i--) if (hot[i].Key.Contains(p)) return hot[i].Value;
                return null;
            }

            protected override void OnMouseMove(MouseEventArgs e) { Cursor = Hit(e.Location) != null ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                string t = Hit(e.Location);
                if (t == null) return;
                if (t == "update") page.main.ShowUpdate(Updater.Available);
                else if (t == "scan") page.main.RunScan(Scanners.Overview);
                else if (t == "defender") U.Open("windowsdefender://threat");
                else page.main.Go(t);
                base.OnMouseClick(e);
            }
        }
    }
}
