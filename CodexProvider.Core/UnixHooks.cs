using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace CodexProvider.Core
{
    /// <summary>
    /// macOS/Linux implementation: user env vars persisted in ~/.codex/provider-keys.env
    /// (chmod 600), desktop app via osascript (mac) / not available (linux).
    /// </summary>
    public class UnixHooks : IPlatformHooks
    {
        static string KeysFile()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".codex", "provider-keys.env");
        }

        public string AuthCommand(string envVar, out string argsJson)
        {
            // source the keys file and print the var; runs at request time (no stale env)
            string f = KeysFile();
            argsJson = "[\"-c\", \"test -f " + f + " && . " + f + " && printf '%s' $" + envVar + " || printf '%s' ''\"]";
            return "sh";
        }

        public string GetUserEnvVar(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(v)) return v;
            string f = KeysFile();
            if (!File.Exists(f)) return null;
            foreach (string line in File.ReadAllLines(f))
            {
                string t = line.Trim();
                if (t.StartsWith("#") || t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                if (t.Substring(0, eq).Trim() == name)
                    return t.Substring(eq + 1).Trim().Trim('"');
            }
            return null;
        }

        public void SetUserEnvVar(string name, string value)
        {
            string f = KeysFile();
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            var lines = new List<string>();
            if (File.Exists(f)) lines.AddRange(File.ReadAllLines(f));
            string newline = name + "=\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq > 0 && lines[i].Substring(0, eq).Trim() == name) { lines[i] = newline; found = true; break; }
            }
            if (!found) lines.Add(newline);
            File.WriteAllLines(f, lines);
            Chmod600(f);
        }

        static void Chmod600(string path)
        {
            try
            {
                var psi = new ProcessStartInfo { FileName = "chmod", Arguments = "600 " + path, UseShellExecute = false, CreateNoWindow = true };
                Process.Start(psi)?.WaitForExit(3000);
            }
            catch { }
        }

        public string RestartDesktopApp()
        {
            if (OperatingSystem.IsMacOS())
            {
                var procs = Process.GetProcessesByName("ChatGPT");
                if (procs.Length == 0) return "Desktop app is not running (nothing to restart).";
                foreach (var p in procs) { try { p.Kill(); } catch { } }
                System.Threading.Thread.Sleep(2500);
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = "-c \"open -a 'Codex' || open -a 'ChatGPT'\"",
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                    return "Desktop app restarted.";
                }
                catch (Exception ex) { return "App closed, but relaunch failed: " + ex.Message; }
            }
            return "Desktop app restart is not supported on this platform (close/reopen manually).";
        }

        public List<string> FetchModels(ProviderInfo p)
        {
            var list = new List<string>();
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var req = new HttpRequestMessage(HttpMethod.Get, p.BaseUrl.TrimEnd('/') + "/models");
                string key = CodexConfig.KeyFor(p);
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
    }
}