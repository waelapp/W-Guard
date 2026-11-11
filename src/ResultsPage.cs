// W-Guard - generic results page (table + action bar) used by every scan section
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WGuard
{
    class ResultsPage : UserControl, IPage
    {
        readonly string section;
        readonly MainForm main;
        readonly DataGridView grid;
        readonly TextBox search;
        readonly CheckBox onlyRisk, recycle;
        readonly Label summary, empty, countLbl;
        readonly FlatButton actBtn, scanBtn;
        readonly Panel card;
        List<Row> rows = new List<Row>();
        bool busy;
        Control[] headerActions;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public ResultsPage(MainForm owner, string sectionId)
        {
            main = owner;
            section = sectionId;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(28, 0, 28, 20);

            // ---- toolbar
            var tool = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, 4, 0, 6) };
            search = new TextBox { Width = 260, Font = Theme.Body, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 0) };
            search.HandleCreated += (s, e) => SendMessage(search.Handle, 0x1501, (IntPtr)1, L.T("بحث بالاسم أو المسار...", "Search name or path..."));
            search.TextChanged += (s, e) => Fill();
            onlyRisk = new CheckBox { Text = L.T("التنبيهات فقط", "Warnings only"), AutoSize = true, Font = Theme.Body, Margin = new Padding(16, 7, 0, 0), ForeColor = Theme.Text };
            onlyRisk.CheckedChanged += (s, e) => Fill();
            var all = MakeLink(L.T("تحديد الكل", "Select all"), (s, e) => SetAll(true));
            var none = MakeLink(L.T("إلغاء التحديد", "Clear selection"), (s, e) => SetAll(false));
            countLbl = new Label { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, Margin = new Padding(16, 9, 0, 0) };
            tool.Controls.AddRange(new Control[] { search, onlyRisk, all, none, countLbl });

            // ---- table card
            card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
            card.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
            grid = new DataGridView();
            StyleGrid(grid);
            card.Controls.Add(grid);
            empty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.H2, ForeColor = Theme.Muted, BackColor = Color.White,
                Text = L.T("لم يُفحص هذا القسم بعد.\nاضغط \"فحص\" في الأعلى - الفحص للقراءة فقط ولن يُغيّر شيئاً.", "This section has not been scanned yet.\nPress \"Scan\" above - scanning is read-only and changes nothing.")
            };
            card.Controls.Add(empty);
            empty.BringToFront();

            // ---- bottom action bar
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = Theme.Bg, Padding = new Padding(0, 12, 0, 0) };
            var barInner = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 8, 14, 8) };
            barInner.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, barInner.Width - 1, barInner.Height - 1); };
            var right = new FlowLayoutPanel { Dock = L.Ar ? DockStyle.Left : DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.White };
            recycle = new CheckBox { Text = L.T("إلى سلة المحذوفات (قابل للاسترجاع)", "To Recycle Bin (restorable)"), Checked = true, AutoSize = true, Font = Theme.Body, Margin = new Padding(0, 9, 14, 0), ForeColor = Theme.Text };
            actBtn = new FlatButton(Actions.Label(section), Actions.IsDestructive(section) ? FlatButton.Kind.Danger : FlatButton.Kind.Primary, Actions.IsDestructive(section) ? Glyph.Delete : Glyph.Check);
            actBtn.Click += (s, e) => DoAction();
            right.Controls.Add(recycle);
            right.Controls.Add(actBtn);
            recycle.Visible = section == Scanners.Space || section == Scanners.Old;
            summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Bold, ForeColor = Theme.Text };
            barInner.Controls.Add(summary);
            barInner.Controls.Add(right);
            bar.Controls.Add(barInner);

            Controls.Add(card);
            Controls.Add(bar);
            Controls.Add(tool);

            scanBtn = new FlatButton(L.T("فحص", "Scan"), FlatButton.Kind.Primary, Glyph.Search);
            scanBtn.Click += (s, e) => main.RunScan(section);
            var extra = new List<Control>();
            if (section == Scanners.Desktop)
            {
                var undo = new FlatButton(L.T("تراجع عن الترتيب", "Undo organizing"), FlatButton.Kind.Secondary, Glyph.Undo);
                undo.Click += (s, e) =>
                {
                    int n = Actions.UndoDesktop();
                    Msg.Info(this, n < 0 ? L.T("لا يوجد ترتيب سابق للتراجع عنه.", "Nothing to undo.") : L.T("أُعيد ", "Restored ") + n + L.T(" عنصراً إلى مكانه.", " items to their place."));
                    if (n > 0) main.RunScan(section);
                };
                extra.Add(undo);
            }
            if (section == Scanners.Security)
            {
                var q = new FlatButton(L.T("فحص Defender سريع", "Defender quick scan"), FlatButton.Kind.Secondary, Glyph.Shield);
                q.Click += (s, e) => { Actions.DefenderQuickScan(); Msg.Info(this, L.T("بدأ فحص Microsoft Defender السريع في الخلفية. تابع النتيجة من أمن Windows.", "Microsoft Defender quick scan started in the background. Follow it in Windows Security.")); };
                extra.Add(q);
                var ws = new FlatButton(L.T("أمن Windows", "Windows Security"), FlatButton.Kind.Secondary, "");
                ws.Click += (s, e) => U.Open("windowsdefender://threat");
                extra.Add(ws);
            }
            if (section == Scanners.Programs)
            {
                var ap = new FlatButton(L.T("إعدادات التطبيقات", "Apps settings"), FlatButton.Kind.Secondary, "");
                ap.Click += (s, e) => U.Open("ms-settings:appsfeatures");
                extra.Add(ap);
            }
            extra.Add(scanBtn);
            headerActions = extra.ToArray();
            UpdateSummary();
        }

        LinkLabel MakeLink(string text, LinkLabelLinkClickedEventHandler h)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, Font = Theme.Body, LinkColor = Theme.Primary, ActiveLinkColor = Theme.PrimaryHover, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(16, 9, 0, 0) };
            l.LinkClicked += h;
            return l;
        }

        public string Title
        {
            get
            {
                switch (section)
                {
                    case Scanners.Space: return L.T("المساحة", "Disk space");
                    case Scanners.Old: return L.T("ملفات قديمة", "Old files");
                    case Scanners.Games: return L.T("الألعاب", "Games");
                    case Scanners.Programs: return L.T("البرامج", "Programs");
                    case Scanners.Security: return L.T("الأمان", "Security");
                    case Scanners.Background: return L.T("الخلفية وبدء التشغيل", "Background & startup");
                    case Scanners.Cleanup: return L.T("التنظيف", "Cleanup");
                    case Scanners.Desktop: return L.T("ترتيب سطح المكتب", "Desktop organizer");
                }
                return section;
            }
        }

        public string Subtitle
        {
            get
            {
                switch (section)
                {
                    case Scanners.Space: return L.T("أكبر المجلدات والملفات في كل الأقراص. ملفات النظام والبرامج للعرض فقط.", "Largest folders and files on every drive. System and app files are view-only.");
                    case Scanners.Old: return L.T("ملفات لم تُعدّل منذ " + Settings.OldDays + " يوماً (ملفاتك والأقراص الإضافية فقط).", "Files not modified for " + Settings.OldDays + " days (your files and extra drives only).");
                    case Scanners.Games: return L.T("ألعاب Steam و Epic و Xbox مع الحجم وآخر تشغيل. الإزالة تتم عبر المنصة نفسها.", "Steam, Epic and Xbox games with size and last played. Removal is done by the platform itself.");
                    case Scanners.Programs: return L.T("البرامج المثبتة مع تنبيه للبرامج الدعائية والبقايا وغير المستخدمة.", "Installed programs, flagging adware, leftovers and unused apps.");
                    case Scanners.Security: return L.T("التشغيل التلقائي والمهام والخدمات غير التابعة لويندوز مع تقييم الخطورة، وسجل Defender.", "Autoruns, tasks and non-Windows services rated by risk, plus Defender history.");
                    case Scanners.Background: return L.T("ما يعمل الآن واستهلاكه للذاكرة، وما يقلع مع ويندوز.", "What is running now and its memory use, and what starts with Windows.");
                    case Scanners.Cleanup: return L.T("ملفات مؤقتة وكاش يُعاد إنشاؤها تلقائياً - حذفها آمن.", "Temporary files and caches that are recreated automatically - safe to clear.");
                    case Scanners.Desktop: return L.T("يصنّف كل عنصر تلقائياً، وقبل البدء تراجع المجلدات وتسمّيها كما تحب. كليك يمين لتغيير مجلد أي عنصر.", "Sorts every item automatically; before starting you review and rename the folders. Right-click to change an item's folder.");
                }
                return "";
            }
        }

        public Control[] HeaderActions { get { return headerActions; } }

        public void OnShown()
        {
            var l = Store.Get(section);
            rows = l ?? new List<Row>();
            empty.Visible = l == null;
            if (l != null && l.Count == 0)
            {
                empty.Visible = true;
                empty.Text = L.T("لا توجد نتائج - كل شيء نظيف هنا ✓", "Nothing found - all clean here ✓");
            }
            Fill();
        }

        void StyleGrid(DataGridView g)
        {
            g.Dock = DockStyle.Fill;
            g.BorderStyle = BorderStyle.None;
            g.BackgroundColor = Color.White;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = Theme.Border;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 40;
            // visible dividers between column headers, so it is clear where to drag to resize a column
            g.CellPainting += (s, e) =>
            {
                if (e.RowIndex != -1 || e.ColumnIndex < 0) return;
                e.Paint(e.CellBounds, DataGridViewPaintParts.All);
                var b = e.CellBounds;
                using (var pen = new Pen(Theme.Hex("D5DBE5")))
                {
                    e.Graphics.DrawLine(pen, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);
                    bool last = e.ColumnIndex == g.Columns.GetLastColumn(DataGridViewElementStates.Visible, DataGridViewElementStates.None).Index;
                    if (!last && g.Columns[e.ColumnIndex].Resizable != DataGridViewTriState.False)
                    {
                        int x = g.RightToLeft == RightToLeft.Yes ? b.Left : b.Right - 1;
                        using (var dp = new Pen(Theme.Hex("B8C1CE"), 1.5f)) e.Graphics.DrawLine(dp, x, b.Top + 9, x, b.Bottom - 10);
                    }
                }
                e.Handled = true;
            };
            g.RowTemplate.Height = 38;
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.Hex("F7F8FB");
            g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Hex("F7F8FB");
            g.ColumnHeadersDefaultCellStyle.Font = Theme.Bold;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            g.DefaultCellStyle.Font = Theme.Body;
            g.DefaultCellStyle.ForeColor = Theme.Text;
            g.DefaultCellStyle.SelectionBackColor = Theme.TealTint;
            g.DefaultCellStyle.SelectionForeColor = Theme.Text;
            g.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            try { typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(g, true, null); } catch { }

            var chk = new DataGridViewCheckBoxColumn { Name = "chk", HeaderText = "", Width = 44, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Resizable = DataGridViewTriState.False };
            g.Columns.Add(chk);
            AddText(g, "name", L.T("الاسم", "Name"), 24);
            var plat = AddText(g, "platform", L.T("المنصة", "Platform"), 0); plat.Width = 110; plat.Visible = section == Scanners.Games;
            AddText(g, "detail", L.T("التفاصيل", "Details"), 34);
            var size = AddText(g, "size", L.T("الحجم", "Size"), 0); size.Width = 96;
            var date = AddText(g, "date", section == Scanners.Games || section == Scanners.Programs ? L.T("آخر تشغيل", "Last used") : L.T("التاريخ", "Date"), 0); date.Width = 112;
            AddText(g, "risk", L.T("تنبيه", "Warning"), 20);
            AddText(g, "path", L.T("المسار", "Path"), 22);
            if (section == Scanners.Security || section == Scanners.Desktop) g.Columns["size"].Visible = false;
            if (section == Scanners.Cleanup || section == Scanners.Background) g.Columns["date"].Visible = false;
            if (section == Scanners.Cleanup) g.Columns["risk"].Visible = false;
            if (section == Scanners.Background) { g.Columns["size"].HeaderText = L.T("الذاكرة", "Memory"); g.Columns["risk"].HeaderText = L.T("النصيحة", "Advice"); g.Columns["risk"].FillWeight = 34; g.Columns["detail"].FillWeight = 26; }
            if (section == Scanners.Games) g.Columns["detail"].HeaderText = L.T("مكتبة الألعاب", "Library");
            if (section == Scanners.Desktop) { g.Columns["detail"].HeaderText = L.T("المجلد", "Folder"); g.Columns["risk"].HeaderText = L.T("سبب التصنيف", "Why"); g.Columns["date"].Visible = false; }

            g.SortCompare += (s, e) =>
            {
                if (e.Column.Name != "size") return;
                var a = g.Rows[e.RowIndex1].Tag as Row; var b = g.Rows[e.RowIndex2].Tag as Row;
                e.SortResult = (a == null ? 0 : a.Bytes).CompareTo(b == null ? 0 : b.Bytes);
                e.Handled = true;
            };
            g.CurrentCellDirtyStateChanged += (s, e) => { if (g.IsCurrentCellDirty) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            g.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var r = g.Rows[e.RowIndex].Tag as Row;
                if (r != null) r.Check = Convert.ToBoolean(g.Rows[e.RowIndex].Cells[0].Value);
                UpdateSummary();
            };
            g.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var r = g.Rows[e.RowIndex].Tag as Row;
                if (r == null) return;
                string col = g.Columns[e.ColumnIndex].Name;
                var str = e.Value as string;
                if (L.Ar && !string.IsNullOrEmpty(str) && !Theme.HasArabic(str))
                {
                    // keep sizes, dates and paths left-to-right with Western digits inside the RTL table
                    e.Value = "\u202A" + str + "\u202C";
                    e.FormattingApplied = true;
                }
                if (col == "risk" && r.Level > 0)
                {
                    e.CellStyle.ForeColor = r.Level == 2 ? Theme.Danger : r.Level == 3 ? Theme.Ok : Theme.Warn;
                    e.CellStyle.BackColor = r.Level == 2 ? Theme.DangerBg : r.Level == 3 ? Theme.OkBg : Theme.WarnBg;
                    e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                    e.CellStyle.Font = Theme.Bold;
                }
                else if (col == "name") e.CellStyle.Font = Theme.Bold;
                else if (col == "detail" || col == "path" || col == "date") e.CellStyle.ForeColor = Theme.Muted;
                if (r.Action == "" && col == "chk") e.CellStyle.BackColor = Theme.Hex("F7F8FB");
            };
            g.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var r = g.Rows[e.RowIndex].Tag as Row;
                if (r != null && r.Path.Length > 2 && r.Path[1] == ':') U.ShowInExplorer(r.Path);
            };
            var menu = new ContextMenuStrip { RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No };
            menu.Items.Add(L.T("فتح الموقع", "Open location"), null, (s, e) => { var r = CurrentRow(); if (r != null) U.ShowInExplorer(r.Path); });
            menu.Items.Add(L.T("نسخ المسار", "Copy path"), null, (s, e) => { var r = CurrentRow(); if (r != null && r.Path.Length > 0) Clipboard.SetText(r.Path); });
            menu.Items.Add(L.T("ابحث عنه في الإنترنت", "Search the web for it"), null, (s, e) => { var r = CurrentRow(); if (r != null) U.Open("https://www.bing.com/search?q=" + Uri.EscapeDataString(r.Name + " " + L.T("ما هو", "what is"))); });
            if (section == Scanners.Desktop)
            {
                var move = new ToolStripMenuItem(L.T("نقل إلى مجلد آخر", "Move to another folder"));
                foreach (var c in Desk.Cats)
                {
                    string id = c.Id;
                    var mi = new ToolStripMenuItem(Desk.Name(id));
                    mi.Click += (s, e) => ChangeTarget(id);
                    move.DropDownItems.Add(mi);
                }
                menu.Opening += (s, e) => { for (int i = 0; i < Desk.Cats.Length; i++) move.DropDownItems[i].Text = Desk.Name(Desk.Cats[i].Id); };
                menu.Items.Insert(0, move);
                menu.Items.Insert(1, new ToolStripSeparator());
            }
            g.ContextMenuStrip = menu;
            g.CellMouseDown += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !g.Rows[e.RowIndex].Selected) { g.ClearSelection(); g.Rows[e.RowIndex].Selected = true; g.CurrentCell = g.Rows[e.RowIndex].Cells[1]; } };
        }

        void ChangeTarget(string id)
        {
            foreach (DataGridViewRow gr in grid.SelectedRows)
            {
                var r = gr.Tag as Row;
                if (r == null) continue;
                Desk.SetTarget(r, id);
                r.Risk = L.T("اختيارك", "Your choice");
            }
            Fill();
        }

        Row CurrentRow() { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Row; }

        DataGridViewTextBoxColumn AddText(DataGridView g, string name, string header, int fill)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic };
            if (fill > 0) c.FillWeight = fill; else c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            g.Columns.Add(c);
            return c;
        }

        void Fill()
        {
            string q = search.Text.Trim();
            grid.SuspendLayout();
            grid.Rows.Clear();
            var list = new List<DataGridViewRow>();
            foreach (var r in rows)
            {
                if (onlyRisk.Checked && (r.Level == 0 || r.Level == 3)) continue;
                if (q.Length > 0 && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && r.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && r.Detail.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var gr = new DataGridViewRow();
                string detail = r.Detail;
                if (section == Scanners.Games) { int i = detail.IndexOf(" · "); if (i >= 0) detail = detail.Substring(i + 3); }
                gr.CreateCells(grid, r.Check && r.Action != "", r.Name, Platform(r), detail, U.Size(r.Bytes), r.Date, r.Risk, r.Path);
                gr.Tag = r;
                if (r.Action == "") gr.Cells[0].ReadOnly = true;
                list.Add(gr);
            }
            grid.Rows.AddRange(list.ToArray());
            grid.ResumeLayout();
            countLbl.Text = list.Count == rows.Count ? U.Num(rows.Count) + L.T(" عنصر", " items") : U.Num(list.Count) + L.T(" من ", " of ") + U.Num(rows.Count);
            UpdateSummary();
        }

        void SetAll(bool v)
        {
            foreach (DataGridViewRow gr in grid.Rows)
            {
                var r = gr.Tag as Row;
                if (r == null || r.Action == "") continue;
                r.Check = v;
                gr.Cells[0].Value = v;
            }
            UpdateSummary();
        }

        void UpdateSummary()
        {
            if (busy) return;
            var sel = rows.Where(r => r.Check && r.Action != "").ToList();
            long bytes = sel.Sum(r => r.Bytes);
            bool sized = section != Scanners.Security && section != Scanners.Desktop && section != Scanners.Background;
            summary.Text = sel.Count == 0
                ? L.T("حدّد العناصر من الجدول ثم اضغط زر التنفيذ", "Select items in the table, then press the action button")
                : L.T("المحدد: ", "Selected: ") + U.Num(sel.Count) + L.T(" عنصر", " items") + (sized && bytes > 0 ? "  ·  " + U.Size(bytes) : "");
            actBtn.Enabled = (sel.Count > 0 || (section == Scanners.Desktop && Store.Get(section) != null)) && !busy;
        }

        void DoAction()
        {
            grid.EndEdit();
            var sel = rows.Where(r => r.Check && r.Action != "").ToList();
            if (sel.Count == 0 && section != Scanners.Desktop) return;
            bool rec = recycle.Checked || !recycle.Visible;
            if (section == Scanners.Desktop)
            {
                // review window: shows the folders that will be created and lets the user rename them
                using (var f = new OrganizeForm(sel))
                {
                    if (f.ShowDialog(this) != DialogResult.OK) { Fill(); return; }
                    sel = f.Result;
                    if (!Demo.On) foreach (string id in f.EmptyFolders) Actions.CreateDeskFolder(id);
                }
                Fill();
                if (Demo.On) { Msg.Info(this, L.T("وضع العرض: لا تُنفَّذ أي تغييرات على الجهاز.", "Demo mode: no changes are made to the PC.")); return; }
                if (sel.Count == 0) { Msg.Info(this, L.T("تم إنشاء المجلدات.", "Folders created.")); return; }
                RunSelected(sel, rec);
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine(L.T("سيتم تنفيذ \"", "This will run \"") + actBtn.Text + L.T("\" على ", "\" on ") + sel.Count + L.T(" عنصر", " items") + (sel.Sum(x => x.Bytes) > 0 ? " (" + U.Size(sel.Sum(x => x.Bytes)) + ")" : "") + ":");
            sb.AppendLine();
            foreach (var r in sel.Take(14)) sb.AppendLine("•  " + r.Name + (section == Scanners.Games ? "  (" + Platform(r) + ")" : ""));
            if (sel.Count > 14) sb.AppendLine(L.T("... و", "... and ") + (sel.Count - 14) + L.T(" أخرى", " more"));
            if (recycle.Visible && !recycle.Checked) { sb.AppendLine(); sb.AppendLine(L.T("تنبيه: الحذف نهائي ولا يمكن استرجاعه.", "Warning: deletion is permanent and cannot be undone.")); }
            if (section == Scanners.Games)
            {
                // games can only be removed by the platform that installed them
                var plats = sel.Select(Platform).Where(x => x != "").Distinct().ToList();
                sb.AppendLine();
                sb.AppendLine(L.T("مهم: الإزالة تتم عن طريق المنصة نفسها، فيجب أن تكون ", "Important: removal is done by the platform itself, so ") + string.Join(L.T(" و ", " and "), plats) +
                    L.T(" مثبّتة وتعمل ومسجّلاً دخولك فيها. ستفتح نافذة المنصة لتأكيد إزالة كل لعبة.", " must be installed, running and signed in. The platform's window will open to confirm each removal."));
                if (plats.Contains("Xbox")) sb.AppendLine(L.T("ألعاب Xbox تُزال من «الإعدادات ← التطبيقات المثبتة».", "Xbox games are removed from Settings > Installed apps."));
            }
            if (section == Scanners.Programs) { sb.AppendLine(); sb.AppendLine(L.T("ستظهر نافذة إزالة كل برنامج - أكملها حتى النهاية.", "Each program's uninstaller will open - follow it to the end.")); }
            sb.AppendLine();
            sb.Append(L.T("هل تريد المتابعة؟", "Do you want to continue?"));
            if (Msg.Show(this, sb.ToString(), L.T("تأكيد", "Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RunSelected(sel, rec);
        }

        static string Platform(Row r)
        {
            switch (r.Action)
            {
                case "SteamUninstall": return "Steam";
                case "OpenEpic": return "Epic Games";
                case "OpenXbox": return "Xbox";
            }
            return "";
        }

        void RunSelected(List<Row> sel, bool rec)
        {
            if (Demo.On) { Msg.Info(this, L.T("وضع العرض: لا تُنفَّذ أي تغييرات على الجهاز.", "Demo mode: no changes are made to the PC.")); return; }

            busy = true;
            actBtn.Enabled = false;
            main.SetBusy(true);
            int ok = 0, fail = 0;
            var errors = new List<string>();
            var done = new List<Row>();
            var th = new Thread(() =>
            {
                int i = 0;
                foreach (var r in sel)
                {
                    i++;
                    string label = L.T("تنفيذ ", "Running ") + i + "/" + sel.Count + ":  " + r.Name;
                    try { BeginInvoke((Action)(() => summary.Text = label)); } catch { }
                    try
                    {
                        string res = Actions.Run(r, rec);
                        ok++;
                        Log.Write("[" + section + "] " + res + ": " + r.Name + " | " + r.Path);
                        if (r.Action != "SteamUninstall" && r.Action != "OpenEpic" && r.Action != "OpenXbox") done.Add(r);
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        errors.Add(r.Name + ": " + ex.Message);
                        Log.Write("[" + section + "] FAILED: " + r.Name + " | " + ex.Message);
                    }
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        foreach (var r in done) { Store.Remove(section, r); rows.Remove(r); }
                        busy = false;
                        main.SetBusy(false);
                        Fill();
                        main.RefreshBadges();
                        string msg = L.T("تم بنجاح: ", "Succeeded: ") + ok + "    " + L.T("فشل: ", "Failed: ") + fail;
                        if (errors.Count > 0) msg += "\n\n" + string.Join("\n", errors.Take(8).ToArray());
                        msg += "\n\n" + L.T("التفاصيل في السجل:\n", "Details in the log:\n") + Log.FilePath;
                        Msg.Show(this, msg, L.T("انتهى", "Done"), MessageBoxButtons.OK, fail > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
        }
    }
}
