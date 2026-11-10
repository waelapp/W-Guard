// W-Guard - Update checker (GitHub Releases). The only network access in the program.
// It reads https://api.github.com/repos/<owner>/<repo>/releases/latest, and downloads the new
// W-Guard.exe only after the user clicks "Update". The file is accepted only if its SHA-256
// matches the W-Guard.exe.sha256 file published in the same release.
// © 2026 WAEL - https://github.com/waelapp
// W-Guard is free software, licensed under the GNU GPL v3.0 - see LICENSE. The name "W-Guard" and its logo belong to WAEL.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WGuard
{
    class ReleaseInfo
    {
        public Version Version;
        public string Tag = "", Notes = "", Date = "", Page = "", ExeUrl = "", ShaUrl = "", SetupUrl = "", SetupShaUrl = "";
        public long ExeSize, SetupSize;
    }

    static class Updater
    {
        public static ReleaseInfo Available;   // set when a newer version exists

        static HttpWebRequest Req(string url)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "W-Guard/" + AppInfo.VersionText;
            r.Timeout = 20000;
            r.ReadWriteTimeout = 30000;
            return r;
        }

        public static ReleaseInfo GetLatest()
        {
            var req = Req("https://api.github.com/repos/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName + "/releases/latest");
            req.Accept = "application/vnd.github+json";
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
            var js = new JavaScriptSerializer();
            var d = js.Deserialize<Dictionary<string, object>>(json);
            var info = new ReleaseInfo();
            info.Tag = Convert.ToString(d["tag_name"]);
            info.Notes = d.ContainsKey("body") ? Convert.ToString(d["body"]) : "";
            info.Page = d.ContainsKey("html_url") ? Convert.ToString(d["html_url"]) : "";
            info.Date = d.ContainsKey("published_at") ? Convert.ToString(d["published_at"]) : "";
            if (info.Date.Length >= 10) info.Date = info.Date.Substring(0, 10);
            Match m = Regex.Match(info.Tag, @"(\d+)\.(\d+)(\.(\d+))?");
            info.Version = m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0) : new Version(0, 0);
            if (d.ContainsKey("assets"))
            {
                foreach (object o in (IEnumerable)d["assets"])
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Convert.ToString(a["name"]).ToLowerInvariant();
                    string url = Convert.ToString(a["browser_download_url"]);
                    long sz; long.TryParse(Convert.ToString(a["size"]), out sz);
                    bool setup = name.Contains("setup");
                    if (name.EndsWith(".exe")) { if (setup) { info.SetupUrl = url; info.SetupSize = sz; } else { info.ExeUrl = url; info.ExeSize = sz; } }
                    else if (name.EndsWith(".sha256") || name.EndsWith(".sha256.txt")) { if (setup) info.SetupShaUrl = url; else info.ShaUrl = url; }
                }
            }
            return info;
        }

        public static bool IsNewer(ReleaseInfo r)
        {
            Version cur = AppInfo.Version;
            Version mine = new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build));
            return r != null && r.Version > mine;
        }

        // background check at startup; calls back on the UI thread only if a newer version exists
        public static void CheckAsync(Control ui, Action<ReleaseInfo> onNew, Action<string> onDone)
        {
            var th = new System.Threading.Thread(() =>
            {
                string msg;
                try
                {
                    var r = GetLatest();
                    if (IsNewer(r)) { Available = r; msg = "new"; if (onNew != null) ui.BeginInvoke(onNew, r); }
                    else msg = "latest";
                    Log.Write("update check: latest=" + r.Tag + " current=" + AppInfo.VersionText);
                }
                catch (Exception ex) { msg = "error:" + ex.Message; Log.Write("update check failed: " + ex.Message); }
                if (onDone != null) { try { ui.BeginInvoke(onDone, msg); } catch { } }
            });
            th.IsBackground = true;
            th.Start();
        }

        // downloads, verifies, then replaces the running exe (rename trick) and restarts
        // installed with the setup (Program Files + uninstaller) -> update through the new setup, so Windows "Installed apps" shows the right version
        public static bool IsInstalled
        {
            get { return File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "unins000.exe")); }
        }

        public static string DownloadAndVerify(ReleaseInfo r, Action<int> progress, Func<bool> cancelled)
        {
            bool viaSetup = IsInstalled && !string.IsNullOrEmpty(r.SetupUrl) && !string.IsNullOrEmpty(r.SetupShaUrl);
            string fileUrl = viaSetup ? r.SetupUrl : r.ExeUrl;
            string shaUrl = viaSetup ? r.SetupShaUrl : r.ShaUrl;
            long size = viaSetup ? r.SetupSize : r.ExeSize;
            if (string.IsNullOrEmpty(fileUrl)) throw new Exception(L.T("لا يوجد ملف exe في الإصدار", "No exe in this release"));
            if (string.IsNullOrEmpty(shaUrl)) throw new Exception(L.T("لا يوجد ملف التحقق sha256 في الإصدار", "No sha256 file in this release"));

            string expected;
            using (var resp = Req(shaUrl).GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream())) expected = sr.ReadToEnd();
            Match hm = Regex.Match(expected, "[0-9a-fA-F]{64}");
            if (!hm.Success) throw new Exception(L.T("ملف التحقق غير صالح", "Invalid sha256 file"));
            expected = hm.Value.ToLowerInvariant();

            string exe = Application.ExecutablePath;
            string tmp = viaSetup ? Path.Combine(Path.GetTempPath(), "W-Guard-Setup-" + r.Version.ToString(3) + ".exe") : exe + ".download";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var resp = Req(fileUrl).GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(tmp))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : size;
                byte[] buf = new byte[81920];
                long got = 0; int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelled()) { dst.Close(); File.Delete(tmp); throw new OperationCanceledException(); }
                    dst.Write(buf, 0, n); got += n;
                    if (total > 0) progress((int)(got * 100 / total));
                }
            }
            string actual = U.Hash256(tmp);
            if (actual != expected)
            {
                File.Delete(tmp);
                Log.Write("update hash mismatch: " + actual + " != " + expected);
                throw new Exception(L.T("فشل التحقق من التوقيع - أُلغي التحديث", "Signature check failed - update cancelled"));
            }
            Log.Write("update downloaded and verified: " + r.Tag);
            return tmp;
        }

        public static void ApplyAndRestart(string downloaded)
        {
            if (downloaded.EndsWith("-Setup-", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(downloaded).StartsWith("W-Guard-Setup", StringComparison.OrdinalIgnoreCase))
            {
                // the setup closes W-Guard (AppMutex), installs the new version and starts it again
                Log.Write("update via setup: " + downloaded);
                Process.Start(downloaded, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS");
                Application.Exit();
                return;
            }
            string exe = Application.ExecutablePath;
            string old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);          // a running exe can be renamed
            File.Move(downloaded, exe);
            Log.Write("update applied, restarting");
            Process.Start(exe, "--restart");
            Application.Exit();
        }

        public static void CleanupOld()
        {
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { string tmp = Application.ExecutablePath + ".download"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}
