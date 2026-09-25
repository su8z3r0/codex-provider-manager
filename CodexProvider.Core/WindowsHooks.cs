using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CodexProvider.Core
{
    /// <summary>Windows implementation: user env vars in HKCU registry, desktop app via ChatGPT.exe.</summary>
    public class WindowsHooks : IPlatformHooks
    {
        public string AuthCommand(string envVar, out string argsJson)
        {
            // read the value straight from the registry at request time: immune to stale env
            argsJson = "[\"-NoProfile\", \"-Command\", \"(Get-ItemProperty HKCU:\\\\Environment).'" + envVar + "'\"]";
            return "powershell";
        }

        public string GetUserEnvVar(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        }

        public void SetUserEnvVar(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        }

        public string RestartDesktopApp()
        {
            var procs = Process.GetProcessesByName("ChatGPT");
            if (procs.Length == 0) return "Desktop app is not running (nothing to restart).";
            string exePath = null;
            try { exePath = procs[0].MainModule?.FileName; } catch { }
            foreach (var p in procs) { try { p.Kill(); } catch { } }
            System.Threading.Thread.Sleep(2500);
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
                return "Desktop app restarted.";
            }
            // MSIX app: relaunch via the shell:AppsFolder alias
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App",
                    UseShellExecute = true
                };
                Process.Start(psi);
                return "Desktop app restarted (via app alias).";
            }
            catch (Exception ex) { return "App closed, but relaunch failed: " + ex.Message; }
        }

        public List<string> FetchModels(ProviderInfo p)
        {
            var list = new List<string>();
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var req = new HttpRequestMessage(HttpMethod.Get, p.BaseUrl.TrimEnd('/') + "/models");
                string key = KeyFor(p);
                if (!string.IsNullOrEmpty(key)) req.Headers.Add("Authorization", "Bearer " + key);
                var resp = http.SendAsync(req).GetAwaiter().GetResult();
                var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(body);
                foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
                    if (m.TryGetProperty("id", out var id)) list.Add(id.GetString());
            }
            catch (Exception ex) { list.Add("(error: " + ex.Message + ")"); }
            return list;
        }

        static string KeyFor(ProviderInfo p) => CodexConfig.KeyFor(p);
    }
}