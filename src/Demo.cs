// W-Guard - demo mode: sample data for screenshots and presentations (no real PC data is shown).
// Turned on by starting W-Guard with --demo, or by a file named demo.flag (containing "on") next to W-Guard.exe.
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WGuard
{
    static class Demo
    {
        public static bool On;

        public static void Detect(string[] args)
        {
            foreach (string a in args) if (a == "--demo") On = true;
            try
            {
                string flag = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "demo.flag");
                if (File.Exists(flag) && File.ReadAllText(flag).Trim().Equals("on", StringComparison.OrdinalIgnoreCase)) On = true;
            }
            catch { }
        }

        public static string PcName { get { return On ? "DEMO-PC" : Environment.MachineName; } }

        const long GB = 1L << 30, MB = 1L << 20;

        static Row R(string name, string detail, long bytes, string date, string path, string action, int level, string risk, bool check)
        {
            var r = new Row(name, detail, bytes, date, path, action, "");
            r.Level = level; r.Risk = risk; r.Check = check;
            return r;
        }

        public static SysInfo Info()
        {
            var s = new SysInfo();
            s.OS = "Windows 11 Pro"; s.Build = "26100";
            s.Cpu = "Intel(R) Core(TM) i7-12700K  ·  12" + L.T(" نواة / ", " cores / ") + "20" + L.T(" مسار", " threads");
            s.Gpus.Add("NVIDIA GeForce RTX 4070  ·  2560x1440 @165Hz");
            s.RamBytes = 32 * GB; s.RamSticks = 2; s.RamSpeed = 2400; s.RamRated = 3600; s.RamPart = "DDR4-3600 2x16GB";
            s.Board = "Demo Board Z690"; s.Bios = "1.20 (2025-03-01)";
            s.Disks.Add("NVMe SSD 1TB  ·  NVMe  ·  954 GB");
            s.Disks.Add("SATA HDD 2TB  ·  HDD  ·  1863 GB");
            s.Volumes.Add(new VolumeInfo { Letter = "C", Label = "", Size = 954 * GB, Free = 214 * GB, Media = "NVMe" });
            s.Volumes.Add(new VolumeInfo { Letter = "D", Label = "Games", Size = 1863 * GB, Free = 640 * GB, Media = "HDD" });
            s.DefenderKnown = true; s.RealTime = true; s.AvEnabled = true;
            s.SigUpdated = DateTime.Today; s.LastQuick = DateTime.Today.AddDays(-1); s.LastFull = DateTime.Today.AddDays(-12);
            s.HasHdd = true; s.PowerPlan = L.T("متوازن", "Balanced"); s.Uptime = TimeSpan.FromHours(9);
            return s;
        }

        public static void Load()
        {
            string d1 = U.D(DateTime.Today.AddDays(-3)), d2 = U.D(DateTime.Today.AddDays(-640)), d3 = U.D(DateTime.Today.AddDays(-820));
            Store.Info = Info();
            Store.Reset(Scanners.Space);
            Store.Add(Scanners.Space, new[] {
                R("Videos", L.T("مجلد · 1,204 ملف · القديم منه 61 GB", "Folder · 1,204 files · old part 61 GB"), 88 * GB, d1, @"C:\Users\User\Videos", "DeleteDir", 0, "", false),
                R("Downloads", L.T("مجلد · 3,980 ملف · القديم منه 22 GB", "Folder · 3,980 files · old part 22 GB"), 41 * GB, d1, @"C:\Users\User\Downloads", "DeleteDir", 0, "", false),
                R("SteamLibrary", L.T("مجلد · للعرض فقط", "Folder · view only"), 512 * GB, d1, @"D:\SteamLibrary", "", 0, "", false),
                R("Windows11_Install.iso", L.T("ملف كبير", "Large file"), 6 * GB, d3, @"C:\Users\User\Downloads\Windows11_Install.iso", "DeleteFile", 0, "", false),
                R("project-backup-2023.zip", L.T("ملف كبير", "Large file"), 12 * GB, d3, @"D:\Backups\project-backup-2023.zip", "DeleteFile", 0, "", false) });
            Store.Reset(Scanners.Old);
            Store.Add(Scanners.Old, new[] {
                R("trip-2022.mp4", L.T("آخر تعديل قبل 27 شهراً", "Last modified 27 months ago"), (long)(4.2 * GB), d3, @"C:\Users\User\Videos\trip-2022.mp4", "DeleteFile", 0, "", false),
                R("old-game-setup.exe", L.T("آخر تعديل قبل 21 شهراً", "Last modified 21 months ago"), (long)(2.8 * GB), d2, @"C:\Users\User\Downloads\old-game-setup.exe", "DeleteFile", 0, "", false),
                R("recording_0412.mkv", L.T("آخر تعديل قبل 20 شهراً", "Last modified 20 months ago"), (long)(1.6 * GB), d2, @"D:\Recordings\recording_0412.mkv", "DeleteFile", 0, "", false) });
            Store.Reset(Scanners.Games);
            Store.Add(Scanners.Games, new[] {
                R("Space Racer X", @"Steam · D:\SteamLibrary", 96 * GB, d2, "steam://uninstall/0", "SteamUninstall", 1, L.T("لم تُلعب منذ مدة طويلة", "Not played for a long time"), false),
                R("Castle Legends", @"Steam · D:\SteamLibrary", 64 * GB, d1, "steam://uninstall/0", "SteamUninstall", 0, "", false),
                R("Puzzle Quest Online", @"Epic Games · D:\Epic", 18 * GB, d3, @"D:\Epic\PuzzleQuest", "OpenEpic", 1, L.T("لم تُلعب منذ مدة طويلة", "Not played for a long time"), false) });
            Store.Reset(Scanners.Programs);
            Store.Add(Scanners.Programs, new[] {
                R("Web Companion", "Lavasoft 8.9", 120 * MB, d2, @"C:\Program Files\Lavasoft\Web Companion", "Uninstall", 2, L.T("برنامج دعائي/غير مرغوب معروف", "Known adware / unwanted program"), true),
                R("Old Toolbar Helper", "Unknown 1.0", 8 * MB, "", @"C:\Program Files\OldToolbar", "RemoveEntry", 1, L.T("بقايا: أداة الإزالة مفقودة", "Leftover: uninstaller missing"), false),
                R("Photo Editor Pro", "Demo Soft 5.2", 900 * MB, d2, @"C:\Program Files\PhotoEditor", "Uninstall", 1, L.T("لم يُستخدم منذ مدة طويلة", "Not used for a long time"), false),
                R("7-Zip", "Igor Pavlov 24.08", 6 * MB, d1, @"C:\Program Files\7-Zip", "Uninstall", 0, "", false) });
            Store.Reset(Scanners.Security);
            Store.Add(Scanners.Security, new[] {
                R("SysHelper", L.T("تشغيل تلقائي: ", "Autorun: ") + @"wscript.exe C:\Users\User\AppData\Roaming\syshelper.vbs", 0, "", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "RemoveRun", 2, L.T("يشغّل سكربت · من مجلد المستخدم", "Runs a script · from user folder"), true),
                R("UpdaterTask", L.T("مهمة مجدولة: ", "Scheduled task: ") + @"C:\ProgramData\upd\updater.exe", 0, "", @"\UpdaterTask", "DisableTask", 1, L.T("غير موقّع رقمياً", "Not digitally signed"), false),
                R("Discord", L.T("تشغيل تلقائي: ", "Autorun: ") + "Update.exe --processStart Discord.exe", 0, "", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "RemoveRun", 0, "", false),
                R("Defender: Trojan:Win32/Demo", @"C:\Users\User\Downloads\crack.exe", 0, U.D(DateTime.Today.AddDays(-5)), "", "", 3, L.T("عولج بنجاح", "Handled"), false) });
            Store.Reset(Scanners.Background);
            Store.Add(Scanners.Background, new[] {
                R(L.T("[بدء التشغيل] ", "[Startup] ") + "Discord", L.T("يقلع مع ويندوز", "Starts with Windows"), 0, "", @"HKCU\...\Run", "DisableStartup", 1, L.T("غير ضروري للإقلاع - تعطيله يسرّع التشغيل", "Not needed at startup - disabling speeds up boot"), false),
                R(L.T("[بدء التشغيل] ", "[Startup] ") + "Steam", L.T("يقلع مع ويندوز", "Starts with Windows"), 0, "", @"HKCU\...\Run", "DisableStartup", 1, L.T("غير ضروري للإقلاع - تعطيله يسرّع التشغيل", "Not needed at startup - disabling speeds up boot"), false),
                R("chrome", L.T("المعالج 2.4%  ·  14 نسخة  ·  Google LLC", "CPU 2.4%  ·  14 instances  ·  Google LLC"), (long)(1.4 * GB), "", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "StopProcess", 1, L.T("متصفح - الاستهلاك يزيد مع عدد التبويبات المفتوحة", "Browser - usage grows with open tabs"), false),
                R("Discord", L.T("المعالج 0.6%  ·  6 نسخة  ·  Discord Inc.", "CPU 0.6%  ·  6 instances  ·  Discord Inc."), 780 * MB, "", @"C:\Users\User\AppData\Local\Discord\Discord.exe", "StopProcess", 1, L.T("غير ضروري للإقلاع - شغّله عند الحاجة", "Not needed at startup - open it when you need it"), false),
                R("nvcontainer", L.T("المعالج 0.1%  ·  خدمة  ·  NVIDIA Corporation", "CPU 0.1%  ·  service  ·  NVIDIA Corporation"), 210 * MB, "", @"C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe", "StopProcess", 3, L.T("تعريف كرت الشاشة NVIDIA - اتركه", "NVIDIA graphics driver - keep it"), false) });
            Store.Reset(Scanners.Cleanup);
            Store.Add(Scanners.Cleanup, new[] {
                R(L.T("ملفات المستخدم المؤقتة", "User temp files"), L.T("يُعاد إنشاؤه تلقائياً - حذفه آمن", "Recreated automatically - safe to clear"), (long)(3.8 * GB), "", @"C:\Users\User\AppData\Local\Temp", "ClearDir", 0, "", true),
                R(L.T("كاش الشيدر NVIDIA DX", "NVIDIA DX shader cache"), L.T("يُعاد إنشاؤه تلقائياً - حذفه آمن", "Recreated automatically - safe to clear"), (long)(2.1 * GB), "", @"C:\Users\User\AppData\Local\NVIDIA\DXCache", "ClearDir", 0, "", true),
                R(L.T("تحديثات ويندوز المنزّلة", "Downloaded Windows updates"), L.T("يُعاد إنشاؤه تلقائياً - حذفه آمن", "Recreated automatically - safe to clear"), (long)(1.7 * GB), "", @"C:\Windows\SoftwareDistribution\Download", "ClearDir", 0, "", true),
                R(L.T("سلة المحذوفات", "Recycle Bin"), L.T("إفراغ السلة نهائياً", "Empty the bin permanently"), 900 * MB, "", "", "EmptyBin", 0, "", true) });
            Store.Reset(Scanners.Desktop);
            var desk = new List<Row>();
            foreach (var x in new[] { new[] { "Steam.lnk", "games" }, new[] { "Valorant.lnk", "games" }, new[] { "Photoshop.lnk", "apps" }, new[] { "my-bot", "code" },
                                      new[] { "logo-final.psd", "designs" }, new[] { "clip.mp4", "media" }, new[] { "invoice.pdf", "docs" }, new[] { "files.zip", "archives" }, new[] { "setup.exe", "review" } })
            {
                var r = R(x[0], "", 0, d1, @"C:\Users\User\Desktop\" + x[0], "MoveDesk", 0, L.T("تصنيف تلقائي", "Auto-sorted"), true);
                Desk.SetTarget(r, x[1]);
                desk.Add(r);
            }
            Store.Add(Scanners.Desktop, desk);
            Store.LastFullScan = DateTime.Now;
        }

        public static List<ScanStep> Steps()
        {
            var names = new[] {
                new[] { L.T("مواصفات الجهاز والحماية", "System specs & protection"), @"root\cimv2" },
                new[] { L.T("القرص C:", "Drive C:"), @"C:\Users\User\Videos" },
                new[] { L.T("القرص D:", "Drive D:"), @"D:\SteamLibrary\steamapps\common" },
                new[] { L.T("الألعاب", "Games"), @"D:\SteamLibrary\steamapps" },
                new[] { L.T("البرامج المثبتة", "Installed programs"), @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" },
                new[] { L.T("الأمان والتشغيل التلقائي", "Security & autostart"), @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run" },
                new[] { L.T("الخلفية وبدء التشغيل", "Background & startup"), L.T("قياس استهلاك المعالج...", "Measuring CPU usage...") },
                new[] { L.T("الملفات المؤقتة", "Temporary files"), @"C:\Users\User\AppData\Local\Temp" } };
            var steps = new List<ScanStep>();
            foreach (var n in names)
            {
                string path = n[1];
                steps.Add(new ScanStep(n[0], 10, ctx =>
                {
                    for (int i = 1; i <= 20 && !ctx.Cancel; i++) { ctx.Current = path; ctx.Files += 4211; ctx.StepProgress = i / 20.0; Thread.Sleep(300); }
                }));
            }
            steps.Add(new ScanStep(L.T("إعداد التقرير", "Building the report"), 1, ctx => { Load(); ctx.StepProgress = 1; }));
            return steps;
        }
    }
}
