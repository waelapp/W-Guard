// W-Guard - Actions (only run after the user selects items and confirms)
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace WGuard
{
    static class Actions
    {
        public static string UndoFile { get { return Path.Combine(AppInfo.DataDir, "desktop-undo.txt"); } }

        public static string Label(string section)
        {
            switch (section)
            {
                case Scanners.Space:
                case Scanners.Old: return L.T("حذف المحدد", "Delete selected");
                case Scanners.Games: return L.T("إلغاء تثبيت المحدد", "Uninstall selected");
                case Scanners.Programs: return L.T("إزالة المحدد", "Remove selected");
                case Scanners.Security: return L.T("تعطيل/إزالة المحدد", "Disable/remove selected");
                case Scanners.Background: return L.T("إيقاف/تعطيل المحدد", "Stop/disable selected");
                case Scanners.Cleanup: return L.T("تنظيف المحدد", "Clean selected");
                case Scanners.Desktop: return L.T("ترتيب المحدد", "Organize selected");
            }
            return "";
        }

        public static bool IsDestructive(string section)
        {
            return section == Scanners.Space || section == Scanners.Old || section == Scanners.Cleanup || section == Scanners.Programs;
        }

        // returns a short description of what happened; throws on failure
        public static string Run(Row r, bool recycle)
        {
            switch (r.Action)
            {
                case "DeleteFile":
                    Guard(r.Path);
                    FileSystem.DeleteFile(r.Path, UIOption.OnlyErrorDialogs, recycle ? RecycleOption.SendToRecycleBin : RecycleOption.DeletePermanently);
                    return recycle ? L.T("نُقل إلى السلة", "Moved to Recycle Bin") : L.T("حُذف", "Deleted");
                case "DeleteDir":
                    Guard(r.Path);
                    FileSystem.DeleteDirectory(r.Path, UIOption.OnlyErrorDialogs, recycle ? RecycleOption.SendToRecycleBin : RecycleOption.DeletePermanently);
                    return recycle ? L.T("نُقل إلى السلة", "Moved to Recycle Bin") : L.T("حُذف", "Deleted");
                case "ClearDir":
                    return ClearDir(r.Path);
                case "ClearThumbs":
                    int n = 0;
                    foreach (string f in Directory.GetFiles(r.Path, "thumbcache_*.db")) { try { File.Delete(f); n++; } catch { } }
                    return L.T("نُظّف ", "Cleared ") + n;
                case "EmptyBin":
                    Native.SHEmptyRecycleBin(IntPtr.Zero, null, 7);
                    return L.T("أُفرغت السلة", "Recycle Bin emptied");
                case "SteamUninstall":
                    U.Open(r.Path);
                    return L.T("فُتحت نافذة Steam للتأكيد", "Steam opened to confirm");
                case "OpenEpic":
                    U.Open("com.epicgames.launcher://store/library");
                    return L.T("أكمل الإزالة من Epic", "Finish in Epic Games");
                case "OpenXbox":
                    U.Open("ms-settings:appsfeatures");
                    return L.T("أكمل الإزالة من الإعدادات", "Finish in Settings");
                case "Uninstall":
                    return Uninstall(r);
                case "RemoveEntry":
                    return RemoveUninstallEntry(r);
                case "RemoveRun":
                    return RemoveRun(r);
                case "DisableTask":
                    {
                        Type t = Type.GetTypeFromProgID("Schedule.Service");
                        dynamic svc = Activator.CreateInstance(t);
                        svc.Connect();
                        dynamic task = svc.GetFolder("\\").GetTask(r.Key);
                        task.Enabled = false;
                        return L.T("عُطّلت المهمة", "Task disabled");
                    }
                case "DisableService":
                    {
                        try
                        {
                            using (var sc = new ServiceController(r.Key))
                            {
                                if (sc.Status != ServiceControllerStatus.Stopped && sc.CanStop) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15)); }
                            }
                        }
                        catch { }
                        using (var mo = new ManagementObject("Win32_Service.Name='" + r.Key.Replace("'", "\\'") + "'"))
                        {
                            object res = mo.InvokeMethod("ChangeStartMode", new object[] { "Disabled" });
                            if (Convert.ToInt32(res) != 0) throw new Exception("ChangeStartMode=" + res);
                        }
                        return L.T("أُوقفت وعُطّلت", "Stopped and disabled");
                    }
                case "StopProcess":
                    {
                        int k = 0;
                        foreach (var p in Process.GetProcessesByName(r.Key))
                        {
                            try { if (p.Id != Process.GetCurrentProcess().Id) { p.Kill(); k++; } } catch { }
                        }
                        return L.T("أُغلق ", "Closed ") + k;
                    }
                case "DisableStartup":
                    return DisableStartup(r);
                case "MoveDesk":
                    return MoveDesk(r);
            }
            return L.T("لا يوجد إجراء", "No action");
        }

        static void Guard(string p)
        {
            if (!Safety.IsSafe(p)) throw new Exception(L.T("مسار محمي - رُفض", "Protected path - refused"));
        }

        static string ClearDir(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return L.T("غير موجود", "Not found");
            bool wu = path.IndexOf("SoftwareDistribution", StringComparison.OrdinalIgnoreCase) >= 0;
            ServiceController sc = null;
            if (wu)
            {
                try { sc = new ServiceController("wuauserv"); if (sc.Status == ServiceControllerStatus.Running) { sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); } else { sc.Dispose(); sc = null; } }
                catch { sc = null; }
            }
            int ok = 0, skipped = 0;
            var di = new DirectoryInfo(path);
            foreach (var f in di.EnumerateFiles())
            {
                try { f.Attributes = FileAttributes.Normal; f.Delete(); ok++; } catch { skipped++; }
            }
            foreach (var d in di.EnumerateDirectories())
            {
                try
                {
                    if ((d.Attributes & FileAttributes.ReparsePoint) != 0) { d.Delete(); continue; }
                    d.Delete(true); ok++;
                }
                catch { skipped++; }
            }
            if (sc != null) { try { sc.Start(); } catch { } sc.Dispose(); }
            return L.T("نُظّف ", "Cleared ") + ok + (skipped > 0 ? L.T(" (تُرك ", " (skipped ") + skipped + L.T(" مستخدم حالياً)", " in use)") : "");
        }

        static RegistryKey OpenUninstallKey(Row r, bool writable, out string sub)
        {
            string[] parts = r.Key.Split('|');
            var hive = (RegistryHive)Enum.Parse(typeof(RegistryHive), parts[0]);
            var view = (RegistryView)Enum.Parse(typeof(RegistryView), parts[1]);
            sub = parts[2];
            return RegistryKey.OpenBaseKey(hive, view);
        }

        static string Uninstall(Row r)
        {
            string sub;
            string cmd = null;
            using (var bk = OpenUninstallKey(r, false, out sub))
            using (var k = bk.OpenSubKey(sub))
            {
                if (k == null) return L.T("أُزيل مسبقاً", "Already removed");
                cmd = Convert.ToString(k.GetValue("UninstallString"));
            }
            if (string.IsNullOrEmpty(cmd)) throw new Exception(L.T("لا توجد أداة إزالة", "No uninstaller"));
            if (cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0)
                cmd = System.Text.RegularExpressions.Regex.Replace(cmd, @"(?i)/I\s*\{", "/X{");
            Log.Write("uninstall: " + r.Name + " | " + cmd);
            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"") { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi)) { p.WaitForExit(); }
            return L.T("انتهت أداة الإزالة", "Uninstaller finished");
        }

        static string RemoveUninstallEntry(Row r)
        {
            string sub;
            using (var bk = OpenUninstallKey(r, true, out sub))
            {
                Backup("reg", r.Key);
                bk.DeleteSubKeyTree(sub, false);
            }
            return L.T("أُزيلت البقايا من القائمة", "Leftover entry removed");
        }

        static string RemoveRun(Row r)
        {
            int i = r.Path.IndexOf('\\');
            string hive = r.Path.Substring(0, i), sub = r.Path.Substring(i + 1);
            using (var h = Scanners.OpenHive(hive))
            using (var k = h.OpenSubKey(sub, true))
            {
                if (k == null) return L.T("غير موجود", "Not found");
                Backup("run", r.Path + " | " + r.Key + " | " + Convert.ToString(k.GetValue(r.Key)));
                k.DeleteValue(r.Key, false);
            }
            return L.T("أُزيل (مع نسخة احتياطية)", "Removed (backed up)");
        }

        static string DisableStartup(Row r)
        {
            int i = r.Path.IndexOf('\\');
            string hive = r.Path.Substring(0, i);
            string ak = hive == "HKLM32" ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32" : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
            using (var h = Scanners.OpenHive(hive == "HKLM32" ? "HKLM" : hive))
            using (var k = h.CreateSubKey(ak))
            {
                byte[] b = new byte[12];
                b[0] = 3;
                BitConverter.GetBytes(DateTime.Now.ToFileTimeUtc()).CopyTo(b, 4);
                k.SetValue(r.Key, b, RegistryValueKind.Binary);
            }
            return L.T("عُطّل من بدء التشغيل (يمكن إعادته من مدير المهام)", "Disabled at startup (re-enable from Task Manager)");
        }

        static string MoveDesk(Row r)
        {
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string dir = Path.Combine(desk, Desk.Name(r.Key));
            Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, Path.GetFileName(r.Path));
            if (File.Exists(dst) || Directory.Exists(dst)) return L.T("موجود مسبقاً - تُرك", "Already exists - skipped");
            if (Directory.Exists(r.Path)) Directory.Move(r.Path, dst); else File.Move(r.Path, dst);
            File.AppendAllText(UndoFile, dst + "|" + r.Path + Environment.NewLine, Encoding.UTF8);
            return L.T("نُقل", "Moved");
        }

        public static void CreateDeskFolder(string id)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Desk.Name(id));
                if (Directory.Exists(dir)) return;
                Directory.CreateDirectory(dir);
                File.AppendAllText(UndoFile, Path.Combine(dir, ".") + "|" + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) { Log.Write("create desk folder: " + ex.Message); }
        }

        public static int UndoDesktop()
        {
            if (!File.Exists(UndoFile)) return -1;
            int n = 0;
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string l in File.ReadAllLines(UndoFile, Encoding.UTF8).Reverse())
            {
                string[] p = l.Split('|');
                if (p.Length != 2) continue;
                try { dirs.Add(Path.GetDirectoryName(p[0])); } catch { }
                if (p[1].Length == 0) continue;
                try
                {
                    if (File.Exists(p[1]) || Directory.Exists(p[1])) continue;
                    if (Directory.Exists(p[0])) { Directory.Move(p[0], p[1]); n++; }
                    else if (File.Exists(p[0])) { File.Move(p[0], p[1]); n++; }
                }
                catch { }
            }
            File.Delete(UndoFile);
            // remove the category folders that became empty
            foreach (string d in dirs)
            {
                try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
            }
            Log.Write("desktop undo: " + n);
            return n;
        }

        static void Backup(string kind, string text)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppInfo.LogDir, "registry-backup.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  [" + kind + "]  " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static void DefenderQuickScan()
        {
            string mp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            if (!File.Exists(mp)) { U.Open("windowsdefender://threat"); return; }
            var psi = new ProcessStartInfo(mp, "-Scan -ScanType 1") { UseShellExecute = false, CreateNoWindow = true };
            Process.Start(psi);
            Log.Write("Defender quick scan started");
        }
    }
}
