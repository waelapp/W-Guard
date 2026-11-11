// W-Guard - Scanners (read-only: nothing here changes the computer)
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WGuard
{
    class ScanCtx
    {
        public volatile bool Cancel;
        public volatile string Current = "";
        public long Files;
        public double StepProgress;   // 0..1 inside the running step
    }

    class ScanStep
    {
        public string Title;
        public double Weight;
        public Action<ScanCtx> Run;
        public ScanStep(string title, double weight, Action<ScanCtx> run) { Title = title; Weight = weight; Run = run; }
    }

    // ------------------------------------------------------------------ results store
    static class Store
    {
        public static readonly object Lock = new object();
        public static readonly Dictionary<string, List<Row>> Results = new Dictionary<string, List<Row>>();
        public static SysInfo Info;
        public static DateTime LastFullScan = DateTime.MinValue;

        public static void Reset(string section) { lock (Lock) Results[section] = new List<Row>(); }
        public static void Add(string section, IEnumerable<Row> rows)
        {
            lock (Lock)
            {
                List<Row> l;
                if (!Results.TryGetValue(section, out l)) { l = new List<Row>(); Results[section] = l; }
                foreach (Row r in rows) { r.Section = section; l.Add(r); }
            }
        }
        public static List<Row> Get(string section)
        {
            lock (Lock)
            {
                List<Row> l;
                return Results.TryGetValue(section, out l) ? new List<Row>(l) : null;
            }
        }
        public static void Remove(string section, Row r) { lock (Lock) { List<Row> l; if (Results.TryGetValue(section, out l)) l.Remove(r); } }
        public static long Total(string section) { var l = Get(section); return l == null ? 0 : l.Sum(x => x.Bytes); }
        // ---- last full scan, kept between runs so the app does not ask for a new scan every time
        // Background rows are not kept: they describe running processes, which change after a restart.
        static string CacheFile { get { return Path.Combine(AppInfo.DataDir, "last-scan.json"); } }

        public class Cache { public int V = 1; public string Time = ""; public Dictionary<string, List<Row>> Sections = new Dictionary<string, List<Row>>(); }

        public static int DaysSinceFull { get { return LastFullScan == DateTime.MinValue ? -1 : (int)(DateTime.Now - LastFullScan).TotalDays; } }
        public static bool ScanIsOld { get { return LastFullScan != DateTime.MinValue && DaysSinceFull >= Settings.RescanDays; } }

        public static void SaveCache()
        {
            if (Demo.On || LastFullScan == DateTime.MinValue) return;
            try
            {
                var c = new Cache { Time = LastFullScan.ToString("o", System.Globalization.CultureInfo.InvariantCulture) };
                lock (Lock) foreach (var kv in Results) if (kv.Key != Scanners.Background) c.Sections[kv.Key] = new List<Row>(kv.Value);
                foreach (var l in c.Sections.Values) foreach (var r in l) r.Check = false;
                var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string tmp = CacheFile + ".tmp";
                File.WriteAllText(tmp, js.Serialize(c), new System.Text.UTF8Encoding(false));
                if (File.Exists(CacheFile)) File.Delete(CacheFile);
                File.Move(tmp, CacheFile);
            }
            catch (Exception ex) { Log.Write("cache save: " + ex.Message); }
        }

        public static bool LoadCache()
        {
            if (Demo.On) return false;
            try
            {
                if (!File.Exists(CacheFile)) return false;
                var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var c = js.Deserialize<Cache>(File.ReadAllText(CacheFile, System.Text.Encoding.UTF8));
                DateTime t;
                if (c == null || c.Sections == null || !DateTime.TryParse(c.Time, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out t)) return false;
                lock (Lock)
                {
                    foreach (var kv in c.Sections)
                    {
                        if (kv.Key == Scanners.Background || kv.Value == null) continue;
                        foreach (var r in kv.Value) r.Section = kv.Key;
                        Results[kv.Key] = kv.Value;
                    }
                }
                LastFullScan = t;
                return true;
            }
            catch (Exception ex) { Log.Write("cache load: " + ex.Message); return false; }
        }

        public static int Count(string section, int minLevel)
        {
            var l = Get(section);
            return l == null ? 0 : l.Count(x => x.Level >= minLevel && x.Level < 3);
        }
    }

    // ------------------------------------------------------------------ system information
    class VolumeInfo { public string Letter; public string Label; public long Size; public long Free; public string Media = ""; }

    class SysInfo
    {
        public string OS = "", Build = "", Cpu = "", Board = "", Bios = "", PowerPlan = "";
        public List<string> Gpus = new List<string>();
        public long RamBytes; public int RamSticks; public int RamSpeed; public int RamRated; public string RamPart = "";
        public List<string> Disks = new List<string>();
        public List<VolumeInfo> Volumes = new List<VolumeInfo>();
        public bool DefenderKnown, RealTime, AvEnabled;
        public DateTime SigUpdated = DateTime.MinValue, LastQuick = DateTime.MinValue, LastFull = DateTime.MinValue;
        public bool HasHdd;
        public TimeSpan Uptime;

        public static SysInfo Collect(ScanCtx ctx)
        {
            var s = new SysInfo();
            try
            {
                foreach (var o in U.Wmi(@"root\cimv2", "SELECT Caption,BuildNumber,LastBootUpTime FROM Win32_OperatingSystem"))
                {
                    s.OS = U.S(o, "Caption").Replace("Microsoft ", "");
                    s.Build = U.S(o, "BuildNumber");
                    DateTime boot = U.WmiDate(U.S(o, "LastBootUpTime"));
                    if (boot > DateTime.MinValue) s.Uptime = DateTime.Now - boot;
                }
                if (ctx != null) ctx.StepProgress = 0.15;
                foreach (var o in U.Wmi(@"root\cimv2", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    s.Cpu = Regex.Replace(U.S(o, "Name"), @"\s+", " ") + "  ·  " + U.N(o, "NumberOfCores") + L.T(" نواة / ", " cores / ") + U.N(o, "NumberOfLogicalProcessors") + L.T(" مسار", " threads");
                    break;
                }
                foreach (var o in U.Wmi(@"root\cimv2", "SELECT Name,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController"))
                {
                    string n = U.S(o, "Name");
                    if (Regex.IsMatch(n, "(?i)basic|remote|virtual|parsec|meta")) continue;
                    string res = U.N(o, "CurrentHorizontalResolution") > 0 ? "  ·  " + U.N(o, "CurrentHorizontalResolution") + "x" + U.N(o, "CurrentVerticalResolution") + " @" + U.N(o, "CurrentRefreshRate") + "Hz" : "";
                    s.Gpus.Add(n + res);
                }
                if (ctx != null) ctx.StepProgress = 0.3;
                foreach (var o in U.Wmi(@"root\cimv2", "SELECT Capacity,ConfiguredClockSpeed,Speed,PartNumber,Manufacturer FROM Win32_PhysicalMemory"))
                {
                    s.RamBytes += U.N(o, "Capacity"); s.RamSticks++;
                    if (s.RamSpeed == 0) { s.RamSpeed = (int)U.N(o, "ConfiguredClockSpeed"); s.RamPart = (U.S(o, "Manufacturer") + " " + U.S(o, "PartNumber")).Trim(); }
                    Match m = Regex.Match(U.S(o, "PartNumber"), @"(\d{4})");
                    int rated;
                    if (m.Success && int.TryParse(m.Groups[1].Value, out rated) && rated >= 2133 && rated <= 9600) s.RamRated = Math.Max(s.RamRated, rated);
                }
                foreach (var o in U.Wmi(@"root\cimv2", "SELECT Manufacturer,Product FROM Win32_BaseBoard")) s.Board = (U.S(o, "Manufacturer") + " " + U.S(o, "Product")).Trim();
                foreach (var o in U.Wmi(@"root\cimv2", "SELECT SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"))
                {
                    DateTime d = U.WmiDate(U.S(o, "ReleaseDate"));
                    s.Bios = U.S(o, "SMBIOSBIOSVersion") + (d > DateTime.MinValue ? " (" + U.D(d) + ")" : "");
                }
                if (ctx != null) ctx.StepProgress = 0.5;

                // physical disks + partitions -> media type per drive letter
                var media = new Dictionary<string, string>();
                var diskMedia = new Dictionary<string, string>();
                foreach (var o in U.Wmi(@"root\Microsoft\Windows\Storage", "SELECT DeviceId,FriendlyName,MediaType,BusType,Size,HealthStatus FROM MSFT_PhysicalDisk"))
                {
                    long mt = U.N(o, "MediaType"), bus = U.N(o, "BusType");
                    string kind = bus == 17 ? "NVMe" : mt == 4 ? "SSD" : mt == 3 ? "HDD" : bus == 7 ? "USB" : "";
                    if (kind == "HDD") s.HasHdd = true;
                    string health = U.N(o, "HealthStatus") == 0 ? "" : "  ·  " + L.T("تحذير صحة القرص!", "Disk health warning!");
                    s.Disks.Add(U.S(o, "FriendlyName") + "  ·  " + kind + "  ·  " + U.GB(U.N(o, "Size")).Replace(".0", "") + " GB" + health);
                    diskMedia[U.S(o, "DeviceId")] = kind;
                }
                foreach (var o in U.Wmi(@"root\Microsoft\Windows\Storage", "SELECT DiskNumber,DriveLetter FROM MSFT_Partition"))
                {
                    string letter = U.S(o, "DriveLetter");
                    string kind;
                    if (letter.Length > 0 && letter != "\0" && diskMedia.TryGetValue(U.S(o, "DiskNumber"), out kind)) media[letter.Substring(0, 1).ToUpperInvariant()] = kind;
                }
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        var v = new VolumeInfo { Letter = d.Name.Substring(0, 1), Label = d.VolumeLabel, Size = d.TotalSize, Free = d.TotalFreeSpace };
                        string mk; if (media.TryGetValue(v.Letter, out mk)) v.Media = mk;
                        s.Volumes.Add(v);
                    }
                    catch { }
                }
                if (ctx != null) ctx.StepProgress = 0.75;

                foreach (var o in U.Wmi(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus"))
                {
                    s.DefenderKnown = true;
                    try { s.RealTime = (bool)o["RealTimeProtectionEnabled"]; } catch { }
                    try { s.AvEnabled = (bool)o["AntivirusEnabled"]; } catch { }
                    s.SigUpdated = U.WmiDate(U.S(o, "AntivirusSignatureLastUpdated"));
                    s.LastQuick = U.WmiDate(U.S(o, "QuickScanEndTime"));
                    s.LastFull = U.WmiDate(U.S(o, "FullScanEndTime"));
                }
                foreach (var o in U.Wmi(@"root\cimv2\power", "SELECT ElementName,IsActive FROM Win32_PowerPlan"))
                {
                    try { if ((bool)o["IsActive"]) s.PowerPlan = U.S(o, "ElementName"); } catch { }
                }
            }
            catch (Exception ex) { Log.Write("SysInfo: " + ex.Message); }
            if (ctx != null) ctx.StepProgress = 1;
            return s;
        }
    }

    // ------------------------------------------------------------------ one pass over a drive: big folders, big files, old files
    class DriveScan
    {
        public Dictionary<string, long[]> Agg = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase); // size, oldSize, files, lastTicks
        public List<Row> Big = new List<Row>();
        public List<Row> Old = new List<Row>();
        public long Bytes;

        public void Run(string root, long usedBytes, ScanCtx ctx)
        {
            DateTime cutoff = DateTime.Now.AddDays(-Settings.OldDays);
            long bigMin = (long)Settings.BigMB << 20;
            long oldMin = (long)Settings.OldMinMB << 20;
            string sysDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").Substring(0, 1).ToUpperInvariant();
            bool isSys = root.Substring(0, 1).ToUpperInvariant() == sysDrive;
            string usersRoot = (sysDrive + ":\\Users\\").ToLowerInvariant();
            string r = root.TrimEnd('\\');
            var st = new Stack<string>();
            st.Push(root);
            int tick = 0;
            while (st.Count > 0)
            {
                if (ctx.Cancel) return;
                string d = st.Pop();
                if (++tick % 64 == 0) ctx.Current = d;
                try
                {
                    foreach (var f in new DirectoryInfo(d).EnumerateFileSystemInfos())
                    {
                        try
                        {
                            if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if ((f.Attributes & FileAttributes.Directory) != 0) { st.Push(f.FullName); continue; }
                            var fi = (FileInfo)f;
                            long len = fi.Length;
                            DateTime m = fi.LastWriteTime;
                            bool old = m < cutoff;
                            Bytes += len;
                            ctx.Files++;
                            string dir = fi.DirectoryName;
                            string rel = dir.Length > r.Length ? dir.Substring(r.Length).Trim('\\') : "";
                            if (rel.Length > 0)
                            {
                                string[] parts = rel.Split('\\');
                                string p = r;
                                for (int i = 0; i < parts.Length && i < 3; i++)
                                {
                                    p = p + "\\" + parts[i];
                                    long[] a;
                                    if (!Agg.TryGetValue(p, out a)) { a = new long[4]; Agg[p] = a; }
                                    a[0] += len; if (old) a[1] += len; a[2]++; if (m.Ticks > a[3]) a[3] = m.Ticks;
                                }
                            }
                            string full = fi.FullName;
                            if (len >= bigMin)
                            {
                                bool can = Safety.IsSafe(full) && !Safety.IsSystemOwned(full);
                                var row = new Row(fi.Name, L.T("ملف كبير", "Large file") + (can ? "" : " · " + L.T("تابع لبرنامج أو للنظام", "Belongs to an app or the system")), len, U.D(m), full, can ? "DeleteFile" : "", "");
                                Big.Add(row);
                            }
                            if (old && len >= oldMin && !Safety.IsSystemOwned(full) && (!isSys || full.ToLowerInvariant().StartsWith(usersRoot)) && Safety.IsSafe(full))
                            {
                                Old.Add(new Row(fi.Name, L.T("آخر تعديل قبل ", "Last modified ") + (int)((DateTime.Now - m).TotalDays / 30) + L.T(" شهراً", " months ago"), len, U.D(m), full, "DeleteFile", ""));
                            }
                            if (usedBytes > 0 && (ctx.Files & 1023) == 0) ctx.StepProgress = Math.Min(0.99, (double)Bytes / usedBytes);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            ctx.StepProgress = 1;
        }

        public List<Row> FolderRows()
        {
            var rows = new List<Row>();
            foreach (var kv in Agg)
            {
                long[] a = kv.Value;
                int depth = kv.Key.Split('\\').Length - 1;
                if (a[0] < (2L << 30) || depth < 1) continue;
                bool can = depth >= 2 && Safety.IsSafe(kv.Key) && !Safety.IsSystemOwned(kv.Key);
                string detail = L.T("مجلد", "Folder") + " · " + U.Num(a[2]) + L.T(" ملف", " files");
                if (a[1] > 0) detail += " · " + L.T("القديم منه ", "old part ") + U.Size(a[1]);
                if (!can) detail += " · " + L.T("للعرض فقط", "view only");
                rows.Add(new Row(System.IO.Path.GetFileName(kv.Key), detail, a[0], a[3] > 0 ? U.D(new DateTime(a[3])) : "", kv.Key, can ? "DeleteDir" : "", ""));
            }
            return rows;
        }
    }

    // ------------------------------------------------------------------ sections
    static class Scanners
    {
        public const string Overview = "overview", Space = "space", Old = "old", Games = "games", Programs = "programs",
            Security = "security", Background = "background", Cleanup = "cleanup", Desktop = "desktop";

        // ---------- steps used by the scan window
        public static List<ScanStep> StepsFor(string section)
        {
            var steps = new List<ScanStep>();
            bool full = section == Overview;
            if (full)
            {
                steps.Add(new ScanStep(L.T("مواصفات الجهاز والحماية", "System specs & protection"), 3, ctx => { Store.Info = SysInfo.Collect(ctx); }));
            }
            if (full || section == Space || section == Old)
            {
                bool first = true;
                long totalUsed = 0;
                var drives = new List<DriveInfo>();
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) { drives.Add(d); totalUsed += d.TotalSize - d.TotalFreeSpace; } } catch { }
                }
                foreach (DriveInfo d in drives)
                {
                    long used = 0;
                    try { used = d.TotalSize - d.TotalFreeSpace; } catch { }
                    string root = d.Name;
                    bool reset = first; first = false;
                    double w = totalUsed > 0 ? 70.0 * used / totalUsed : 10;
                    steps.Add(new ScanStep(L.T("القرص ", "Drive ") + root.TrimEnd('\\'), Math.Max(2, w), ctx =>
                    {
                        if (reset) { Store.Reset(Space); Store.Reset(Old); }
                        var ds = new DriveScan();
                        ds.Run(root, used, ctx);
                        Store.Add(Space, ds.FolderRows());
                        Store.Add(Space, ds.Big);
                        Store.Add(Old, ds.Old);
                    }));
                }
            }
            if (full || section == Games) steps.Add(new ScanStep(L.T("الألعاب", "Games"), 5, ctx => { Store.Reset(Games); Store.Add(Games, ScanGames(ctx)); }));
            if (full || section == Programs) steps.Add(new ScanStep(L.T("البرامج المثبتة", "Installed programs"), 5, ctx => { Store.Reset(Programs); Store.Add(Programs, ScanPrograms(ctx)); }));
            if (full || section == Security) steps.Add(new ScanStep(L.T("الأمان والتشغيل التلقائي", "Security & autostart"), 8, ctx => { Store.Reset(Security); Store.Add(Security, ScanSecurity(ctx)); }));
            if (full || section == Background) steps.Add(new ScanStep(L.T("الخلفية وبدء التشغيل", "Background & startup"), 3, ctx => { Store.Reset(Background); Store.Add(Background, ScanBackground(ctx)); }));
            if (full || section == Cleanup) steps.Add(new ScanStep(L.T("الملفات المؤقتة", "Temporary files"), 4, ctx => { Store.Reset(Cleanup); Store.Add(Cleanup, ScanCleanup(ctx)); }));
            if (full || section == Desktop) steps.Add(new ScanStep(L.T("سطح المكتب", "Desktop"), 1, ctx => { Store.Reset(Desktop); Store.Add(Desktop, ScanDesktop(ctx)); }));
            if (full) steps.Add(new ScanStep(L.T("إعداد التقرير", "Building the report"), 1, ctx => { Store.LastFullScan = DateTime.Now; ctx.StepProgress = 1; }));
            return steps;
        }

        // ---------- games
        static string Vdf(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        public static List<Row> ScanGames(ScanCtx ctx)
        {
            var rows = new List<Row>();
            DateTime cutoff = DateTime.Now.AddDays(-Settings.OldDays);
            // Steam
            string steam = null;
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) if (k != null) steam = (k.GetValue("SteamPath") as string); } catch { }
            if (string.IsNullOrEmpty(steam)) steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            steam = steam.Replace('/', '\\');
            var libs = new List<string>();
            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
            if (libs.Count == 0) libs.Add(steam);
            foreach (string lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (ctx.Cancel) break;
                string sa = Path.Combine(lib, "steamapps");
                ctx.Current = sa;
                if (!Directory.Exists(sa)) continue;
                foreach (string f in Directory.GetFiles(sa, "appmanifest_*.acf"))
                {
                    try
                    {
                        string t = File.ReadAllText(f);
                        string name = Vdf(t, "name"), id = Vdf(t, "appid");
                        if (Regex.IsMatch(name, "(?i)redistributable|proton|steam linux runtime|steamworks common")) continue;
                        long size; long.TryParse(Vdf(t, "SizeOnDisk"), out size);
                        long lp; long.TryParse(Vdf(t, "LastPlayed"), out lp);
                        var row = new Row(name, "Steam · " + lib, size, L.T("لم تُلعب", "Never played"), "steam://uninstall/" + id, "SteamUninstall", id);
                        string inst = Path.Combine(sa, "common", Vdf(t, "installdir"));
                        row.Key = inst;
                        if (lp > 0)
                        {
                            DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(lp).ToLocalTime();
                            row.Date = U.D(dt);
                            if (dt < cutoff) { row.Risk = L.T("لم تُلعب منذ مدة طويلة", "Not played for a long time"); row.Level = 1; }
                        }
                        else { row.Risk = L.T("لم تُشغّل أبداً", "Never launched"); row.Level = 1; }
                        rows.Add(row);
                    }
                    catch { }
                }
            }
            ctx.StepProgress = 0.4;
            // Epic
            string epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (Directory.Exists(epic))
            {
                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                foreach (string f in Directory.GetFiles(epic, "*.item"))
                {
                    try
                    {
                        var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                        string name = d.ContainsKey("DisplayName") ? Convert.ToString(d["DisplayName"]) : Path.GetFileNameWithoutExtension(f);
                        string loc = d.ContainsKey("InstallLocation") ? Convert.ToString(d["InstallLocation"]) : "";
                        long size = 0;
                        if (d.ContainsKey("InstallSize")) long.TryParse(Convert.ToString(d["InstallSize"]), out size);
                        if (size <= 0) size = U.FolderSize(loc, ctx);
                        string date = Directory.Exists(loc) ? U.D(Directory.GetLastWriteTime(loc)) : "";
                        rows.Add(new Row(name, "Epic Games · " + loc, size, date, loc, "OpenEpic", ""));
                    }
                    catch { }
                }
            }
            ctx.StepProgress = 0.6;
            // Xbox / Microsoft Store games
            foreach (DriveInfo dr in DriveInfo.GetDrives())
            {
                try
                {
                    if (dr.DriveType != DriveType.Fixed || !dr.IsReady) continue;
                    string xg = Path.Combine(dr.Name, "XboxGames");
                    if (!Directory.Exists(xg)) continue;
                    foreach (string d in Directory.GetDirectories(xg))
                    {
                        if (ctx.Cancel) break;
                        if (Path.GetFileName(d).Equals("GameSave", StringComparison.OrdinalIgnoreCase)) continue;
                        ctx.Current = d;
                        rows.Add(new Row(Path.GetFileName(d), "Xbox · " + xg, U.FolderSize(d, ctx), U.D(Directory.GetLastWriteTime(d)), d, "OpenXbox", ""));
                    }
                }
                catch { }
            }
            ctx.StepProgress = 1;
            return rows.OrderByDescending(x => x.Bytes).ToList();
        }

        // ---------- programs
        public class UninstallEntry
        {
            public string Name, Publisher, Version, InstallDate, Location, Uninstall, Quiet, RegPath;
            public RegistryHive Hive; public RegistryView View; public long SizeKB;
        }

        public static List<UninstallEntry> GetUninstalls()
        {
            var list = new List<UninstallEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new[] {
                new { H = RegistryHive.LocalMachine, V = RegistryView.Registry64 },
                new { H = RegistryHive.LocalMachine, V = RegistryView.Registry32 },
                new { H = RegistryHive.CurrentUser, V = RegistryView.Registry64 } };
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
            foreach (var src in sources)
            {
                try
                {
                    using (var bk = RegistryKey.OpenBaseKey(src.H, src.V))
                    using (var root = bk.OpenSubKey(path))
                    {
                        if (root == null) continue;
                        foreach (string sub in root.GetSubKeyNames())
                        {
                            try
                            {
                                using (var k = root.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    string name = Convert.ToString(k.GetValue("DisplayName"));
                                    if (string.IsNullOrEmpty(name)) continue;
                                    if (Convert.ToString(k.GetValue("SystemComponent")) == "1") continue;
                                    if (!string.IsNullOrEmpty(Convert.ToString(k.GetValue("ParentKeyName")))) continue;
                                    if (!seen.Add(name)) continue;
                                    long kb = 0; long.TryParse(Convert.ToString(k.GetValue("EstimatedSize")), out kb);
                                    list.Add(new UninstallEntry
                                    {
                                        Name = name.Trim(), Publisher = Convert.ToString(k.GetValue("Publisher")), Version = Convert.ToString(k.GetValue("DisplayVersion")),
                                        InstallDate = Convert.ToString(k.GetValue("InstallDate")), Location = Convert.ToString(k.GetValue("InstallLocation")),
                                        Uninstall = Convert.ToString(k.GetValue("UninstallString")), Quiet = Convert.ToString(k.GetValue("QuietUninstallString")),
                                        RegPath = path + "\\" + sub, Hive = src.H, View = src.V, SizeKB = kb
                                    });
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

        const string PupNames = @"(?i)Web Companion|Lavasoft|Driver Booster|DriverPack|360 Total|Avast Secure Browser|OneLaunch|Wave Browser|WebNavigator|Shift Browser|PC App Store|Segurazo|ByteFence|Reimage|Restoro|Outbyte|Advanced SystemCare|McAfee WebAdvisor|Hola VPN|BitTorrent Web|Search Protect|Ask Toolbar|Conduit|Mindspark|PDFast|WinZip Driver Updater|Slimware|Driver Easy|Driver Support|PC Optimizer|SpeedUpMyPC|MyPC Backup|Iobit Uninstaller Toolbar|Chromium \(unknown\)";

        public static List<Row> ScanPrograms(ScanCtx ctx)
        {
            var rows = new List<Row>();
            var pf = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            DateTime oldestPf = DateTime.MaxValue;
            try
            {
                foreach (string f in Directory.GetFiles(Path.Combine(Environment.GetEnvironmentVariable("windir"), "Prefetch"), "*.pf"))
                {
                    string n = Regex.Replace(Path.GetFileName(f), @"-[0-9A-F]{8}\.pf$", "", RegexOptions.IgnoreCase);
                    DateTime t = File.GetLastWriteTime(f);
                    DateTime cur;
                    if (!pf.TryGetValue(n, out cur) || cur < t) pf[n] = t;
                    if (t < oldestPf) oldestPf = t;
                }
            }
            catch { }
            ctx.StepProgress = 0.2;
            var all = GetUninstalls();
            int i = 0;
            DateTime cutoff = DateTime.Now.AddDays(-Settings.OldDays);
            foreach (var p in all)
            {
                if (ctx.Cancel) break;
                ctx.StepProgress = 0.2 + 0.8 * (++i) / Math.Max(1, all.Count);
                if (Regex.IsMatch(p.Name, @"(?i)Visual C\+\+|\.NET|DirectX|Runtime|Redistributable|Update Health|WebView2|Windows SDK|Microsoft Edge$|Windows Driver Package")) continue;
                ctx.Current = p.Name;
                DateTime last = DateTime.MinValue;
                try
                {
                    if (!string.IsNullOrEmpty(p.Location) && Directory.Exists(p.Location))
                    {
                        var exes = Directory.GetFiles(p.Location, "*.exe", SearchOption.TopDirectoryOnly).ToList();
                        foreach (string sd in Directory.GetDirectories(p.Location).Take(15))
                            try { exes.AddRange(Directory.GetFiles(sd, "*.exe", SearchOption.TopDirectoryOnly)); } catch { }
                        foreach (string e in exes.Take(60))
                        {
                            string fn = Path.GetFileName(e).ToUpperInvariant();
                            if (Regex.IsMatch(fn, "^(UNINS|UNINSTALL|UPDATE|SETUP|CRASH)")) continue;
                            DateTime t;
                            if (pf.TryGetValue(fn, out t) && t > last) last = t;
                        }
                    }
                }
                catch { }
                string lastTxt = last > DateTime.MinValue ? U.D(last) : (oldestPf < DateTime.MaxValue ? L.T("قبل ", "before ") + U.D(oldestPf) : "");
                var row = new Row(p.Name, ((p.Publisher ?? "") + " " + (p.Version ?? "")).Trim(), p.SizeKB * 1024, lastTxt, p.Location, "Uninstall", p.Hive + "|" + p.View + "|" + p.RegPath);
                if (Regex.IsMatch(p.Name, PupNames)) { row.Risk = L.T("برنامج دعائي/غير مرغوب معروف", "Known adware / unwanted program"); row.Level = 2; row.Check = true; }
                else
                {
                    string ex = Risk.ExePath(p.Uninstall);
                    if (ex != null && Regex.IsMatch(ex, @"^[A-Za-z]:\\") && !ex.ToLowerInvariant().Contains("msiexec") && !File.Exists(Environment.ExpandEnvironmentVariables(ex)))
                    { row.Risk = L.T("بقايا: أداة الإزالة مفقودة", "Leftover: uninstaller missing"); row.Level = 1; row.Action = "RemoveEntry"; }
                    else if (last == DateTime.MinValue && oldestPf < cutoff && pf.Count > 0)
                    { row.Risk = L.T("لم يُستخدم منذ مدة طويلة", "Not used for a long time"); row.Level = 1; }
                }
                rows.Add(row);
            }
            return rows.OrderByDescending(x => x.Level).ThenBy(x => x.Name).ToList();
        }

        // ---------- security
        static readonly string[][] RunKeys = {
            new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run" },
            new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" },
            new[] { "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run" },
            new[] { "HKLM", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" },
            new[] { "HKLM32", @"Software\Microsoft\Windows\CurrentVersion\Run" } };

        public static RegistryKey OpenHive(string h)
        {
            if (h == "HKCU") return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            if (h == "HKLM32") return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        static string ShortcutTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic sc = sh.CreateShortcut(lnk);
                return "\"" + (string)sc.TargetPath + "\" " + (string)sc.Arguments;
            }
            catch { return lnk; }
        }

        public static List<Row> ScanSecurity(ScanCtx ctx)
        {
            var rows = new List<Row>();
            int lv;
            foreach (var rk in RunKeys)
            {
                try
                {
                    using (var hive = OpenHive(rk[0]))
                    using (var k = hive.OpenSubKey(rk[1]))
                    {
                        if (k == null) continue;
                        foreach (string n in k.GetValueNames())
                        {
                            string v = Convert.ToString(k.GetValue(n));
                            var row = new Row(n.Length == 0 ? "(default)" : n, L.T("تشغيل تلقائي: ", "Autorun: ") + v, 0, "", rk[0] + "\\" + rk[1], "RemoveRun", n);
                            row.Risk = Risk.ForCommand(v, out lv); row.Level = lv;
                            rows.Add(row);
                        }
                    }
                }
                catch { }
            }
            ctx.StepProgress = 0.15;
            foreach (string sf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            {
                try
                {
                    if (!Directory.Exists(sf)) continue;
                    foreach (string f in Directory.GetFiles(sf))
                    {
                        if (Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        string tgt = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(f) : f;
                        var row = new Row(Path.GetFileName(f), L.T("مجلد بدء التشغيل: ", "Startup folder: ") + tgt, 0, U.D(File.GetLastWriteTime(f)), f, "DeleteFile", "");
                        row.Risk = Risk.ForCommand(tgt, out lv); row.Level = lv;
                        rows.Add(row);
                    }
                }
                catch { }
            }
            ctx.StepProgress = 0.25;
            // scheduled tasks (non-Microsoft, enabled)
            try
            {
                Type t = Type.GetTypeFromProgID("Schedule.Service");
                dynamic svc = Activator.CreateInstance(t);
                svc.Connect();
                var folders = new Stack<dynamic>();
                folders.Push(svc.GetFolder("\\"));
                while (folders.Count > 0)
                {
                    if (ctx.Cancel) break;
                    dynamic folder = folders.Pop();
                    string fpath = (string)folder.Path;
                    if (fpath.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                    try { foreach (dynamic sub in folder.GetFolders(0)) folders.Push(sub); } catch { }
                    try
                    {
                        foreach (dynamic task in folder.GetTasks(1))
                        {
                            try
                            {
                                if (!(bool)task.Enabled) continue;
                                var cmds = new List<string>();
                                foreach (dynamic a in task.Definition.Actions)
                                {
                                    try { if ((int)a.Type == 0) cmds.Add(((string)a.Path + " " + (string)a.Arguments).Trim()); } catch { }
                                }
                                string cmd = string.Join(" | ", cmds.ToArray());
                                var row = new Row((string)task.Name, L.T("مهمة مجدولة: ", "Scheduled task: ") + cmd, 0, "", (string)task.Path, "DisableTask", (string)task.Path);
                                row.Risk = Risk.ForCommand(cmd, out lv); row.Level = lv;
                                rows.Add(row);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Log.Write("tasks: " + ex.Message); }
            ctx.StepProgress = 0.55;
            // services not from Windows
            foreach (var o in U.Wmi(@"root\cimv2", "SELECT Name,DisplayName,PathName,State,StartMode FROM Win32_Service"))
            {
                string p = U.S(o, "PathName");
                if (p.Length == 0 || Regex.IsMatch(p, @"(?i)\\windows\\(system32|syswow64|servicing|microsoft\.net)\\|windows defender|\\microsoft\\|\\windowsapps\\")) continue;
                string mode = U.S(o, "StartMode");
                if (mode == "Disabled") continue;
                var row = new Row(U.S(o, "DisplayName"), L.T("خدمة", "Service") + " (" + U.S(o, "State") + " / " + mode + "): " + p, 0, "", p, "DisableService", U.S(o, "Name"));
                row.Risk = Risk.ForCommand(p, out lv); row.Level = lv;
                rows.Add(row);
            }
            ctx.StepProgress = 0.8;
            // Microsoft Defender history
            var names = new Dictionary<string, string>();
            foreach (var o in U.Wmi(@"root\Microsoft\Windows\Defender", "SELECT ThreatID,ThreatName FROM MSFT_MpThreat")) names[U.S(o, "ThreatID")] = U.S(o, "ThreatName");
            var det = U.Wmi(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpThreatDetection");
            foreach (var o in det.OrderByDescending(x => U.WmiDate(U.S(x, "InitialDetectionTime"))).Take(40))
            {
                string id = U.S(o, "ThreatID"), name;
                if (!names.TryGetValue(id, out name)) name = id;
                string res = "";
                try { var arr = o["Resources"] as string[]; if (arr != null) res = string.Join(" , ", arr); } catch { }
                bool ok = false;
                try { ok = (bool)o["ActionSuccess"]; } catch { }
                var row = new Row("Defender: " + name, res, 0, U.WmiDate(U.S(o, "InitialDetectionTime")).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), "", "", "");
                row.Risk = ok ? L.T("عولج بنجاح", "Handled") : L.T("لم يُعالج - افتح أمن Windows", "Not handled - open Windows Security");
                row.Level = ok ? 3 : 2;
                rows.Add(row);
            }
            ctx.StepProgress = 1;
            return rows.OrderByDescending(x => x.Level == 3 ? 0 : x.Level).ThenBy(x => x.Name).ToList();
        }

        // ---------- background & startup
        public static List<Row> ScanBackground(ScanCtx ctx) { return Advice.ScanBackground(ctx); }

        // ---------- cleanup
        public static List<Row> ScanCleanup(ScanCtx ctx)
        {
            var rows = new List<Row>();
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ra = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string win = Environment.GetEnvironmentVariable("windir");
            string[][] items = {
                new[] { L.T("ملفات المستخدم المؤقتة", "User temp files"), Path.GetTempPath() },
                new[] { L.T("ملفات ويندوز المؤقتة", "Windows temp files"), Path.Combine(win, "Temp") },
                new[] { L.T("تقارير الأعطال", "Crash dumps"), Path.Combine(la, "CrashDumps") },
                new[] { L.T("تقارير أخطاء ويندوز", "Windows error reports"), Path.Combine(pd, @"Microsoft\Windows\WER") },
                new[] { L.T("كاش الشيدر NVIDIA DX", "NVIDIA DX shader cache"), Path.Combine(la, @"NVIDIA\DXCache") },
                new[] { L.T("كاش الشيدر NVIDIA GL", "NVIDIA GL shader cache"), Path.Combine(la, @"NVIDIA\GLCache") },
                new[] { L.T("كاش الشيدر AMD", "AMD shader cache"), Path.Combine(la, @"AMD\DxCache") },
                new[] { L.T("كاش الشيدر DirectX", "DirectX shader cache"), Path.Combine(la, "D3DSCache") },
                new[] { L.T("تحديثات ويندوز المنزّلة", "Downloaded Windows updates"), Path.Combine(win, @"SoftwareDistribution\Download") },
                new[] { L.T("كاش Chrome", "Chrome cache"), Path.Combine(la, @"Google\Chrome\User Data\Default\Cache") },
                new[] { L.T("كاش Edge", "Edge cache"), Path.Combine(la, @"Microsoft\Edge\User Data\Default\Cache") },
                new[] { L.T("كاش Brave", "Brave cache"), Path.Combine(la, @"BraveSoftware\Brave-Browser\User Data\Default\Cache") },
                new[] { L.T("كاش Opera GX", "Opera GX cache"), Path.Combine(la, @"Opera Software\Opera GX Stable\Cache") },
                new[] { L.T("كاش Discord", "Discord cache"), Path.Combine(ra, @"discord\Cache") },
                new[] { L.T("كاش Steam (المتصفح)", "Steam browser cache"), Path.Combine(la, @"Steam\htmlcache") },
                new[] { L.T("كاش FiveM", "FiveM cache"), Path.Combine(la, @"FiveM\FiveM.app\data\cache") },
                new[] { L.T("سجلات Adobe", "Adobe logs"), Path.Combine(la, @"Adobe\Logs") },
                new[] { L.T("مثبّتات مؤقتة (Squirrel)", "Squirrel temp installers"), Path.Combine(la, "SquirrelTemp") },
                new[] { L.T("صور مصغّرة (Thumbnails)", "Thumbnail cache"), Path.Combine(la, @"Microsoft\Windows\Explorer") } };
            int i = 0;
            foreach (var it in items)
            {
                if (ctx.Cancel) break;
                ctx.StepProgress = (double)(++i) / (items.Length + 1);
                ctx.Current = it[1];
                if (!Directory.Exists(it[1])) continue;
                long size = it[1].EndsWith("Explorer") ? ThumbSize(it[1]) : U.FolderSize(it[1], ctx);
                if (size < (5L << 20)) continue;
                var row = new Row(it[0], L.T("يُعاد إنشاؤه تلقائياً - حذفه آمن", "Recreated automatically - safe to clear"), size, "", it[1], it[1].EndsWith("Explorer") ? "ClearThumbs" : "ClearDir", "");
                row.Check = true;
                rows.Add(row);
            }
            long rb = Native.RecycleBinSize();
            if (rb > 0) { var r = new Row(L.T("سلة المحذوفات", "Recycle Bin"), L.T("إفراغ السلة نهائياً", "Empty the bin permanently"), rb, "", "", "EmptyBin", ""); r.Check = true; rows.Add(r); }
            ctx.StepProgress = 1;
            return rows.OrderByDescending(x => x.Bytes).ToList();
        }

        static long ThumbSize(string dir)
        {
            long s = 0;
            try { foreach (string f in Directory.GetFiles(dir, "thumbcache_*.db")) s += new FileInfo(f).Length; } catch { }
            return s;
        }

        // ---------- desktop organizer
        public static string[][] DeskMap()
        {
            return new[] {
                new[] { L.T("1- الألعاب", "1- Games"), @"(?i)\.url$|steam|epic|ubisoft|origin|battle\.net|rockstar|game|launcher|fivem|roblox|riot|valorant|minecraft|wemod|xbox" },
                new[] { L.T("2- البرامج", "2- Apps"), @"(?i)\.lnk$" },
                new[] { L.T("3- المشاريع البرمجية", "3- Code projects"), @"(?i)\.(py|js|ts|php|html|css|json|sql|ipa|apk|cs|java|cpp)$|bot|server|src|-main$|dist$" },
                new[] { L.T("4- التصاميم", "4- Designs"), @"(?i)\.(psd|ai|eps|idml|indd|svg|xd|fig|png|jpe?g|webp|gif|ttf|otf|heic)$|font|design|تصميم" },
                new[] { L.T("5- المستندات", "5- Documents"), @"(?i)\.(pdf|docx?|xlsx?|pptx?|txt|csv|rtf|odt|md)$" },
                new[] { L.T("6- ملفات مضغوطة", "6- Archives"), @"(?i)\.(zip|rar|7z|tar|gz)$" },
                new[] { L.T("7- للمراجعة", "7- To review"), @"(?i)\.(exe|msi|iso|bat|cmd|reg)$|^مجلد جديد|^new folder" } };
        }

        public static List<Row> ScanDesktop(ScanCtx ctx) { return Desk.Scan(ctx); }
    }
}
