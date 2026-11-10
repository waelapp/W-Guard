// W-Guard - background apps: resource use + advice (is it needed at startup?)
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WGuard
{
    static class Advice
    {
        // level: 3 = important (keep), 1 = optional (not needed at startup), 2 = check it (remote control etc.)
        class Tip { public string Pattern; public int Level; public string Ar, En; }

        static Tip T(string p, int lv, string ar, string en) { return new Tip { Pattern = "(?i)" + p, Level = lv, Ar = ar, En = en }; }

        static readonly Tip[] Tips = {
            T(@"remoterelay|anydesk|teamviewer|rustdesk|ultraviewer|ammyy|supremo|splashtop|parsec", 2, "تحكم بالجهاز عن بُعد - تأكد أنك ثبّته بنفسك، وأوقفه إن لم تستخدمه", "Remote control tool - make sure you installed it; stop it if unused"),
            T(@"^nvcontainer|nvdisplay\.container|nvidia web helper|^nvcplui", 3, "تعريف كرت الشاشة NVIDIA - اتركه", "NVIDIA graphics driver - keep it"),
            T(@"nvidia overlay|nvsphelper|shadowplay", 1, "طبقة NVIDIA والتسجيل - عطّلها من تطبيق NVIDIA إن لم تسجّل", "NVIDIA overlay/recording - turn off in the NVIDIA app if you don't record"),
            T(@"rtkaud|realtek|nahimic|waves|dolby|sonic studio", 3, "تعريف الصوت - اتركه", "Audio driver - keep it"),
            T(@"msmpeng|securityhealth|nissrv|mpdefender", 3, "حماية ويندوز - اتركها", "Windows protection - keep it"),
            T(@"^gamingservices|xboxservice|^gameinput|xboxgamebar", 3, "خدمات Xbox والإدخال - مطلوبة لألعاب Game Pass والأيادي", "Xbox & input services - needed for Game Pass and controllers"),
            T(@"asusfancontrol", 3, "التحكم بالمراوح - اتركه", "Fan control - keep it"),
            T(@"nefarius|vigem|virtualpad", 3, "تعريف يد تحكم افتراضية - اتركه إذا تستخدم يد تحكم", "Virtual controller driver - keep if you use a gamepad"),
            T(@"inputmapper|hidguardian", 1, "InputMapper قديم - إذا لم تعد تستخدمه أزله؛ HidGuardian قد يسبب مشاكل مع الأيادي", "Old InputMapper - remove if unused; HidGuardian can break controllers"),
            T(@"msedgewebview2", 3, "مكوّن ويندوز تستخدمه برامج كثيرة - طبيعي", "Windows component used by many apps - normal"),
            T(@"officeclicktorun", 3, "خدمة Microsoft Office - طبيعي", "Microsoft Office service - normal"),
            T(@"snippingtool|screensketch", 3, "أداة القص في ويندوز - طبيعي", "Windows Snipping Tool - normal"),
            T(@"^discord|^steam$|steamwebhelper|epicgameslauncher|^spotify|ms-teams|^teams|^slack|^telegram|whatsapp|battle\.net|^riotclient|ubisoftconnect|^origin|eadesktop|galaxyclient", 1, "غير ضروري للإقلاع - شغّله عند الحاجة", "Not needed at startup - open it when you need it"),
            T(@"^chrome|^msedge$|^brave|^opera|^firefox|^vivaldi", 1, "متصفح - الاستهلاك يزيد مع عدد التبويبات المفتوحة", "Browser - usage grows with open tabs"),
            T(@"onedrive|filecoauth", 1, "OneDrive - عطّله إن لم تستخدم المزامنة السحابية", "OneDrive - disable if you don't sync to the cloud"),
            T(@"phoneexperiencehost|crossdevice|yourphone", 1, "ربط الهاتف - عطّله إن لم تستخدمه", "Phone Link - disable if unused"),
            T(@"^widgets|widgetservice|widgetsplatform", 1, "أدوات ويندوز (الطقس والأخبار) - يمكن تعطيلها", "Windows widgets - can be turned off"),
            T(@"wallpaper(32|64)|wallpaper_engine|lively", 1, "خلفية متحركة - تستهلك كرت الشاشة؛ فعّل الإيقاف المؤقت أثناء الألعاب", "Animated wallpaper - uses the GPU; enable pause while gaming"),
            T(@"warp-svc|cloudflare", 1, "Cloudflare WARP (VPN) - أوقفه إن لم تستخدمه", "Cloudflare WARP (VPN) - stop it if unused"),
            T(@"armsvc|adobeupdate|adobenotification|adobearm|ccxprocess|coresync|creative cloud|adobe desktop", 1, "تحديثات Adobe - غير ضرورية للإقلاع", "Adobe updaters - not needed at startup"),
            T(@"lghub", 1, "Logitech G HUB - اتركه إن كنت تعتمد على إعدادات الماوس/السماعة", "Logitech G HUB - keep if you rely on its mouse/headset settings"),
            T(@"asus_framework|armourycrate|armourysw|rogliveservice|atkexcomsvc|asuscertservice|gamesdk|acpowernotification|asusoptimization", 1, "ASUS Armoury Crate - ثقيل؛ اتركه فقط إن كنت تحتاجه للإضاءة أو المراوح", "ASUS Armoury Crate - heavy; keep only if you need it for RGB/fans"),
            T(@"^aac|lightingservice|sonixddr|aura", 1, "إضاءة RGB من ASUS - غير ضرورية للأداء", "ASUS RGB lighting - not needed for performance"),
            T(@"nzxt|\\cam\\", 1, "NZXT CAM - مراقبة وإضاءة؛ قد يتعارض مع برامج إضاءة ASUS", "NZXT CAM - monitoring/RGB; may conflict with ASUS lighting apps"),
            T(@"goxlr", 3, "مطلوب إذا تستخدم مكسر GoXLR", "Needed if you use a GoXLR mixer"),
            T(@"applemobiledevice|itunes", 1, "لمزامنة الآيفون مع الكمبيوتر فقط - أوقفه إن لم تستخدمه", "Only for iPhone sync - stop if unused"),
            T(@"codex|claude|cowork-svc|chatgpt", 3, "مساعد ذكاء اصطناعي - طبيعي إن كنت تستخدمه", "AI assistant - normal if you use it"),
            T(@"^ccleaner|driverbooster|iobit|avast|avg|mcafee|norton|360", 1, "برنامج تنظيف/حماية إضافي - ويندوز يكفيك غالباً", "Extra cleaner/antivirus - Windows usually covers this")
        };

        public static int For(string name, string path, out string text)
        {
            string hay = name + " " + (path ?? "");
            foreach (var t in Tips)
            {
                if (Regex.IsMatch(name, t.Pattern) || Regex.IsMatch(hay, t.Pattern)) { text = L.T(t.Ar, t.En); return t.Level; }
            }
            text = "";
            return 0;
        }

        static Dictionary<int, TimeSpan> CpuSnapshot()
        {
            var d = new Dictionary<int, TimeSpan>();
            foreach (var p in Process.GetProcesses())
            {
                try { d[p.Id] = p.TotalProcessorTime; } catch { }
                p.Dispose();
            }
            return d;
        }

        static HashSet<string> StartupNames()
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rk in new[] { new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run" }, new[] { "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run" }, new[] { "HKLM32", @"Software\Microsoft\Windows\CurrentVersion\Run" } })
            {
                try
                {
                    using (var h = Scanners.OpenHive(rk[0])) using (var k = h.OpenSubKey(rk[1]))
                    {
                        if (k == null) continue;
                        foreach (string n in k.GetValueNames())
                        {
                            string exe = Risk.ExePath(Convert.ToString(k.GetValue(n)));
                            if (exe != null) s.Add(Path.GetFileNameWithoutExtension(exe));
                        }
                    }
                }
                catch { }
            }
            return s;
        }

        public static List<Row> ScanBackground(ScanCtx ctx)
        {
            var rows = new List<Row>();
            string win = (Environment.GetEnvironmentVariable("windir") ?? "C:\\Windows").ToLowerInvariant();
            int self = Process.GetCurrentProcess().Id;
            int cores = Math.Max(1, Environment.ProcessorCount);

            var cpu1 = CpuSnapshot();
            var sw = Stopwatch.StartNew();
            ctx.Current = L.T("قياس استهلاك المعالج...", "Measuring CPU usage...");
            System.Threading.Thread.Sleep(1500);
            var cpu2 = CpuSnapshot();
            double secs = sw.Elapsed.TotalSeconds;
            ctx.StepProgress = 0.35;

            var servicePids = new Dictionary<long, string>();
            foreach (var o in U.Wmi(@"root\cimv2", "SELECT ProcessId,DisplayName FROM Win32_Service WHERE State='Running'"))
                servicePids[U.N(o, "ProcessId")] = U.S(o, "DisplayName");
            var startup = StartupNames();

            var groups = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase); // ram, count, cpu, isService
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in U.Wmi(@"root\cimv2", "SELECT ProcessId,Name,ExecutablePath,WorkingSetSize FROM Win32_Process"))
            {
                string path = U.S(o, "ExecutablePath");
                long pid = U.N(o, "ProcessId");
                if (path.Length == 0 || path.ToLowerInvariant().StartsWith(win) || pid == self) continue;
                string n = Path.GetFileNameWithoutExtension(U.S(o, "Name"));
                double[] a;
                if (!groups.TryGetValue(n, out a)) { a = new double[4]; groups[n] = a; paths[n] = path; }
                a[0] += U.N(o, "WorkingSetSize");
                a[1]++;
                TimeSpan t1, t2;
                if (cpu1.TryGetValue((int)pid, out t1) && cpu2.TryGetValue((int)pid, out t2))
                    a[2] += (t2 - t1).TotalSeconds / secs / cores * 100.0;
                if (servicePids.ContainsKey(pid)) a[3] = 1;
            }
            ctx.StepProgress = 0.6;

            foreach (var kv in groups)
            {
                string path = paths[kv.Key];
                string company = "";
                try { company = FileVersionInfo.GetVersionInfo(path).CompanyName ?? ""; } catch { }
                double[] a = kv.Value;
                var parts = new List<string>();
                parts.Add(L.T("المعالج ", "CPU ") + a[2].ToString("0.0") + "%");
                if (a[1] > 1) parts.Add(((int)a[1]) + L.T(" نسخة", " instances"));
                if (a[3] > 0) parts.Add(L.T("خدمة", "service"));
                if (startup.Contains(kv.Key)) parts.Add(L.T("يقلع مع ويندوز", "starts with Windows"));
                if (company.Length > 0) parts.Add(company);
                var row = new Row(kv.Key, string.Join("  ·  ", parts.ToArray()), (long)a[0], "", path, "StopProcess", kv.Key);
                string tip;
                int lv = For(kv.Key, path, out tip);
                if (tip.Length == 0)
                {
                    tip = a[3] > 0 ? L.T("خدمة لبرنامج مثبت - اتركها إلا إذا لم تعد تستخدمه", "Service of an installed app - keep unless you no longer use the app")
                                   : L.T("برنامج مفتوح - ابحث عنه (كليك يمين) إن لم تعرفه", "Open app - search it (right-click) if you don't know it");
                }
                if (a[2] >= 10) { tip = L.T("يستهلك المعالج بشكل ملحوظ الآن · ", "Using noticeable CPU now · ") + tip; if (lv == 0 || lv == 3) lv = 1; }
                row.Risk = tip;
                row.Level = lv;
                rows.Add(row);
            }
            ctx.StepProgress = 0.8;

            // startup entries that are enabled
            var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ak in new[] { new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" }, new[] { "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" }, new[] { "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32" } })
            {
                try
                {
                    using (var h = Scanners.OpenHive(ak[0])) using (var k = h.OpenSubKey(ak[1]))
                    {
                        if (k == null) continue;
                        foreach (string n in k.GetValueNames()) { var b = k.GetValue(n) as byte[]; if (b != null && b.Length > 0 && (b[0] & 1) == 1) disabled.Add(n); }
                    }
                }
                catch { }
            }
            var startRows = new List<Row>();
            foreach (var rk in new[] { new[] { "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run" }, new[] { "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run" }, new[] { "HKLM32", @"Software\Microsoft\Windows\CurrentVersion\Run" } })
            {
                try
                {
                    using (var h = Scanners.OpenHive(rk[0])) using (var k = h.OpenSubKey(rk[1]))
                    {
                        if (k == null) continue;
                        foreach (string n in k.GetValueNames())
                        {
                            if (n.Length == 0 || disabled.Contains(n)) continue;
                            string cmd = Convert.ToString(k.GetValue(n));
                            var row = new Row(n, L.T("يقلع مع ويندوز: ", "Starts with Windows: ") + cmd, 0, "", rk[0] + "\\" + rk[1], "DisableStartup", n);
                            string tip;
                            int lv = For(n, cmd, out tip);
                            if (lv == 3) { row.Risk = L.T("مهم للإقلاع - لا تعطّله · ", "Needed at startup - keep · ") + tip; row.Level = 3; }
                            else if (lv == 2) { row.Risk = tip; row.Level = 2; }
                            else { row.Risk = L.T("غير ضروري للإقلاع - تعطيله يسرّع التشغيل", "Not needed at startup - disabling speeds up boot") + (tip.Length > 0 ? " · " + tip : ""); row.Level = 1; }
                            row.Name = L.T("[بدء التشغيل] ", "[Startup] ") + n;
                            startRows.Add(row);
                        }
                    }
                }
                catch { }
            }
            ctx.StepProgress = 1;
            var result = startRows.OrderByDescending(x => x.Level == 2).ThenBy(x => x.Level == 3).ThenBy(x => x.Name).ToList();
            result.AddRange(rows.OrderByDescending(x => x.Bytes));
            return result;
        }
    }
}
