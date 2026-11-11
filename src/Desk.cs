// W-Guard - desktop organizer: smart classification + folder review window (rename before organizing)
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WGuard
{
    class DeskCat
    {
        public string Id, Ar, En, Glyph, Keywords;
        public HashSet<string> Ext;
        public DeskCat(string id, string ar, string en, string glyph, string ext, string keywords)
        {
            Id = id; Ar = ar; En = en; Glyph = glyph; Keywords = keywords;
            Ext = new HashSet<string>(ext.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => "." + x), StringComparer.OrdinalIgnoreCase);
        }
        public string Default { get { return L.T(Ar, En); } }
    }

    static class Desk
    {
        const string GameWords = @"(?i)steam|epic ?games|ubisoft|uplay|origin|ea app|ea desktop|battle\.net|rockstar|game|launcher|fivem|roblox|riot|valorant|league of legends|minecraft|wemod|xbox|gog|playnite|emulator|lutris|fortnite|call of duty|rainbow six|gta";

        public static readonly DeskCat[] Cats = {
            new DeskCat("games", "1- الألعاب", "1- Games", WGuard.Glyph.Game, "", GameWords),
            new DeskCat("apps", "2- البرامج", "2- Apps", WGuard.Glyph.Apps, "lnk appref-ms", ""),
            new DeskCat("code", "3- المشاريع البرمجية", "3- Code projects", "",
                "py js ts jsx tsx php html htm css json sql cs java cpp c h hpp go rs kt swift lua rb sh ps1 ipynb sln csproj apk ipa yml yaml xml", @"(?i)\bbot\b|server|\bsrc\b|-main$|-master$|\bdist\b|project|repo|api|sdk|code|مشروع|مشاريع|برمج"),
            new DeskCat("designs", "4- التصاميم", "4- Designs", "",
                "psd psb ai eps idml indd svg xd fig sketch png jpg jpeg webp gif bmp tif tiff heic raw cr2 nef ttf otf woff woff2 blend", @"(?i)design|logo|mockup|font|تصميم|شعار"),
            new DeskCat("media", "5- الوسائط", "5- Media", "",
                "mp4 mkv mov avi wmv webm m4v mp3 wav flac m4a aac ogg", @"(?i)video|clip|فيديو|مقطع"),
            new DeskCat("docs", "6- المستندات", "6- Documents", "",
                "pdf doc docx xls xlsx ppt pptx txt csv rtf odt ods odp md epub pages numbers key", ""),
            new DeskCat("archives", "7- ملفات مضغوطة", "7- Archives", "", "zip rar 7z tar gz xz bz2 cab", ""),
            new DeskCat("review", "8- للمراجعة", "8- To review", WGuard.Glyph.Warning, "exe msi msix iso img bat cmd reg vbs jar", "")
        };

        public static DeskCat Get(string id) { return Cats.FirstOrDefault(c => c.Id == id) ?? Cats[Cats.Length - 1]; }

        public static string Name(string id)
        {
            string v;
            if (Settings.DeskNames.TryGetValue(id, out v) && v.Trim().Length > 0) return v.Trim();
            return Get(id).Default;
        }

        // every name that may already be one of our folders (current custom names + both default languages)
        static HashSet<string> OwnFolders()
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Cats) { s.Add(c.Ar); s.Add(c.En); s.Add(Name(c.Id)); }
            foreach (string legacy in new[] { "6- ملفات مضغوطة", "7- للمراجعة", "6- Archives", "7- To review", "5- المستندات", "5- Documents" }) s.Add(legacy);
            return s;
        }

        static string ShortcutTarget(string lnk)
        {
            try
            {
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic sc = sh.CreateShortcut(lnk);
                return ((string)sc.TargetPath ?? "") + " " + ((string)sc.Arguments ?? "");
            }
            catch { return ""; }
        }

        static string UrlTarget(string file)
        {
            try
            {
                foreach (string l in File.ReadAllLines(file))
                    if (l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return l.Substring(4).Trim();
            }
            catch { }
            return "";
        }

        // returns category id and a short human reason
        public static string Classify(string path, bool isDir, out string reason)
        {
            string name = Path.GetFileName(path);
            string ext = isDir ? "" : Path.GetExtension(path);
            var games = Get("games");

            if (isDir)
            {
                if (Regex.IsMatch(name, @"(?i)^(new folder|مجلد جديد)")) { reason = L.T("مجلد فارغ الاسم", "Unnamed folder"); return "review"; }
                foreach (string id in new[] { "games", "code", "designs", "media" })
                    if (Get(id).Keywords.Length > 0 && Regex.IsMatch(name, Get(id).Keywords)) { reason = L.T("مجلد - حسب اسمه", "Folder - by its name"); return id; }
                if (File.Exists(Path.Combine(path, "package.json")) || File.Exists(Path.Combine(path, ".git", "HEAD")) || Directory.GetFiles(path, "*.sln").Length > 0 || Directory.GetFiles(path, "*.py").Length > 0)
                { reason = L.T("مجلد يحتوي على كود", "Folder contains code"); return "code"; }
                reason = L.T("مجلد غير معروف", "Unknown folder");
                return "review";
            }

            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string t = ShortcutTarget(path);
                if (Regex.IsMatch(t + " " + name, GameWords)) { reason = L.T("اختصار لعبة/منصة ألعاب", "Game / launcher shortcut"); return "games"; }
                if (t.Trim().Length > 0 && Directory.Exists(t.Trim())) { reason = L.T("اختصار لمجلد", "Folder shortcut"); return "apps"; }
                reason = L.T("اختصار برنامج", "App shortcut");
                return "apps";
            }
            if (ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                string u = UrlTarget(path);
                if (Regex.IsMatch(u, @"(?i)^(steam|com\.epicgames\.launcher|uplay|origin2?|battlenet|roblox|riotclient|xbox|ms-xbl)") || Regex.IsMatch(name, GameWords))
                { reason = L.T("رابط لعبة", "Game link"); return "games"; }
                reason = L.T("رابط موقع", "Website link");
                return "apps";
            }

            foreach (var c in Cats)
                if (c.Ext.Contains(ext))
                {
                    if (c.Id == "review" && ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(name, games.Keywords) && !Regex.IsMatch(name, @"(?i)setup|install|installer"))
                    { reason = L.T("ملف تشغيل لعبة", "Game executable"); return "games"; }
                    reason = L.T("حسب نوع الملف ", "By file type ") + ext.ToLowerInvariant();
                    return c.Id;
                }

            foreach (string id in new[] { "code", "designs", "media" })
                if (Regex.IsMatch(name, Get(id).Keywords)) { reason = L.T("حسب الاسم", "By name"); return id; }
            reason = L.T("نوع غير معروف", "Unknown type");
            return "review";
        }

        public static List<Row> Scan(ScanCtx ctx)
        {
            var rows = new List<Row>();
            var own = OwnFolders();
            foreach (string baseDir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
            {
                try
                {
                    if (!Directory.Exists(baseDir)) continue;
                    foreach (string p in Directory.GetFileSystemEntries(baseDir))
                    {
                        try
                        {
                            string n = Path.GetFileName(p);
                            if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || own.Contains(n)) continue;
                            if ((File.GetAttributes(p) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                            bool isDir = Directory.Exists(p);
                            // a numbered folder ("1- Work", "3- Projects") is the user's own organization: never touch or list it
                            if (isDir && Regex.IsMatch(n, @"^\d+\s*-\s*\S")) continue;
                            if (ctx != null) ctx.Current = p;
                            string reason;
                            string id = Classify(p, isDir, out reason);
                            var row = new Row(n, "", 0, U.D(isDir ? Directory.GetLastWriteTime(p) : File.GetLastWriteTime(p)), p, "MoveDesk", id);
                            row.Risk = reason;
                            row.Check = true;
                            SetTarget(row, id);
                            rows.Add(row);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            if (ctx != null) ctx.StepProgress = 1;
            return rows.OrderBy(x => Array.FindIndex(Cats, c => c.Id == x.Key)).ThenBy(x => x.Name).ToList();
        }

        public static void SetTarget(Row r, string id)
        {
            r.Key = id;
            r.Detail = L.T("← ", "→ ") + Name(id);
        }

        public static bool ValidName(string s, out string why)
        {
            why = "";
            s = (s ?? "").Trim();
            if (s.Length == 0) { why = L.T("الاسم فارغ", "Name is empty"); return false; }
            if (s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { why = L.T("يحتوي على رموز غير مسموحة \\ / : * ? \" < > |", "Contains characters not allowed \\ / : * ? \" < > |"); return false; }
            if (s.EndsWith(".")) { why = L.T("لا يصح أن ينتهي بنقطة", "Cannot end with a dot"); return false; }
            if (Regex.IsMatch(s, @"(?i)^(con|prn|aux|nul|com\d|lpt\d)$")) { why = L.T("اسم محجوز في ويندوز", "Reserved Windows name"); return false; }
            return true;
        }
    }

    // ------------------------------------------------------------------ review window: folders to be created, renamable
    class OrganizeForm : Form
    {
        public List<Row> Result = new List<Row>();
        public List<string> EmptyFolders = new List<string>();
        readonly List<Row> items;
        readonly Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>();
        readonly Dictionary<string, CheckBox> includes = new Dictionary<string, CheckBox>();
        readonly Label error;
        readonly CheckBox remember;

        public OrganizeForm(List<Row> selected)
        {
            items = selected;
            Text = AppInfo.Name + " - " + L.T("ترتيب سطح المكتب", "Organize desktop");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            Font = Theme.Body;
            RightToLeft = L.Ar ? RightToLeft.Yes : RightToLeft.No;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var groups = Desk.Cats.ToList();
            int listH = Math.Min(470, groups.Count * 70);
            ClientSize = new Size(680, 200 + listH + 70);

            var head = new Label
            {
                Location = new Point(28, 20), Size = new Size(624, 30), Font = Theme.H1, ForeColor = Theme.Text,
                Text = L.T("المجلدات التي ستُنشأ على سطح المكتب", "Folders that will be created on the desktop")
            };
            var sub = new Label
            {
                Location = new Point(28, 54), Size = new Size(624, 44), Font = Theme.Body, ForeColor = Theme.Muted,
                Text = L.T("عاينّا سطح مكتبك وهذه التصنيفات المقترحة. غيّر أي اسم كما تحب، أو ألغِ تصنيفاً كاملاً ليبقى في مكانه.", "We reviewed your desktop and suggest these groups. Rename any folder as you like, or untick a group to leave it where it is.")
            };
            Controls.Add(head); Controls.Add(sub);

            var list = new Panel { Location = new Point(28, 108), Size = new Size(624, listH), AutoScroll = true, BackColor = Color.White };
            int y = 0;
            foreach (var c in groups)
            {
                var its = items.Where(r => r.Key == c.Id).ToList();
                var card = new Panel { Location = new Point(0, y), Size = new Size(listH < groups.Count * 70 ? 600 : 620, 62), BackColor = Theme.Bg };
                var inc = new CheckBox { Checked = its.Count > 0, Location = new Point(14, 20), Size = new Size(20, 20) };
                var glyph = new Label { Text = c.Glyph, Font = Theme.IconBig, ForeColor = inc.Checked ? Theme.Primary : Theme.Faint, Location = new Point(40, 14), Size = new Size(32, 32), TextAlign = ContentAlignment.MiddleCenter };
                var box = new TextBox { Text = Desk.Name(c.Id), Font = Theme.Bold, Location = new Point(80, 8), Width = 250, MaxLength = 80, Enabled = inc.Checked };
                var count = new Label { Text = its.Count > 0 ? its.Count + L.T(" عنصر", " items") : L.T("فارغ", "Empty"), Font = Theme.Bold, ForeColor = its.Count > 0 ? Theme.Text : Theme.Faint, Location = new Point(344, 10), Size = new Size(card.Width - 356, 22) };
                var sample = new Label
                {
                    Text = its.Count > 0 ? string.Join("، ", its.Take(3).Select(r => r.Name).ToArray()) + (its.Count > 3 ? " ..." : "")
                                         : L.T("لا توجد عناصر له الآن - فعّله لإنشاء المجلد فارغاً", "No items for it now - tick to create it empty"),
                    Font = Theme.Small, ForeColor = Theme.Muted, AutoEllipsis = true, Location = new Point(80, 36), Size = new Size(card.Width - 92, 20)
                };
                inc.CheckedChanged += (s, e) => { box.Enabled = inc.Checked; glyph.ForeColor = inc.Checked ? Theme.Primary : Theme.Faint; };
                box.TextChanged += (s, e) => Validate2();
                card.Controls.AddRange(new Control[] { inc, glyph, box, count, sample });
                list.Controls.Add(card);
                boxes[c.Id] = box;
                includes[c.Id] = inc;
                y += 70;
            }
            Controls.Add(list);

            int by = 108 + listH + 12;
            remember = new CheckBox { Text = L.T("احفظ هذه الأسماء للمرات القادمة", "Remember these names next time"), Checked = true, AutoSize = true, Location = new Point(28, by) };
            var reset = new LinkLabel { Text = L.T("استعادة الأسماء الافتراضية", "Restore default names"), AutoSize = true, LinkColor = Theme.Primary, LinkBehavior = LinkBehavior.HoverUnderline, Location = new Point(400, by + 2) };
            reset.LinkClicked += (s, e) => { foreach (var kv in boxes) kv.Value.Text = Desk.Get(kv.Key).Default; };
            error = new Label { AutoSize = false, Size = new Size(624, 22), Location = new Point(28, by + 28), ForeColor = Theme.Danger, Font = Theme.Bold };
            Controls.Add(remember); Controls.Add(reset); Controls.Add(error);

            var go = new FlatButton(L.T("بدء الترتيب", "Start organizing"), FlatButton.Kind.Primary, WGuard.Glyph.Check);
            var cancel = new FlatButton(L.T("إلغاء", "Cancel"), FlatButton.Kind.Secondary, "");
            go.Location = new Point(ClientSize.Width - 28 - go.Width, ClientSize.Height - 56);
            cancel.Location = new Point(go.Left - 10 - cancel.Width, ClientSize.Height - 56);
            go.Click += (s, e) => Accept();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(go); Controls.Add(cancel);
            // with RightToLeft = Yes, WinForms mirrors these left-based locations automatically only when RightToLeftLayout is on
            RightToLeftLayout = L.Ar;
        }

        bool Validate2()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in boxes)
            {
                if (!includes[kv.Key].Checked) continue;
                string why;
                if (!Desk.ValidName(kv.Value.Text, out why)) { error.Text = "«" + kv.Value.Text.Trim() + "»: " + why; return false; }
                if (!seen.Add(kv.Value.Text.Trim())) { error.Text = L.T("يوجد اسمان متطابقان: ", "Two folders have the same name: ") + kv.Value.Text.Trim(); return false; }
            }
            error.Text = "";
            return true;
        }

        void Accept()
        {
            if (!Validate2()) return;
            if (!includes.Values.Any(c => c.Checked)) { error.Text = L.T("لم تختر أي تصنيف", "No group selected"); return; }
            foreach (var kv in boxes)
            {
                string v = kv.Value.Text.Trim();
                if (v == Desk.Get(kv.Key).Default) Settings.DeskNames.Remove(kv.Key);
                else Settings.DeskNames[kv.Key] = v;
            }
            if (remember.Checked) Settings.Save();
            foreach (var r in items)
            {
                CheckBox inc;
                if (includes.TryGetValue(r.Key, out inc) && inc.Checked) { Desk.SetTarget(r, r.Key); Result.Add(r); }
            }
            foreach (var kv in includes)
                if (kv.Value.Checked && !items.Any(r => r.Key == kv.Key)) EmptyFolders.Add(kv.Key);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
