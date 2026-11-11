// W-Guard - Core: app info, language, settings, log, theme, logo, helpers
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WGuard
{
    static class AppInfo
    {
        public const string Name = "W-Guard";
        public const string Owner = "WAEL";
        public const string Link = "https://github.com/waelapp";
        public const string LinkText = "github.com/waelapp";
        public const string Year = "2026";

        // GitHub repository that hosts the releases (owner/repo). Change before publishing.
        public const string RepoOwner = "waelapp";
        public const string RepoName = "W-Guard";

        public static Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string VersionText
        {
            get
            {
                Version v = Version;
                return v.Major + "." + v.Minor + (v.Build > 0 ? "." + v.Build : "");
            }
        }
        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "W-Guard");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string LogDir
        {
            get { string d = Path.Combine(DataDir, "logs"); Directory.CreateDirectory(d); return d; }
        }
        public static string ReportDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "W-Guard");
                Directory.CreateDirectory(d);
                return d;
            }
        }
    }

    // ------------------------------------------------------------------ language
    static class L
    {
        public static bool Ar = true;
        public static string T(string ar, string en) { return Ar ? ar : en; }
    }

    // ------------------------------------------------------------------ settings
    static class Settings
    {
        public static string Lang = "";
        public static bool CheckUpdates = true;
        public static int OldDays = 548;
        public static int BigMB = 500;
        public static int OldMinMB = 50;
        public static int RescanDays = 7;
        public static string SkipVersion = "";
        public static bool DarkMode;
        public static Dictionary<string, string> DeskNames = new Dictionary<string, string>();

        static string FilePath { get { return Path.Combine(AppInfo.DataDir, "settings.ini"); } }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim();
                        string v = line.Substring(i + 1).Trim();
                        int n;
                        if (k == "lang") Lang = v;
                        else if (k == "checkUpdates") CheckUpdates = v != "0";
                        else if (k == "oldDays" && int.TryParse(v, out n)) OldDays = Math.Max(30, n);
                        else if (k == "bigMB" && int.TryParse(v, out n)) BigMB = Math.Max(50, n);
                        else if (k == "oldMinMB" && int.TryParse(v, out n)) OldMinMB = Math.Max(1, n);
                        else if (k == "rescanDays" && int.TryParse(v, out n)) RescanDays = Math.Max(1, Math.Min(365, n));
                        else if (k == "dark") DarkMode = v == "1";
                        else if (k == "skipVersion") SkipVersion = v;
                        else if (k.StartsWith("desk.") && v.Length > 0) DeskNames[k.Substring(5)] = v;
                    }
                }
            }
            catch { }
            if (Lang != "ar" && Lang != "en")
            {
                string ui = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                Lang = ui == "ar" ? "ar" : "en";
            }
        }

        public static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("lang=" + Lang);
                sb.AppendLine("checkUpdates=" + (CheckUpdates ? "1" : "0"));
                sb.AppendLine("oldDays=" + OldDays);
                sb.AppendLine("bigMB=" + BigMB);
                sb.AppendLine("oldMinMB=" + OldMinMB);
                sb.AppendLine("rescanDays=" + RescanDays);
                sb.AppendLine("dark=" + (DarkMode ? "1" : "0"));
                sb.AppendLine("skipVersion=" + SkipVersion);
                foreach (var kv in DeskNames) sb.AppendLine("desk." + kv.Key + "=" + kv.Value);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ log
    static class Log
    {
        static readonly object Lock = new object();
        public static string FilePath { get { return Path.Combine(AppInfo.LogDir, "log-" + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".txt"); } }
        public static void Write(string text)
        {
            try
            {
                lock (Lock) { File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + text + Environment.NewLine, Encoding.UTF8); }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ theme
    static class Theme
    {
        public static Color Hex(string h)
        {
            return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
        }
        public static Color Navy = Hex("111827");
        public static Color Navy2 = Hex("1C2536");
        public static Color NavyLine = Hex("263043");
        public static Color NavText = Hex("C9D1DE");
        public static Color Teal = Hex("00D6A0");
        public static Color Primary = Hex("08876A");
        public static Color PrimaryHover = Hex("066F57");
        public static Color TealTint = Hex("E3F8F1");
        public static Color Bg = Hex("F3F5F8");
        public static Color Card = Color.White;
        public static Color Border = Hex("E3E7EE");
        public static Color Track = Hex("E6EAF0");
        public static Color Text = Hex("111827");
        public static Color Muted = Hex("5B6577");
        public static Color Faint = Hex("8892A4");
        public static Color WarnBg = Hex("FFF4E5");
        public static Color Warn = Hex("8A4B00");
        public static Color WarnDot = Hex("E8930C");
        public static Color Danger = Hex("D9342B");
        public static Color DangerHover = Hex("B9281F");
        public static Color DangerBg = Hex("FDECEB");
        public static Color Ok = Hex("1F9D6B");
        public static Color OkBg = Hex("E7F6EF");

        // extra surfaces (tables, notes, inputs)
        public static Color HeaderBg = Hex("F7F8FB");
        public static Color GridLine = Hex("D5DBE5");
        public static Color GridDivider = Hex("B8C1CE");
        public static Color NoteText = Hex("1F4D36");
        public static Color Input = Color.White;
        public static Color Link = Hex("08876A");
        public static bool Dark;

        // dark palette: same layout and brand colors, darker surfaces and lighter text
        public static void Apply(bool dark)
        {
            Dark = dark;
            if (!dark) return;
            Navy = Hex("0A0F19"); Navy2 = Hex("1C2536"); NavyLine = Hex("182132");
            TealTint = Hex("11352C");
            Bg = Hex("0E1420"); Card = Hex("171F2E"); Border = Hex("263043"); Track = Hex("263043");
            Text = Hex("E8ECF2"); Muted = Hex("9AA4B5"); Faint = Hex("6F7A8C");
            WarnBg = Hex("3A2A12"); Warn = Hex("F5B35C");
            DangerBg = Hex("3A1D20");
            Ok = Hex("2BD99A"); OkBg = Hex("12362B");
            HeaderBg = Hex("1C2536"); GridLine = Hex("2E3A50"); GridDivider = Hex("4A5870");
            NoteText = Hex("BFEBDD"); Input = Hex("1F2838"); Link = Hex("2BD99A");
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

        // gives standard Windows controls (text boxes, lists, check boxes, scroll bars, title bar) the current palette
        static readonly HashSet<Control> styled = new HashSet<Control>();
        public static void Style(Control c)
        {
            if (!Dark || c == null || !styled.Add(c)) return;
            c.Disposed += (s, e) => styled.Remove(c);
            var f = c as Form;
            if (f != null)
            {
                f.HandleCreated += (s, e) => { int on = 1; try { DwmSetWindowAttribute(f.Handle, 20, ref on, 4); } catch { } };
                if (f.IsHandleCreated) { int on = 1; try { DwmSetWindowAttribute(f.Handle, 20, ref on, 4); } catch { } }
            }
            if (c is TextBox || c is NumericUpDown || c is ComboBox) { c.BackColor = Input; c.ForeColor = Text; var cb = c as ComboBox; if (cb != null) cb.FlatStyle = FlatStyle.Flat; }
            else if (c.ForeColor == SystemColors.ControlText) c.ForeColor = Text;
            var dg = c as DataGridView;
            if (dg != null || c is TextBox || (c is ScrollableControl && ((ScrollableControl)c).AutoScroll))
            {
                c.HandleCreated += (s, e) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
                if (c.IsHandleCreated) try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
            }
            foreach (Control k in c.Controls) Style(k);
            c.ControlAdded += (s, e) => Style(e.Control);
        }

        public static readonly Font Body = new Font("Segoe UI", 10f);
        public static readonly Font Small = new Font("Segoe UI", 9f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 10f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 12f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 17f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 15f);
        public static readonly Font IconSmall = new Font("Segoe MDL2 Assets", 9f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 24f);
        public static readonly Font Huge = new Font("Segoe UI Semibold", 30f);
        public static readonly Font Icon = new Font("Segoe MDL2 Assets", 12f);
        public static readonly Font IconBig = new Font("Segoe MDL2 Assets", 16f);

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, 1f)) g.DrawPath(pen, p);
        }

        // Start = reading start side (right in Arabic, left in English)
        public enum Align { Start, Center, End }

        public static void Txt(Graphics g, string s, Font f, Color c, Rectangle r, Align a, bool vcenter, bool wrap)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextFormatFlags fl = TextFormatFlags.NoPrefix;
            fl |= wrap ? TextFormatFlags.WordBreak : (TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (vcenter) fl |= TextFormatFlags.VerticalCenter;
            if (L.Ar && HasArabic(s)) fl |= TextFormatFlags.RightToLeft;
            if (a == Align.Center) fl |= TextFormatFlags.HorizontalCenter;
            else if ((a == Align.Start) == L.Ar) fl |= TextFormatFlags.Right;
            else fl |= TextFormatFlags.Left;
            TextRenderer.DrawText(g, s, f, r, c, fl);
        }

        public static void Txt(Graphics g, string s, Font f, Color c, Rectangle r, Align a)
        {
            Txt(g, s, f, c, r, a, true, false);
        }

        // numbers, dates and Latin text keep Western digits and left-to-right order
        public static bool HasArabic(string s)
        {
            foreach (char ch in s) if (ch >= '\u0600' && ch <= '\u06FF') return true;
            return false;
        }

        public static int TextHeight(Graphics g, string s, Font f, int width)
        {
            TextFormatFlags fl = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            if (L.Ar) fl |= TextFormatFlags.RightToLeft;
            return TextRenderer.MeasureText(g, s, f, new Size(width, 10000), fl).Height;
        }

        // mirror a rectangle laid out left-to-right inside a container of width w (for Arabic)
        public static Rectangle M(Rectangle r, int w)
        {
            if (!L.Ar) return r;
            return new Rectangle(w - r.Right, r.Y, r.Width, r.Height);
        }
    }

    // ------------------------------------------------------------------ logo (vector, drawn at any size)
    static class Logo
    {
        static PointF P(RectangleF r, float x, float y) { return new PointF(r.X + r.Width * x, r.Y + r.Height * y); }

        public static void Draw(Graphics g, RectangleF r, Color shield)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                PointF tl = P(r, 0.14f, 0.10f), tr = P(r, 0.86f, 0.10f), rs = P(r, 0.86f, 0.50f), bt = P(r, 0.5f, 0.95f), ls = P(r, 0.14f, 0.50f);
                PointF c1 = P(r, 0.85f, 0.82f), c2 = P(r, 0.15f, 0.82f);
                path.AddLine(tl, tr);
                path.AddLine(tr, rs);
                path.AddBezier(rs, Lerp(rs, c1, 2f / 3f), Lerp(bt, c1, 2f / 3f), bt);
                path.AddBezier(bt, Lerp(bt, c2, 2f / 3f), Lerp(ls, c2, 2f / 3f), ls);
                path.CloseFigure();
                using (var b = new SolidBrush(shield)) g.FillPath(b, path);
            }
            PointF[] line = {
                P(r, 0.10f, 0.44f), P(r, 0.26f, 0.44f), P(r, 0.36f, 0.66f), P(r, 0.50f, 0.30f),
                P(r, 0.64f, 0.66f), P(r, 0.74f, 0.44f), P(r, 0.90f, 0.44f) };
            using (var pen = new Pen(Theme.Teal, Math.Max(1.5f, r.Width * 0.07f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, line);
            }
            g.SmoothingMode = old;
        }

        static PointF Lerp(PointF a, PointF b, float t) { return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
    }

    // ------------------------------------------------------------------ result row
    class Row
    {
        public bool Check;
        public string Name = "";
        public string Detail = "";
        public long Bytes;
        public string Date = "";
        public string Risk = "";
        public int Level;          // 0 = none, 1 = warning, 2 = danger, 3 = info (handled)
        public string Path = "";
        public string Action = ""; // what the action button does to this row
        public string Key = "";
        public string Section = "";

        public Row() { }
        public Row(string name, string detail, long bytes, string date, string path, string action, string key)
        {
            Name = name ?? ""; Detail = detail ?? ""; Bytes = bytes; Date = date ?? ""; Path = path ?? ""; Action = action ?? ""; Key = key ?? "";
        }
    }

    // ------------------------------------------------------------------ helpers
    static class U
    {
        public static string Size(long b)
        {
            if (b <= 0) return "";
            if (b >= 1L << 30) return (b / 1073741824.0).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / 1048576.0).ToString("0") + " MB";
            return Math.Max(1, b / 1024).ToString() + " KB";
        }

        public static string GB(long b) { return (b / 1073741824.0).ToString("0.0"); }

        public static string D(DateTime d) { return d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); }

        public static string Clock(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--:--";
            int s = (int)Math.Round(seconds);
            int h = s / 3600, m = (s % 3600) / 60, ss = s % 60;
            if (h > 0) return h + ":" + m.ToString("00") + ":" + ss.ToString("00");
            return m.ToString("00") + ":" + ss.ToString("00");
        }

        public static string Num(long n) { return n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture); }

        public static void ShowInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (File.Exists(path) || Directory.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                {
                    string dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
                }
            }
            catch { }
        }

        public static void Open(string target)
        {
            try { Process.Start(target); } catch (Exception ex) { Log.Write("open failed: " + target + " | " + ex.Message); }
        }

        public static long FolderSize(string path, ScanCtx ctx)
        {
            long total = 0;
            if (!Directory.Exists(path)) return 0;
            var st = new Stack<string>();
            st.Push(path);
            while (st.Count > 0)
            {
                if (ctx != null && ctx.Cancel) break;
                string d = st.Pop();
                try
                {
                    foreach (var f in new DirectoryInfo(d).EnumerateFileSystemInfos())
                    {
                        try
                        {
                            if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if ((f.Attributes & FileAttributes.Directory) != 0) { st.Push(f.FullName); continue; }
                            total += ((FileInfo)f).Length;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return total;
        }

        public static List<ManagementObject> Wmi(string ns, string query)
        {
            var list = new List<ManagementObject>();
            try
            {
                using (var s = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery(query)))
                {
                    foreach (ManagementObject o in s.Get()) list.Add(o);
                }
            }
            catch (Exception ex) { Log.Write("WMI " + ns + " | " + query + " | " + ex.Message); }
            return list;
        }

        public static string S(ManagementObject o, string prop)
        {
            try { object v = o[prop]; return v == null ? "" : v.ToString().Trim(); } catch { return ""; }
        }

        public static long N(ManagementObject o, string prop)
        {
            try { object v = o[prop]; return v == null ? 0 : Convert.ToInt64(v); } catch { return 0; }
        }

        public static DateTime WmiDate(string s)
        {
            try { if (!string.IsNullOrEmpty(s)) return ManagementDateTimeConverter.ToDateTime(s); } catch { }
            return DateTime.MinValue;
        }
    }

    // ------------------------------------------------------------------ safety: paths that are never touched
    static class Safety
    {
        static List<string> protectedPaths;

        static string Norm(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            return p.Trim().TrimEnd('\\').ToLowerInvariant();
        }

        static void Init()
        {
            if (protectedPaths != null) return;
            var l = new List<string>();
            Func<Environment.SpecialFolder, string> sf = Environment.GetFolderPath;
            string[] raw = {
                Environment.GetEnvironmentVariable("windir"), Environment.GetEnvironmentVariable("SystemDrive") + "\\",
                sf(Environment.SpecialFolder.ProgramFiles), sf(Environment.SpecialFolder.ProgramFilesX86),
                sf(Environment.SpecialFolder.CommonApplicationData), sf(Environment.SpecialFolder.UserProfile),
                sf(Environment.SpecialFolder.Desktop), sf(Environment.SpecialFolder.MyDocuments),
                System.IO.Path.Combine(sf(Environment.SpecialFolder.UserProfile), "Downloads"),
                sf(Environment.SpecialFolder.ApplicationData), sf(Environment.SpecialFolder.LocalApplicationData),
                sf(Environment.SpecialFolder.MyPictures), sf(Environment.SpecialFolder.MyVideos), sf(Environment.SpecialFolder.MyMusic),
                System.IO.Path.GetDirectoryName(Application.ExecutablePath), AppInfo.DataDir,
                System.IO.Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") + "\\", "Users")
            };
            foreach (string r in raw) { string n = Norm(r); if (n.Length > 0 && !l.Contains(n)) l.Add(n); }
            protectedPaths = l;
        }

        public static bool IsSafe(string p)
        {
            Init();
            string l = Norm(p);
            if (l.Length < 4) return false;                        // drive roots
            if (protectedPaths.Contains(l)) return false;
            string win = Norm(Environment.GetEnvironmentVariable("windir"));
            if (l.StartsWith(win + "\\")) return false;
            if (Regex.IsMatch(l, @"\\(system volume information|\$recycle\.bin|recovery|boot|efi)(\\|$)")) return false;
            if (l.EndsWith("\\pagefile.sys") || l.EndsWith("\\hiberfil.sys") || l.EndsWith("\\swapfile.sys")) return false;
            string self = Norm(Application.ExecutablePath);
            if (l == self) return false;
            return true;
        }

        // inside folders that belong to installed software / the system
        public static bool IsSystemOwned(string p)
        {
            string l = Norm(p);
            Func<Environment.SpecialFolder, string> sf = Environment.GetFolderPath;
            string[] roots = { sf(Environment.SpecialFolder.ProgramFiles), sf(Environment.SpecialFolder.ProgramFilesX86),
                sf(Environment.SpecialFolder.CommonApplicationData), Environment.GetEnvironmentVariable("windir"),
                sf(Environment.SpecialFolder.ApplicationData), sf(Environment.SpecialFolder.LocalApplicationData),
                System.IO.Path.Combine(sf(Environment.SpecialFolder.UserProfile), "AppData") };
            foreach (string r in roots)
            {
                string n = Norm(r);
                if (n.Length > 0 && (l == n || l.StartsWith(n + "\\"))) return true;
            }
            return Regex.IsMatch(l, @"\\(steamapps|steamlibrary|xboxgames|windowsapps|epic games|\$recycle\.bin|system volume information)(\\|$)");
        }
    }

    // ------------------------------------------------------------------ risk heuristics for autostart commands
    static class Risk
    {
        public static string ForCommand(string cmd, out int level)
        {
            level = 0;
            var r = new List<string>();
            if (string.IsNullOrEmpty(cmd)) return "";
            if (Regex.IsMatch(cmd, @"(?i)wscript|cscript|mshta|\.vbs\b|\.js\b|\.jse\b|\.hta\b")) { r.Add(L.T("يشغّل سكربت", "Runs a script")); level = 2; }
            if (Regex.IsMatch(cmd, @"(?i)powershell.*(-enc|-e\s|frombase64|iex|downloadstring|invoke-expression|-w(indowstyle)?\s+hidden)")) { r.Add(L.T("PowerShell مخفي/مشفّر", "Hidden/encoded PowerShell")); level = 2; }
            if (Regex.IsMatch(cmd, @"(?i)\\appdata\\local\\temp\\|\\windows\\temp\\|\\users\\public\\|\\programdata\\[^\\]+\.(exe|vbs|js|bat|cmd)")) { r.Add(L.T("من مجلد مؤقت/عام", "From a temp/public folder")); level = Math.Max(level, 1); }
            if (Regex.IsMatch(cmd, @"[Ѐ-ӿᴀ-ᶿ​-‏]")) { r.Add(L.T("اسم مموّه بحروف مشابهة", "Look-alike characters in name")); level = 2; }

            string exe = ExePath(cmd);
            if (exe != null)
            {
                string x = Environment.ExpandEnvironmentVariables(exe);
                if (Regex.IsMatch(x, @"^[A-Za-z]:\\") && !File.Exists(x)) { r.Add(L.T("الملف غير موجود (بقايا)", "File is missing (leftover)")); level = Math.Max(level, 1); }
                else if (x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(x) && Regex.IsMatch(x, @"(?i)\\appdata\\|\\temp\\|\\programdata\\") && !IsSigned(x))
                { r.Add(L.T("غير موقّع رقمياً", "Not digitally signed")); level = Math.Max(level, 1); }
            }
            return string.Join(" · ", r.ToArray());
        }

        public static string ExePath(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            Match m = Regex.Match(cmd, "^\\s*\"([^\"]+)\"");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(cmd, @"^\s*(.+?\.(exe|vbs|js|bat|cmd|com|scr))(\s|$)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
            return null;
        }

        public static bool IsSigned(string file)
        {
            try { System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file); return true; }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------ native
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        public static long RecycleBinSize()
        {
            var info = new SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
            try { if (SHQueryRecycleBin(null, ref info) == 0) return info.i64Size; } catch { }
            return 0;
        }
    }
}
