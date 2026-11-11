// W-Guard - Update checker (GitHub Releases). The only network access in the program.
// It reads https://api.github.com/repos/<owner>/<repo>/releases/latest (version + notes only).
// When a newer version exists, the user downloads it in the browser and runs it - W-Guard never
// downloads or starts the update itself.
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

        // GitHub's API allows a limited number of anonymous requests per hour from one internet address
        // (shared networks can run out). When it refuses, the public "latest release" link is used instead.
        public static ReleaseInfo GetLatest()
        {
            try { return GetLatestApi(); }
            catch (WebException ex)
            {
                Log.Write("update api: " + ex.Message + " - using the releases page");
                return GetLatestWeb();
            }
        }

        static ReleaseInfo GetLatestWeb()
        {
            string repo = "https://github.com/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName;
            var req = Req(repo + "/releases/latest");
            req.Method = "HEAD";
            req.AllowAutoRedirect = false;
            string loc;
            using (var resp = (HttpWebResponse)req.GetResponse()) loc = resp.Headers["Location"] ?? "";
            Match t = Regex.Match(loc, @"/releases/tag/([^/?#]+)");
            if (!t.Success) throw new WebException("no release found");
            var info = new ReleaseInfo();
            info.Tag = Uri.UnescapeDataString(t.Groups[1].Value);
            info.Page = repo + "/releases/tag/" + t.Groups[1].Value;
            info.Notes = L.T("التفاصيل في صفحة الإصدار.", "See the release page for details.");
            // release files carry the version in their names: W-Guard-Setup-1.4.exe, W-Guard-1.4.exe
            string dl = repo + "/releases/download/" + t.Groups[1].Value + "/", ver = info.Tag.TrimStart('v', 'V');
            info.ExeUrl = dl + "W-Guard-" + ver + ".exe"; info.ShaUrl = info.ExeUrl + ".sha256";
            info.SetupUrl = dl + "W-Guard-Setup-" + ver + ".exe"; info.SetupShaUrl = info.SetupUrl + ".sha256";
            Match m = Regex.Match(info.Tag, @"(\d+)\.(\d+)(\.(\d+))?");
            info.Version = m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0) : new Version(0, 0);
            return info;
        }

        static ReleaseInfo GetLatestApi()
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

        // installed with the setup (Program Files + uninstaller) -> update through the new setup, so Windows "Installed apps" shows the right version
        public static bool IsInstalled
        {
            get { return File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "unins000.exe")); }
        }

        // The update is never downloaded or run by W-Guard itself: the browser downloads it from the
        // official release and the user runs it (two steps). This keeps the program free of any
        // "download and execute" behaviour.
        public static string DownloadUrl(ReleaseInfo r, bool installed)
        {
            if (installed && !string.IsNullOrEmpty(r.SetupUrl)) return r.SetupUrl;
            if (!installed && !string.IsNullOrEmpty(r.ExeUrl)) return r.ExeUrl;
            if (!string.IsNullOrEmpty(r.Page)) return r.Page;
            return "https://github.com/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName + "/releases/latest";
        }

        public static void CleanupOld()
        {
            try { string old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { string tmp = Application.ExecutablePath + ".download"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}
