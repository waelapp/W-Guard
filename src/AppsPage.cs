// W-Guard - essential apps: install or update the programs you pick, from their official sources through winget
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Net;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WGuard
{
    class AppDef
    {
        public string Id, Name, Publisher, Cat, Initials;
        public Color Color;
        public bool Essential, OpenSource;
        public string Match = "", IconSite = "";   // name in "Installed apps", and the official site its icon comes from
        public AppDef(string cat, string id, string name, string publisher, string initials, string color, bool essential, bool open)
        {
            Cat = cat; Id = id; Name = name; Publisher = publisher; Initials = initials; Color = Theme.Hex(color); Essential = essential; OpenSource = open;
        }
        public AppDef M(string match, string site) { Match = match; IconSite = site; return this; }
    }

    // talks to the Windows Package Manager (winget). Every program comes from its official source and is hash-checked by winget.
    static class Winget
    {
        public static bool Available, Checked;
        public static string Version = "";
        public static readonly HashSet<string> Installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> Upgradable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly object gate = new object();

        // installed / update results that only mean "nothing to do"
        const int InstallerFailed = unchecked((int)0x8A150006), AlreadyInstalled = unchecked((int)0x8A150061), NoUpdate = unchecked((int)0x8A15002B), RestartNeeded = unchecked((int)0x8A150109), RebootInit = unchecked((int)0x8A15010A), NotFound = unchecked((int)0x8A150014);

        public static string Exe
        {
            get
            {
                string alias = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe");
                if (File.Exists(alias)) return alias;
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string d in path.Split(';'))
                {
                    try { if (d.Trim().Length == 0) continue; string f = Path.Combine(d.Trim(), "winget.exe"); if (File.Exists(f)) return f; } catch { }
                }
                return "winget.exe";
            }
        }

        public static int Run(string args, out string output, int timeoutMs)
        {
            var sb = new StringBuilder();
            output = "";
            try
            {
                var psi = new ProcessStartInfo(Exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        lock (sb) output = sb.ToString();
                        return -2;
                    }
                    p.WaitForExit();
                    lock (sb) output = sb.ToString();
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }

        // like Run, but reads the output as it comes so the download bar ("12.5 MB / 80.0 MB" or "45%") can be followed
        static readonly Regex SizeRx = new Regex(@"([\d.,]+)\s*(KB|MB|GB)\s*/\s*([\d.,]+)\s*(KB|MB|GB)", RegexOptions.IgnoreCase);
        static readonly Regex PctRx = new Regex(@"(\d{1,3})\s*%");

        static double ToBytes(string n, string unit)
        {
            double v;
            if (!double.TryParse(n.Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return 0;
            switch (unit.ToUpperInvariant()) { case "GB": return v * 1073741824; case "MB": return v * 1048576; default: return v * 1024; }
        }

        public static volatile Process Current;
        [System.Runtime.InteropServices.DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);
        [System.Runtime.InteropServices.DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr h);

        // winget and the setup it started (the setup runs as its child)
        static List<int> Tree(int pid)
        {
            var l = new List<int> { pid };
            for (int i = 0; i < l.Count && i < 64; i++)
                foreach (var o in U.Wmi(@"root\cimv2", "SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = " + l[i]))
                    l.Add((int)U.N(o, "ProcessId"));
            return l;
        }

        public static void Suspend(bool on)
        {
            var p = Current;
            if (p == null) return;
            try
            {
                foreach (int id in Tree(p.Id))
                    try { using (var q = Process.GetProcessById(id)) { if (on) NtSuspendProcess(q.Handle); else NtResumeProcess(q.Handle); } } catch { }
            }
            catch { }
        }

        public static void KillCurrent()
        {
            var p = Current;
            if (p == null) return;
            try
            {
                var t = Tree(p.Id);
                t.Reverse();
                foreach (int id in t) try { using (var q = Process.GetProcessById(id)) q.Kill(); } catch { }
                Log.Write("stopped by the user: winget pid " + p.Id);
            }
            catch { }
        }

        public static long Downloaded, TotalBytes;   // what the running download has saved so far, and its full size (0 = unknown)

        // winget does not draw its progress bar when its output is captured, so the download is followed on disk:
        // the size comes from the server, the bytes from the file growing in winget's download folder
        static Thread StartPoller(string url, Action<double> progress, Func<bool> done)
        {
            var started = DateTime.Now.AddSeconds(-5);
            var th = new Thread(() =>
            {
                try
                {
                    var rq = (HttpWebRequest)WebRequest.Create(url);
                    rq.Method = "HEAD"; rq.AllowAutoRedirect = true; rq.Timeout = 15000;
                    rq.UserAgent = "winget-cli";
                    using (var rs = (HttpWebResponse)rq.GetResponse()) if (rs.ContentLength > 0) TotalBytes = rs.ContentLength;
                }
                catch (Exception ex) { Log.Write("download size: " + ex.Message); }
                string root = Path.Combine(Path.GetTempPath(), "WinGet");
                while (!done())
                {
                    try
                    {
                        long best = 0;
                        if (Directory.Exists(root))
                            foreach (var f in new DirectoryInfo(root).GetFiles("*", SearchOption.AllDirectories))
                                if (f.LastWriteTime >= started && f.Length > best) best = f.Length;
                        if (best > 0)
                        {
                            Downloaded = best;
                            if (TotalBytes > 0) progress(Math.Min(0.99, (double)best / TotalBytes));
                        }
                    }
                    catch { }
                    Thread.Sleep(400);
                }
            });
            th.IsBackground = true;
            th.Start();
            return th;
        }

        public static int RunLive(string args, out string output, int timeoutMs, Action<double> progress)
        {
            var sb = new StringBuilder();
            output = "";
            try
            {
                var psi = new ProcessStartInfo(Exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = new Process { StartInfo = psi })
                {
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    Current = p;
                    p.BeginErrorReadLine();
                    Thread poller = null;
                    bool phaseDone = false;
                    Downloaded = 0; TotalBytes = 0;
                    var reader = new Thread(() =>
                    {
                        var buf = new char[512];
                        var seg = new StringBuilder();
                        try
                        {
                            int n;
                            while ((n = p.StandardOutput.Read(buf, 0, buf.Length)) > 0)
                            {
                                for (int i = 0; i < n; i++)
                                {
                                    char c = buf[i];
                                    if (c == '\r' || c == '\n')
                                    {
                                        string line = seg.ToString();
                                        seg.Length = 0;
                                        if (line.Trim().Length == 0) continue;
                                        lock (sb) sb.AppendLine(line);
                                        if (progress == null) continue;
                                        int http = line.IndexOf("http", StringComparison.OrdinalIgnoreCase);
                                        if (http >= 0 && poller == null) { poller = StartPoller(line.Substring(http).Trim(), progress, () => phaseDone); continue; }
                                        if (line.IndexOf("hash", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Starting package install", StringComparison.OrdinalIgnoreCase) >= 0) { phaseDone = true; progress(1); continue; }
                                        var m = SizeRx.Match(line);
                                        if (m.Success)
                                        {
                                            double done = ToBytes(m.Groups[1].Value, m.Groups[2].Value), total = ToBytes(m.Groups[3].Value, m.Groups[4].Value);
                                            if (total > 0) progress(Math.Min(1, done / total));
                                            continue;
                                        }
                                        var q = PctRx.Match(line);
                                        if (q.Success) { int v = int.Parse(q.Groups[1].Value); if (v <= 100) progress(v / 100.0); }
                                    }
                                    else seg.Append(c);
                                }
                            }
                        }
                        catch { }
                        if (seg.Length > 0) lock (sb) sb.AppendLine(seg.ToString());
                    });
                    reader.IsBackground = true;
                    reader.Start();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        phaseDone = true;
                        Current = null;
                        reader.Join(2000);
                        lock (sb) output = sb.ToString();
                        return -2;
                    }
                    reader.Join(5000);
                    p.WaitForExit();
                    phaseDone = true;
                    Current = null;
                    lock (sb) output = sb.ToString();
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { Current = null; output = ex.Message; return -1; }
        }

        // reads which catalog programs are installed and which have an update waiting
        public static void Refresh(List<AppDef> catalog)
        {
            lock (gate)
            {
                string o;
                int v = Run("--version", out o, 30000);
                Available = v == 0 && o.Trim().Length > 0;
                Version = Available ? o.Trim() : "";
                var inst = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var upd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (Available)
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "wguard-winget-" + Guid.NewGuid().ToString("N") + ".json");
                    int e = Run("export -o \"" + tmp + "\" --accept-source-agreements", out o, 180000);
                    bool gotExport = false;
                    try
                    {
                        if (File.Exists(tmp))
                        {
                            string json = File.ReadAllText(tmp);
                            foreach (Match m in Regex.Matches(json, "\"PackageIdentifier\"\\s*:\\s*\"([^\"]+)\"")) inst.Add(m.Groups[1].Value);
                            gotExport = true;
                            File.Delete(tmp);
                        }
                    }
                    catch { }
                    if (!gotExport) Log.Write("winget export failed (" + e + ")");
                    // the list view also catches programs the export leaves out
                    Run("list --accept-source-agreements", out o, 180000);
                    foreach (var a in catalog) if (LineHas(o, a.Id)) inst.Add(a.Id);
                    Run("upgrade --accept-source-agreements", out o, 180000);
                    foreach (var a in catalog) if (LineHas(o, a.Id)) upd.Add(a.Id);
                }
                // programs installed by their own setup (not known to winget) still count as installed
                foreach (var kv in InstalledApps.Read())
                    foreach (var a in catalog) if (InstalledApps.Matches(a, kv.Key)) { inst.Add(a.Id); AppIcons.SetExeIcon(a.Id, kv.Value); }
                lock (Installed) { Installed.Clear(); foreach (var s in inst) Installed.Add(s); Upgradable.Clear(); foreach (var s in upd) Upgradable.Add(s); }
                Checked = true;
                Log.Write("winget " + (Available ? Version : "not available") + " | installed from list: " + CountIn(catalog, Installed) + " | updates: " + CountIn(catalog, Upgradable));
            }
        }

        static int CountIn(List<AppDef> c, HashSet<string> s) { int n = 0; foreach (var a in c) if (s.Contains(a.Id)) n++; return n; }

        // winget prints a table; an id is matched as a whole word (long ids may be cut and end with "…")
        static bool LineHas(string text, string id)
        {
            foreach (string raw in text.Split('\n'))
            {
                foreach (string tok in raw.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.Equals(tok, id, StringComparison.OrdinalIgnoreCase)) return true;
                    if (tok.Length > 8 && tok.EndsWith("…") && id.StartsWith(tok.Substring(0, tok.Length - 1), StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        public static bool IsInstalled(string id) { lock (Installed) return Installed.Contains(id); }
        // launchers that keep themselves up to date when opened. Updating them through winget removes the old
        // version first, and if their own installer then fails the launcher is gone - so W-Guard leaves them alone.
        public static readonly HashSet<string> SelfUpdating = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Valve.Steam", "EpicGames.EpicGamesLauncher", "ElectronicArts.EADesktop", "Ubisoft.Connect", "Blizzard.BattleNet", "Discord.Discord", "Microsoft.Edge"
        };
        public static bool HasUpdate(string id) { if (SelfUpdating.Contains(id)) return false; lock (Installed) return Upgradable.Contains(id); }

        // installs (or updates, when it is already there) one program silently. Returns true on success.
        public static bool Apply(AppDef a, out string message, Action<double> progress)
        {
            bool upgrade = IsInstalled(a.Id);
            string verb = upgrade ? "upgrade" : "install";
            string o;
            int code = RunLive(verb + " --id " + a.Id + " -e --source winget --silent --accept-package-agreements --accept-source-agreements", out o, 30 * 60000, progress);
            Log.Write("winget " + verb + " " + a.Id + " -> " + code + "\r\n" + Tail(o, 12));
            if (upgrade && code == NotFound)
            {
                // installed by its own setup, so winget cannot match it: installing over it updates it
                verb = "install";
                code = RunLive("install --id " + a.Id + " -e --source winget --silent --accept-package-agreements --accept-source-agreements", out o, 30 * 60000, progress);
                Log.Write("winget install " + a.Id + " -> " + code + "\r\n" + Tail(o, 12));
            }
            if (code == 0 || code == AlreadyInstalled || code == NoUpdate || code == RestartNeeded || code == RebootInit)
            {
                message = code == RestartNeeded || code == RebootInit ? L.T("تم - يحتاج إعادة تشغيل الجهاز", "Done - restart needed") : upgrade ? L.T("تم التحديث", "Updated") : L.T("تم التثبيت", "Installed");
                return true;
            }
            if (code == -2) message = L.T("انتهت المهلة", "Timed out");
            else if (code == InstallerFailed && o.IndexOf("Successfully uninstalled", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // winget removed the old version, then the program's own installer failed
                lock (Installed) { Installed.Remove(a.Id); Upgradable.Remove(a.Id); }
                message = L.T("أُزيلت النسخة القديمة وفشل مثبّت الجديدة - أعد تشغيل الجهاز ثم ثبّته", "Old version removed but its installer failed - restart, then install it");
            }
            else if (code == InstallerFailed) message = L.T("فشل مثبّت البرنامج نفسه - أعد تشغيل الجهاز ثم جرّب", "The app's own installer failed - restart, then try again");
            else message = L.T("لم ينجح", "Failed") + " (0x" + code.ToString("X8") + ")";
            return false;
        }

        static string Tail(string s, int lines)
        {
            var all = s.Replace("\r", "").Split('\n');
            var keep = new List<string>();
            for (int i = all.Length - 1; i >= 0 && keep.Count < lines; i--) { string t = all[i].Trim(); if (t.Length > 0 && t.IndexOfAny(new[] { '█', '▒' }) < 0) keep.Insert(0, t); }
            return string.Join("\r\n", keep.ToArray());
        }

        public static void LoadDemo()
        {
            AppIcons.LoadInBackground(AppsPage.Catalog, null);
            Available = true; Checked = true; Version = "v1.12.350";
            lock (Installed)
            {
                Installed.Clear(); Upgradable.Clear();
                foreach (string s in new[] { "Microsoft.Edge", "EpicGames.EpicGamesLauncher", "Microsoft.VCRedist.2015+.x64", "OBSProject.OBSStudio", "Notepad++.Notepad++", "Telegram.TelegramDesktop" }) Installed.Add(s);
                foreach (string s in new[] { "OBSProject.OBSStudio", "Notepad++.Notepad++", "Telegram.TelegramDesktop" }) Upgradable.Add(s);
            }
        }
    }

    // reads "Installed apps" (the uninstall list) for names and icons
    static class InstalledApps
    {
        public static List<KeyValuePair<string, string>> Read()
        {
            var list = new List<KeyValuePair<string, string>>();
            var roots = new[] { new[] { "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" }, new[] { "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" }, new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Uninstall" } };
            foreach (var r in roots)
            {
                try
                {
                    var hive = r[0] == "HKLM" ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64) : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                    using (hive)
                    using (var k = hive.OpenSubKey(r[1]))
                    {
                        if (k == null) continue;
                        foreach (string sub in k.GetSubKeyNames())
                        {
                            try
                            {
                                using (var e = k.OpenSubKey(sub))
                                {
                                    string n = e == null ? null : e.GetValue("DisplayName") as string;
                                    if (string.IsNullOrEmpty(n)) continue;
                                    string icon = e.GetValue("DisplayIcon") as string ?? "";
                                    list.Add(new KeyValuePair<string, string>(n.Trim(), icon));
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        // "=Name" exact, "*a|b" contains both parts, otherwise starts with
        public static bool Matches(AppDef a, string name)
        {
            string m = a.Match;
            if (string.IsNullOrEmpty(m)) return false;
            if (m[0] == '=') return string.Equals(name, m.Substring(1), StringComparison.OrdinalIgnoreCase);
            if (m[0] == '*') { foreach (string part in m.Substring(1).Split('|')) if (name.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) return false; return true; }
            if (!name.StartsWith(m, StringComparison.OrdinalIgnoreCase)) return false;
            // "Steam" must not match "SteamVR", "Brave" must not match "Brave Nightly"
            if (name.Length == m.Length) return true;
            char c = name[m.Length];
            return !char.IsLetterOrDigit(c) && name.IndexOf("Nightly", StringComparison.OrdinalIgnoreCase) < 0 && name.IndexOf("Beta", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }

    // each program's real icon: from its own installed file, or its official site's icon (saved once, then used offline)
    static class AppIcons
    {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> exePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool loading, again;
        static string Dir { get { return Path.Combine(AppInfo.DataDir, "icons"); } }
        static string FileFor(string id) { foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_'); return Path.Combine(Dir, id.Replace('+', 'p') + ".png"); }

        static bool Again() { lock (cache) return again; }

        public static Image Get(string id) { lock (cache) { Image i; return cache.TryGetValue(id, out i) ? i : null; } }

        public static void SetExeIcon(string id, string displayIcon)
        {
            if (string.IsNullOrEmpty(displayIcon)) return;
            string p = displayIcon.Trim().Trim('"');
            int comma = p.LastIndexOf(',');
            if (comma > 2 && !File.Exists(p)) p = p.Substring(0, comma).Trim().Trim('"');
            if (!File.Exists(p)) return;
            lock (exePaths) exePaths[id] = p;
        }

        static void Put(string id, Image img) { lock (cache) { Image old; if (cache.TryGetValue(id, out old) && old != null) old.Dispose(); cache[id] = img; } }

        // saved icons first, then the program's own file, then the site icon in the background
        public static void LoadInBackground(List<AppDef> apps, Action changed)
        {
            lock (cache) { if (loading) { again = true; return; } loading = true; again = false; }
            var th = new Thread(() =>
            {
                try
                {
                  do
                  {
                    lock (cache) again = false;
                    try { Directory.CreateDirectory(Dir); } catch { }
                    bool any = false;
                    foreach (var a in apps)
                    {
                        if (Get(a.Id) != null) continue;
                        string f = FileFor(a.Id);
                        try { if (File.Exists(f)) { using (var fs = File.OpenRead(f)) Put(a.Id, new Bitmap(Image.FromStream(fs))); any = true; continue; } } catch { }
                        string exe; lock (exePaths) exePaths.TryGetValue(a.Id, out exe);
                        if (exe != null) try { using (var ic = Icon.ExtractAssociatedIcon(exe)) if (ic != null) { Put(a.Id, ic.ToBitmap()); any = true; } } catch { }
                    }
                    if (any && changed != null) changed();
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    foreach (var a in apps)
                    {
                        if (a.IconSite.Length == 0 || File.Exists(FileFor(a.Id))) continue;
                        try
                        {
                            using (var wc = new WebClient())
                            {
                                wc.Headers[HttpRequestHeader.UserAgent] = AppInfo.Name + "/" + AppInfo.VersionText;
                                byte[] data = wc.DownloadData("https://www.google.com/s2/favicons?sz=64&domain_url=" + Uri.EscapeDataString("https://" + a.IconSite));
                                using (var ms = new MemoryStream(data))
                                using (var img = Image.FromStream(ms))
                                {
                                    if (img.Width < 32) continue;   // the generic globe comes back small: keep the program's own icon
                                    var bmp = new Bitmap(img);
                                    bmp.Save(FileFor(a.Id), System.Drawing.Imaging.ImageFormat.Png);
                                    Put(a.Id, bmp);
                                }
                            }
                            if (changed != null) changed();
                        }
                        catch (Exception ex) { Log.Write("icon " + a.Id + ": " + ex.Message); }
                    }
                  } while (Again());
                }
                finally { lock (cache) loading = false; }
            });
            th.IsBackground = true;
            th.Start();
        }
    }

    class AppsPage : Panel, IPage
    {
        readonly MainForm main;
        readonly Panel scroll, footer;
        readonly AppsCanvas canvas;
        readonly FlatButton installBtn, clearBtn;
        static bool checking;

        public static readonly List<AppDef> Catalog = new List<AppDef>
        {
            new AppDef("browsers", "Google.Chrome", "Google Chrome", "Google", "C", "1A73E8", true, false).M("Google Chrome", "chrome.com"),
            new AppDef("browsers", "Mozilla.Firefox", "Mozilla Firefox", "Mozilla", "F", "E66000", false, true).M("Mozilla Firefox", "firefox.com"),
            new AppDef("browsers", "Brave.Brave", "Brave", "Brave Software", "B", "FB542B", false, true).M("Brave", "brave.com"),
            new AppDef("browsers", "Opera.OperaGX", "Opera GX", "Opera", "GX", "C4113B", false, false).M("Opera GX", "operagx.gg"),
            new AppDef("browsers", "Microsoft.Edge", "Microsoft Edge", "Microsoft", "E", "0C59A4", false, false).M("=Microsoft Edge", "microsoftedge.com"),
            new AppDef("chat", "Discord.Discord", "Discord", "Discord Inc.", "D", "5865F2", true, false).M("Discord", "discord.com"),
            new AppDef("chat", "Telegram.TelegramDesktop", "Telegram", "Telegram", "T", "26A5E4", false, true).M("Telegram Desktop", "telegram.org"),
            new AppDef("chat", "Zoom.Zoom", "Zoom", "Zoom", "Z", "0B5CFF", false, false).M("Zoom", "zoom.us"),
            new AppDef("gaming", "Valve.Steam", "Steam", "Valve", "S", "1B2838", false, false).M("Steam", "store.steampowered.com"),
            new AppDef("gaming", "EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Epic Games", "E", "2A2A2A", false, false).M("Epic Games Launcher", "store.epicgames.com/en-US/"),
            new AppDef("gaming", "ElectronicArts.EADesktop", "EA app", "Electronic Arts", "EA", "FF4747", false, false).M("EA app", "help.ea.com"),
            new AppDef("gaming", "Ubisoft.Connect", "Ubisoft Connect", "Ubisoft", "U", "0070CC", false, false).M("Ubisoft Connect", "www.ubisoft.com"),
            new AppDef("gaming", "Blizzard.BattleNet", "Battle.net", "Blizzard", "B", "148EFF", false, false).M("Battle.net", "battle.net"),
            new AppDef("runtime", "Microsoft.VCRedist.2015+.x64", "Visual C++ 2015-2022 (x64)", "Microsoft", "C++", "68217A", true, false).M("*Visual C++ 2015|Redistributable (x64)", "visualstudio.microsoft.com"),
            new AppDef("runtime", "Microsoft.DirectX", "DirectX End-User Runtime", "Microsoft", "DX", "107C10", true, false).M("", "microsoft.com"),
            new AppDef("media", "VideoLAN.VLC", "VLC media player", "VideoLAN", "V", "FF8800", true, true).M("VLC media player", "www.videolan.org"),
            new AppDef("media", "OBSProject.OBSStudio", "OBS Studio", "OBS Project", "O", "302E31", false, true).M("OBS Studio", "obsproject.com"),
            new AppDef("tools", "7zip.7zip", "7-Zip", "Igor Pavlov", "7z", "4A4A4A", true, true).M("7-Zip", "7-zip.org"),
            new AppDef("tools", "RARLab.WinRAR", "WinRAR", "RARLab", "R", "6B3FA0", false, false).M("WinRAR", "win-rar.com"),
            new AppDef("tools", "Notepad++.Notepad++", "Notepad++", "Don Ho", "N", "3E8E41", false, true).M("Notepad++", "notepad-plus-plus.org"),
            new AppDef("tools", "Microsoft.PowerToys", "PowerToys", "Microsoft", "PT", "2B2B6B", false, true).M("PowerToys", ""),
            new AppDef("tools", "ShareX.ShareX", "ShareX", "ShareX Team", "SX", "2D8CEB", false, true).M("ShareX", "getsharex.com"),
        };

        public static readonly string[] Cats = { "browsers", "chat", "gaming", "runtime", "media", "tools" };
        public static string CatName(string c)
        {
            switch (c)
            {
                case "browsers": return L.T("المتصفحات", "Browsers");
                case "chat": return L.T("التواصل", "Communication");
                case "gaming": return L.T("منصات الألعاب", "Game platforms");
                case "runtime": return L.T("مكتبات الألعاب", "Game runtimes");
                case "media": return L.T("الوسائط", "Media");
                case "tools": return L.T("الأدوات", "Tools");
            }
            return c;
        }
        public static string CatHint(string c)
        {
            switch (c)
            {
                case "gaming": return L.T("تنزّل منها ألعابك", "Where you get your games");
                case "runtime": return L.T("تحتاجها أغلب الألعاب", "Most games need these");
            }
            return "";
        }

        public static readonly HashSet<string> Selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public AppsPage(MainForm owner)
        {
            main = owner;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            Padding = new Padding(28, 0, 28, 16);

            footer = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Theme.Bg, Padding = new Padding(0, 12, 0, 0) };
            var actions = new FlowLayoutPanel { Dock = L.Ar ? DockStyle.Left : DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(10, 10, 10, 0) };
            clearBtn = new FlatButton(L.T("مسح التحديد", "Clear selection"), FlatButton.Kind.Secondary, "");
            clearBtn.Click += (s, e) => { Selected.Clear(); Changed(); };
            installBtn = new FlatButton(L.T("تثبيت المحدد", "Install selected"), FlatButton.Kind.Primary, Glyph.Download);
            installBtn.Click += (s, e) => RunSelected(null);
            if (L.Ar) { actions.Controls.Add(installBtn); actions.Controls.Add(clearBtn); } else { actions.Controls.Add(clearBtn); actions.Controls.Add(installBtn); }
            footer.Controls.Add(actions);
            footer.Paint += PaintFooter;

            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            canvas = new AppsCanvas(this) { Dock = DockStyle.Top };
            scroll.Controls.Add(canvas);
            canvas.MouseEnter += (s, e) => scroll.Focus();

            Controls.Add(scroll);
            Controls.Add(footer);
            Changed();
        }

        void PaintFooter(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 8, footer.Width - 1, footer.Height - 10);
            Theme.FillRound(g, Theme.Card, r, 12);
            Theme.StrokeRound(g, Theme.Border, r, 12);
            int n = 0, upd = 0;
            foreach (var a in Catalog) if (Selected.Contains(a.Id)) { n++; if (Winget.IsInstalled(a.Id)) upd++; }
            string t = n == 0 ? L.T("اختر البرامج اللي تحتاجها", "Pick the programs you need")
                : L.T(n + " محدد", n + " selected") + (upd > 0 ? L.T("  ·  منها " + upd + " تحديث", "  ·  " + upd + " of them updates") : "");
            string sub = L.T("من مصادرها الرسمية عبر winget", "From their official sources via winget");
            int aw = installBtn.Width + clearBtn.Width + 50;
            Theme.Txt(g, t, Theme.Bold, Theme.Text, Theme.M(new Rectangle(18, 14, footer.Width - aw - 30, 24), footer.Width), Theme.Align.Start);
            Theme.Txt(g, sub, Theme.Small, Theme.Muted, Theme.M(new Rectangle(18, 38, footer.Width - aw - 30, 20), footer.Width), Theme.Align.Start);
        }

        public void Changed()
        {
            int n = 0;
            foreach (var a in Catalog) if (Selected.Contains(a.Id)) n++;
            installBtn.Text = n == 0 ? L.T("تثبيت المحدد", "Install selected") : L.T("تثبيت المحدد (" + n + ")", "Install selected (" + n + ")");
            installBtn.Enabled = n > 0 && Winget.Available && !checking;
            clearBtn.Enabled = n > 0;
            footer.Invalidate();
            canvas.Relayout();
            canvas.Invalidate();
        }

        public bool Checking { get { return checking; } }

        public static bool Selectable(AppDef a)
        {
            if (!Winget.Available) return false;
            return !Winget.IsInstalled(a.Id) || Winget.HasUpdate(a.Id);
        }

        public void Toggle(AppDef a)
        {
            if (!Selectable(a) || checking) return;
            if (!Selected.Remove(a.Id)) Selected.Add(a.Id);
            Changed();
        }

        void SelectEssentials()
        {
            bool hasBrowser = false;
            foreach (var a in Catalog) if (a.Cat == "browsers" && a.Id != "Microsoft.Edge" && Winget.IsInstalled(a.Id)) hasBrowser = true;
            foreach (var a in Catalog)
            {
                if (!a.Essential || Winget.IsInstalled(a.Id)) continue;
                if (a.Cat == "browsers" && hasBrowser) continue;
                Selected.Add(a.Id);
            }
            Changed();
        }

        int UpdateCount { get { int n = 0; foreach (var a in Catalog) if (Winget.HasUpdate(a.Id)) n++; return n; } }

        public void CheckState(bool force)
        {
            if (checking) return;
            if (Winget.Checked && !force) return;
            if (Demo.On) { Winget.LoadDemo(); if (Selected.Count == 0) foreach (string s in new[] { "Google.Chrome", "Discord.Discord", "Valve.Steam", "VideoLAN.VLC", "7zip.7zip" }) Selected.Add(s); Changed(); return; }
            checking = true;
            Changed();
            var th = new Thread(() =>
            {
                try { Winget.Refresh(Catalog); } catch (Exception ex) { Log.Write("winget refresh: " + ex.Message); }
                checking = false;
                try { BeginInvoke((Action)(() => { LoadIcons(); var keep = new List<string>(Selected); foreach (string id in keep) { AppDef a = Find(id); if (a == null || !Selectable(a)) Selected.Remove(id); } main.RefreshAll(); })); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        public static void DrawIcon(Graphics g, AppDef a, Rectangle r)
        {
            var img = AppIcons.Get(a.Id);
            if (img != null)
            {
                // a light tile keeps dark logos (Ubisoft, Epic) visible in the dark look too
                Theme.FillRound(g, Theme.Dark ? Theme.Hex("E8ECF2") : Theme.Hex("F1F3F6"), r, 8);
                var q = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                int inset = Math.Max(3, r.Width / 9);
                g.DrawImage(img, new Rectangle(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2));
                g.InterpolationMode = q;
                return;
            }
            Theme.FillRound(g, a.Color, r, 8);
            using (var f = new Font("Segoe UI Semibold", a.Initials.Length > 2 ? 8f : 10f))
                TextRenderer.DrawText(g, a.Initials, f, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        void LoadIcons()
        {
            AppIcons.LoadInBackground(Catalog, () => { try { BeginInvoke((Action)(() => canvas.Invalidate())); } catch { } });
        }

        static AppDef Find(string id) { foreach (var a in Catalog) if (string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) return a; return null; }

        void RunSelected(List<AppDef> only)
        {
            var list = new List<AppDef>();
            if (only != null) list.AddRange(only);
            else foreach (var a in Catalog) if (Selected.Contains(a.Id) && Selectable(a)) list.Add(a);
            if (list.Count == 0) return;
            var names = new List<string>();
            foreach (var a in list) names.Add((Winget.IsInstalled(a.Id) ? L.T("تحديث: ", "Update: ") : "") + a.Name);
            string text = L.T("سيتم تنزيل البرامج التالية من مصادرها الرسمية وتثبيتها:\n\n", "These programs will be downloaded from their official sources and installed:\n\n")
                + string.Join("\n", names.ToArray())
                + L.T("\n\nقد يستغرق ذلك عدة دقائق حسب سرعة الإنترنت. متابعة؟", "\n\nThis can take a few minutes depending on your connection. Continue?");
            if (Msg.Show(this, text, L.T("تثبيت البرامج", "Install programs"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            using (var f = new InstallForm(list)) f.ShowDialog(this);
            foreach (var a in list) Selected.Remove(a.Id);
            main.RefreshAll();
        }

        public string Title { get { return L.T("البرامج المهمة", "Essential apps"); } }
        public string Subtitle
        {
            get
            {
                return L.T("اختر البرامج اللي تحتاجها بعد الفرمتة، ونثبّتها كلها بضغطة", "Pick what you need after a fresh install and get it all in one click");
            }
        }

        public Control[] HeaderActions
        {
            get
            {
                var ess = new FlatButton(L.T("تحديد الأساسيات", "Select essentials"), FlatButton.Kind.Secondary, Glyph.Check);
                ess.Enabled = Winget.Available && !checking;
                ess.Click += (s, e) => SelectEssentials();
                int n = UpdateCount;
                var upd = new FlatButton(n > 0 ? L.T("تحديث المتاح (" + n + ")", "Update available (" + n + ")") : L.T("لا توجد تحديثات", "No updates"), FlatButton.Kind.Secondary, Glyph.Download);
                upd.Enabled = n > 0 && !checking;
                upd.Click += (s, e) => { var l = new List<AppDef>(); foreach (var a in Catalog) if (Winget.HasUpdate(a.Id)) l.Add(a); RunSelected(l); };
                var re = new FlatButton(L.T("إعادة الفحص", "Re-check"), FlatButton.Kind.Secondary, Glyph.Refresh);
                re.Enabled = !checking;
                re.Click += (s, e) => CheckState(true);
                return new Control[] { ess, upd, re };
            }
        }

        public void OnShown() { CheckState(false); LoadIcons(); Changed(); }
    }

    class AppsCanvas : Control
    {
        readonly AppsPage page;
        string filter = "";
        readonly List<KeyValuePair<Rectangle, Action>> hits = new List<KeyValuePair<Rectangle, Action>>();
        const int RowH = 52, CardHead = 46, Gap = 16;

        public AppsCanvas(AppsPage p)
        {
            page = p;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = 600;
        }

        int Cols { get { return Width >= 760 ? 2 : 1; } }

        List<string> VisibleCats()
        {
            var l = new List<string>();
            foreach (string c in AppsPage.Cats) if (filter.Length == 0 || filter == c) l.Add(c);
            return l;
        }

        int CardHeight(string cat) { int n = 0; foreach (var a in AppsPage.Catalog) if (a.Cat == cat) n++; return CardHead + n * RowH + 10; }

        public void Relayout()
        {
            int cols = Cols;
            var colY = new int[cols];
            for (int i = 0; i < cols; i++) colY[i] = 118;
            foreach (string c in VisibleCats())
            {
                int k = 0;
                for (int i = 1; i < cols; i++) if (colY[i] < colY[k]) k = i;
                colY[k] += CardHeight(c) + Gap;
            }
            int h = 0;
            foreach (int y in colY) h = Math.Max(h, y);
            h += 4;
            if (Height != h) Height = h;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        Rectangle M(Rectangle r) { return Theme.M(r, Width); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            hits.Clear();
            int W = Width;

            // status banner
            var br = new Rectangle(0, 0, W - 1, 52);
            string bt; Color bbg = Theme.TealTint, bfg = Theme.Text; string glyph = Glyph.Info;
            if (page.Checking) { bt = L.T("جارٍ قراءة البرامج المثبتة والتحديثات المتاحة...", "Reading installed programs and available updates..."); glyph = Glyph.Refresh; }
            else if (Winget.Checked && !Winget.Available) { bt = L.T("أداة winget غير موجودة في ويندوز. ثبّت «مُثبِّت التطبيقات» من متجر مايكروسوفت ثم أعد الفحص.", "winget is not available. Install \"App Installer\" from the Microsoft Store, then re-check."); bbg = Theme.WarnBg; bfg = Theme.Warn; glyph = Glyph.Warning; }
            else bt = L.T("بعد الفرمتة؟ اضغط «تحديد الأساسيات» ويختار لك متصفح و Discord و ‎7-Zip و VLC ومكتبات الألعاب.", "Fresh Windows? Press \"Select essentials\" to pick a browser, Discord, 7-Zip, VLC and game runtimes.");
            Theme.FillRound(g, bbg, br, 12);
            TextRenderer.DrawText(g, glyph, Theme.Icon, M(new Rectangle(14, 0, 26, 52)), bfg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var btr = M(new Rectangle(48, 0, W - 64, 52));
            Theme.Txt(g, bt, Theme.Body, bfg, btr, Theme.Align.Start);
            if (Winget.Checked && !Winget.Available && !page.Checking) hits.Add(new KeyValuePair<Rectangle, Action>(btr, () => U.Open("ms-windows-store://pdp/?productid=9NBLGGH4NNS1")));

            // category chips
            int x = 0, y = 68;
            var chips = new List<string> { "" };
            chips.AddRange(AppsPage.Cats);
            foreach (string c in chips)
            {
                string t = c.Length == 0 ? L.T("الكل", "All") : AppsPage.CatName(c);
                int w = TextRenderer.MeasureText(t, Theme.Body).Width + 28;
                var r = M(new Rectangle(x, y, w, 32));
                bool on = filter == c;
                Theme.FillRound(g, on ? Theme.Primary : Theme.Card, r, 16);
                if (!on) Theme.StrokeRound(g, Theme.Border, r, 16);
                Theme.Txt(g, t, Theme.Body, on ? Color.White : Theme.Text, r, Theme.Align.Center);
                string cc = c;
                hits.Add(new KeyValuePair<Rectangle, Action>(r, () => { filter = cc; Relayout(); Invalidate(); }));
                x += w + 8;
            }

            // cards (masonry)
            int cols = Cols;
            int cw = (W - Gap * (cols - 1)) / cols;
            var colY = new int[cols];
            for (int i = 0; i < cols; i++) colY[i] = 118;
            foreach (string cat in VisibleCats())
            {
                int k = 0;
                for (int i = 1; i < cols; i++) if (colY[i] < colY[k]) k = i;
                int cx = k * (cw + Gap), cy = colY[k], ch = CardHeight(cat);
                colY[k] += ch + Gap;
                var card = M(new Rectangle(cx, cy, cw, ch));
                Theme.FillRound(g, Theme.Card, card, 14);
                Theme.StrokeRound(g, Theme.Border, card, 14);
                Theme.Txt(g, AppsPage.CatName(cat), Theme.H2, Theme.Text, M(new Rectangle(cx + 18, cy + 12, cw / 2, 26)), Theme.Align.Start);
                string hint = AppsPage.CatHint(cat);
                if (hint.Length > 0) Theme.Txt(g, hint, Theme.Small, Theme.Muted, M(new Rectangle(cx + cw / 2, cy + 14, cw / 2 - 18, 24)), Theme.Align.End);
                int ry = cy + CardHead;
                foreach (var a in AppsPage.Catalog)
                {
                    if (a.Cat != cat) continue;
                    DrawRow(g, a, cx + 8, ry, cw - 16);
                    ry += RowH;
                }
            }
        }

        void DrawRow(Graphics g, AppDef a, int x, int y, int w)
        {
            bool inst = Winget.IsInstalled(a.Id), upd = Winget.HasUpdate(a.Id);
            bool can = AppsPage.Selectable(a), sel = AppsPage.Selected.Contains(a.Id);
            var row = M(new Rectangle(x, y, w, RowH - 4));
            if (sel) Theme.FillRound(g, Theme.TealTint, row, 10);

            var cb = M(new Rectangle(x + 12, y + (RowH - 4 - 20) / 2, 20, 20));
            if (sel) { Theme.FillRound(g, Theme.Primary, cb, 5); TextRenderer.DrawText(g, Glyph.Check, Theme.IconSmall, cb, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding); }
            else if (can) Theme.StrokeRound(g, Theme.Faint, cb, 5);
            else Theme.FillRound(g, Theme.Track, cb, 5);

            var ic = M(new Rectangle(x + 44, y + (RowH - 4 - 32) / 2, 32, 32));
            AppsPage.DrawIcon(g, a, ic);

            string pill = upd ? L.T("تحديث متاح", "Update") : inst ? L.T("مثبّت", "Installed") : "";
            int pw = pill.Length == 0 ? 0 : TextRenderer.MeasureText(pill, Theme.Small).Width + 18;
            int tx = x + 88, tw = w - 88 - pw - 16;
            Theme.Txt(g, a.Name, Theme.Bold, can || inst ? Theme.Text : Theme.Faint, M(new Rectangle(tx, y + 6, tw, 22)), Theme.Align.Start);
            Theme.Txt(g, a.Publisher + (a.OpenSource ? L.T("  ·  مفتوح المصدر", "  ·  open source") : ""), Theme.Small, Theme.Muted, M(new Rectangle(tx, y + 26, tw, 18)), Theme.Align.Start);
            if (pw > 0)
            {
                var pr = M(new Rectangle(x + w - pw - 10, y + (RowH - 4 - 22) / 2, pw, 22));
                Theme.FillRound(g, upd ? Theme.WarnBg : Theme.OkBg, pr, 11);
                Theme.Txt(g, pill, Theme.Small, upd ? Theme.Warn : Theme.Ok, pr, Theme.Align.Center);
            }
            AppDef aa = a;
            hits.Add(new KeyValuePair<Rectangle, Action>(row, () => page.Toggle(aa)));
        }

        Action HitAt(Point p) { for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].Key.Contains(p)) return hits[i].Value; return null; }

        protected override void OnMouseMove(MouseEventArgs e) { Cursor = HitAt(e.Location) != null ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { var a = HitAt(e.Location); if (a != null) a(); }
            base.OnMouseClick(e);
        }
    }

    // installs / updates the chosen programs one after another, with a progress bar for each one
    class InstallForm : Form
    {
        readonly List<AppDef> items;
        readonly int[] state;      // 0 waiting, 1 running, 2 done, 3 failed, 4 skipped
        readonly string[] note;
        readonly double[] prog;    // 0..1 download, -1 = not known yet
        readonly FlatButton close;
        readonly IconButton pause, skip, stopAll;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        volatile bool cancelAll, skipNow, paused, finished;
        int spin;
        const int W = 620, RowH = 64, Top0 = 118, VisRows = 6;

        protected override void OnLoad(EventArgs e) { Theme.Style(this); base.OnLoad(e); }

        public InstallForm(List<AppDef> list)
        {
            items = list;
            state = new int[list.Count];
            note = new string[list.Count];
            prog = new double[list.Count];
            for (int i = 0; i < note.Length; i++) { note[i] = ""; prog[i] = -1; }
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = L.T("تثبيت البرامج", "Installing programs");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Card;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(W, Top0 + Math.Min(list.Count, VisRows) * RowH + 78);

            // fixed-size icon buttons (the name shows on hover), so nothing moves when pause turns into resume
            pause = new IconButton("\uE769", L.T("إيقاف مؤقت", "Pause"), false);
            pause.Click += (s, e) => TogglePause();
            skip = new IconButton("\uE893", L.T("إيقاف والانتقال للتالي", "Stop and go to the next one"), false);
            skip.Click += (s, e) => SkipCurrent();
            stopAll = new IconButton("\uE71A", L.T("إيقاف الكل", "Stop all"), true);
            stopAll.Click += (s, e) => StopAll();
            close = new FlatButton(L.T("إغلاق", "Close"), FlatButton.Kind.Primary, "");
            close.Visible = false;
            close.Click += (s, e) => Close();
            Controls.Add(pause); Controls.Add(skip); Controls.Add(stopAll); Controls.Add(close);
            PlaceButtons();

            timer.Tick += (s, e) => { spin++; Invalidate(); if (finished) Done(); };
            FormClosing += (s, e) => { if (!finished) e.Cancel = true; };
        }

        void PlaceButtons()
        {
            int by = ClientSize.Height - 58, x = W - 24;
            foreach (var b in new Control[] { close, stopAll, skip, pause })
            {
                if (!b.Visible) continue;
                int left = x - b.Width;
                b.Location = new Point(L.Ar ? W - left - b.Width : left, by);
                x -= b.Width + 10;
            }
        }

        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (close != null) PlaceButtons(); }

        int Current { get { for (int i = 0; i < state.Length; i++) if (state[i] == 1) return i; return -1; } }

        // stopping a program while its setup is already running can leave it half installed
        bool ConfirmStop()
        {
            int c = Current;
            if (c < 0 || prog[c] < 1) return true;
            return Msg.Show(this, L.T("البرنامج في مرحلة التثبيت الآن، وإيقافه قد يتركه مثبّتاً بشكل ناقص. متابعة؟", "The program is installing right now; stopping may leave it half installed. Continue?"), "", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        void TogglePause()
        {
            paused = !paused;
            Winget.Suspend(paused);
            pause.Set(paused ? Glyph.Play : "\uE769", paused ? L.T("استئناف", "Resume") : L.T("إيقاف مؤقت", "Pause"));
            Invalidate();
        }

        void SkipCurrent()
        {
            if (Current < 0 || !ConfirmStop()) return;
            if (paused) TogglePause();
            skipNow = true;
            Winget.KillCurrent();
        }

        void StopAll()
        {
            if (!ConfirmStop()) return;
            if (paused) TogglePause();
            cancelAll = true;
            skipNow = true;
            Winget.KillCurrent();
            stopAll.Enabled = false; skip.Enabled = false; pause.Enabled = false;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
            var th = new Thread(Work) { IsBackground = true };
            th.Start();
        }

        void Work()
        {
            for (int i = 0; i < items.Count; i++)
            {
                while (paused && !cancelAll) Thread.Sleep(200);
                if (cancelAll) { state[i] = 4; note[i] = L.T("أُوقف", "Stopped"); continue; }
                skipNow = false;
                state[i] = 1;
                prog[i] = -1;
                string msg;
                bool ok;
                int k = i;
                try { ok = Winget.Apply(items[i], out msg, v => { if (v > prog[k]) prog[k] = v; }); } catch (Exception ex) { ok = false; msg = ex.Message; }
                if (skipNow && !ok) { state[i] = 4; note[i] = cancelAll ? L.T("أُوقف", "Stopped") : L.T("تم التخطي", "Skipped"); continue; }
                state[i] = ok ? 2 : 3;
                if (ok) prog[i] = 1;
                note[i] = msg;
            }
            try { Winget.Refresh(AppsPage.Catalog); } catch { }
            finished = true;
        }

        bool doneShown;
        void Done()
        {
            if (doneShown) return;
            doneShown = true;
            timer.Stop();
            pause.Visible = false; skip.Visible = false; stopAll.Visible = false;
            close.Visible = true;
            PlaceButtons();
            Invalidate();
            close.Focus();
        }

        Rectangle MR(Rectangle r) { return Theme.M(r, ClientSize.Width); }

        void Bar(Graphics g, Rectangle r, double v, Color fill, bool moving)
        {
            Theme.FillRound(g, Theme.Track, MR(r), r.Height / 2f);
            if (moving)
            {
                // the size is not known yet: a short block slides along the track
                int bw = r.Width / 4, pos = (spin * 9) % (r.Width + bw) - bw;
                int x0 = Math.Max(r.X, r.X + pos), x1 = Math.Min(r.Right, r.X + pos + bw);
                if (x1 > x0) Theme.FillRound(g, fill, MR(new Rectangle(x0, r.Y, x1 - x0, r.Height)), r.Height / 2f);
                return;
            }
            int w = (int)Math.Round(r.Width * Math.Max(0, Math.Min(1, v)));
            if (w > 0) Theme.FillRound(g, fill, MR(new Rectangle(L.Ar ? r.Right - w : r.X, r.Y, w, r.Height)), r.Height / 2f);
        }

        // whole-item progress: downloading fills up to 90%, the setup itself the last 10%
        double ItemFraction(int i)
        {
            if (state[i] == 2) return 1;
            if (state[i] != 1) return 0;
            return prog[i] < 0 ? 0 : prog[i] * 0.9;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int ok = 0, bad = 0, skipped = 0;
            double total = 0;
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] == 2) ok++; else if (state[i] == 3) bad++; else if (state[i] == 4) skipped++;
                total += state[i] >= 2 ? 1 : ItemFraction(i);
            }
            double overall = items.Count == 0 ? 1 : total / items.Count;
            string head = finished ? (bad == 0 && skipped == 0 ? L.T("اكتمل التثبيت", "All done") : L.T("انتهى", "Finished"))
                : paused ? L.T("متوقف مؤقتاً", "Paused") : L.T("جارٍ التثبيت...", "Installing...");
            Theme.Txt(g, head, Theme.H1, Theme.Text, MR(new Rectangle(24, 16, W - 140, 30)), Theme.Align.Start);
            int pct = (int)Math.Round(overall * 100);
            Theme.Txt(g, pct + "%", Theme.H1, Theme.Primary, MR(new Rectangle(W - 124, 16, 100, 30)), Theme.Align.End);
            string sub = L.T("نجح: " + ok + "   ·   لم ينجح: " + bad + "   ·   تخطي: " + skipped + "   ·   الإجمالي: " + items.Count,
                "Succeeded: " + ok + "   ·   Failed: " + bad + "   ·   Skipped: " + skipped + "   ·   Total: " + items.Count);
            Theme.Txt(g, sub, Theme.Body, Theme.Muted, MR(new Rectangle(24, 50, W - 48, 22)), Theme.Align.Start);
            Bar(g, new Rectangle(24, 80, W - 48, 10), overall, Theme.Primary, false);

            int cur = Current, first = 0;
            if (cur >= 0) first = Math.Max(0, cur - 2);
            int shown = Math.Min(items.Count, VisRows);
            if (first + shown > items.Count) first = Math.Max(0, items.Count - shown);
            for (int k = 0; k < shown; k++)
            {
                int i = first + k;
                var a = items[i];
                int y = Top0 + k * RowH;
                using (var p = new Pen(Theme.Border)) g.DrawLine(p, 24, y + RowH - 1, W - 24, y + RowH - 1);
                AppsPage.DrawIcon(g, a, MR(new Rectangle(24, y + 10, 30, 30)));
                Theme.Txt(g, a.Name, Theme.Bold, Theme.Text, MR(new Rectangle(66, y + 6, 250, 24)), Theme.Align.Start);
                string st; Color c; string gl;
                switch (state[i])
                {
                    case 1:
                        if (paused) st = L.T("متوقف مؤقتاً", "Paused");
                        else if (prog[i] < 0 && Winget.Downloaded <= 0) st = L.T("جارٍ التجهيز", "Preparing") + new string('.', spin / 4 % 4);
                        else if (prog[i] < 1) st = L.T("جارٍ التنزيل", "Downloading") + "  " + U.Size(Winget.Downloaded) + (Winget.TotalBytes > 0 ? " / " + U.Size(Winget.TotalBytes) : "");
                        else st = L.T("جارٍ التثبيت", "Installing") + new string('.', spin / 4 % 4);
                        c = Theme.Primary; gl = paused ? "" : Glyph.Download; break;
                    case 2: st = note[i]; c = Theme.Ok; gl = Glyph.Check; break;
                    case 3: st = note[i]; c = Theme.Danger; gl = Glyph.Error; break;
                    case 4: st = note[i]; c = Theme.Faint; gl = Glyph.Stop; break;
                    default: st = L.T("في الانتظار", "Waiting"); c = Theme.Faint; gl = ""; break;
                }
                Theme.Txt(g, st, Theme.Body, c, MR(new Rectangle(320, y + 6, W - 320 - 56, 24)), Theme.Align.End);
                if (gl.Length > 0) TextRenderer.DrawText(g, gl, Theme.Icon, MR(new Rectangle(W - 50, y + 6, 26, 24)), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                // the item's own bar with its percentage
                var br = new Rectangle(66, y + 38, W - 66 - 76, 8);
                double f = state[i] == 1 ? (prog[i] < 0 ? 0 : prog[i] >= 1 ? 1 : prog[i]) : state[i] == 2 ? 1 : 0;
                bool moving = state[i] == 1 && !paused && (prog[i] < 0 || prog[i] >= 1);
                Color bc = state[i] == 3 ? Theme.Danger : state[i] == 2 ? Theme.Ok : state[i] == 4 ? Theme.Faint : Theme.Primary;
                Bar(g, br, f, bc, moving);
                string ptxt = state[i] == 1 ? (prog[i] < 0 ? "" : prog[i] >= 1 ? L.T("تثبيت", "setup") : (int)Math.Round(prog[i] * 100) + "%") : state[i] == 2 ? "100%" : "";
                if (ptxt.Length > 0) Theme.Txt(g, ptxt, Theme.Small, c, MR(new Rectangle(W - 72, y + 30, 48, 24)), Theme.Align.End);
            }
            if (finished && bad > 0)
                Theme.Txt(g, L.T("التفاصيل في سجل البرنامج (الإعدادات ← مجلد السجلات).", "Details are in the log (Settings > Logs folder)."), Theme.Small, Theme.Muted, MR(new Rectangle(24, ClientSize.Height - 54, 300, 30)), Theme.Align.Start, true, true);
        }
    }

    class IconButton : Control
    {
        string glyph;
        readonly bool danger;
        bool hover;
        readonly ToolTip tip = new ToolTip();

        public IconButton(string g, string tipText, bool isDanger)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            glyph = g; danger = isDanger;
            Size = new Size(46, 40);
            Cursor = Cursors.Hand;
            tip.SetToolTip(this, tipText);
            AccessibleName = tipText;
        }

        public void Set(string g, string tipText) { glyph = g; tip.SetToolTip(this, tipText); AccessibleName = tipText; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color bg = danger ? (hover ? Theme.DangerHover : Theme.Danger) : (hover ? Theme.Bg : Theme.Card);
            Color fg = danger ? Color.White : Theme.Text;
            if (!Enabled) { bg = Theme.Track; fg = Theme.Faint; }
            Theme.FillRound(g, bg, r, 10);
            if (!danger && Enabled) Theme.StrokeRound(g, Theme.Border, r, 10);
            TextRenderer.DrawText(g, glyph, Theme.Icon, new Rectangle(0, 0, Width, Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
