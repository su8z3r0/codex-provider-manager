using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodexProvider.Core
{
    public class ProviderInfo { public string Id; public string Name; public string BaseUrl; public string EnvKey; public string WireApi; }

    /// <summary>
    /// Cross-platform management of the Codex config (~/.codex/config.toml).
    /// Ported from the Windows-only WinForms app; platform specifics (key storage,
    /// restart of the desktop app) are delegated to IPlatformHooks.
    /// </summary>
    public static class CodexConfig
    {
        public static string ConfigPath;
        public static string ModelsMapPath;
        public static string SettingsPath;
        public static string BackupPath;

        public static List<ProviderInfo> Providers = new List<ProviderInfo>();
        public static string CurrentProvider;
        public static string CurrentModel;
        public static bool RestartAfterSwitch = true;
        public static Dictionary<string, string> DefaultModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Platform hooks injected at startup (key store, desktop-app restarter, process exec).</summary>
        public static IPlatformHooks Platform = null;

        static CodexConfig()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string codexDir = Path.Combine(home, ".codex");
            ConfigPath = Path.Combine(codexDir, "config.toml");
            ModelsMapPath = Path.Combine(codexDir, "codex-provider.models.json");
            SettingsPath = Path.Combine(codexDir, "codex-provider.settings.json");
            BackupPath = ConfigPath + ".bak-gui";
        }

        // ------------------------------------------------------------ load
        public static void Load()
        {
            LoadModelMap();
            Providers.Clear();
            CurrentProvider = null;
            CurrentModel = null;
            if (!File.Exists(ConfigPath)) return;
            string[] lines = File.ReadAllLines(ConfigPath);
            string section = null;
            string pendingAuthSection = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("["))
                {
                    section = line.Trim('[', ']');
                    pendingAuthSection = null;
                    if (section.StartsWith("model_providers."))
                    {
                        string id = section.Substring("model_providers.".Length);
                        if (!id.Contains(".")) Providers.Add(new ProviderInfo { Id = id });
                        else if (section.EndsWith(".auth")) pendingAuthSection = id.Substring(0, id.Length - ".auth".Length);
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim().Trim('"');
                if (section == null)
                {
                    if (key == "model_provider" && CurrentProvider == null) CurrentProvider = val;
                    else if (key == "model" && CurrentModel == null) CurrentModel = val;
                }
                else if (section.StartsWith("model_providers.") && !section.EndsWith(".auth"))
                {
                    string id = section.Substring("model_providers.".Length);
                    ProviderInfo p = Providers.FirstOrDefault(x => x.Id == id);
                    if (p == null) continue;
                    if (key == "name" && p.Name == null) p.Name = val;
                    else if (key == "base_url") p.BaseUrl = val;
                    else if (key == "wire_api") p.WireApi = val;
                    else if (key == "env_key" && p.EnvKey == null) p.EnvKey = val;
                }
                else if (section.EndsWith(".auth") && pendingAuthSection != null && key == "args")
                {
                    // args = ["-NoProfile", "-Command", "...'VAR_NAME'"]  (windows variant)
                    // or   ["-NoProfile", "-Command", "print(os.environ.get('VAR_NAME',''))"]  (unix variant)
                    int q1 = line.LastIndexOf('\'');
                    int q2 = q1 > 0 ? line.LastIndexOf('\'', q1 - 1) : -1;
                    if (q1 > 0 && q2 > 0)
                    {
                        string varName = line.Substring(q2 + 1, q1 - q2 - 1);
                        ProviderInfo p = Providers.FirstOrDefault(x => x.Id == pendingAuthSection);
                        if (p != null && p.EnvKey == null) p.EnvKey = varName;
                    }
                    pendingAuthSection = null;
                }
            }

            EnsureOpenAiEntry();
        }

        // the built-in openai provider has no [model_providers.*] section; expose it virtually
        public static void EnsureOpenAiEntry()
        {
            if (Providers.FirstOrDefault(x => x.Id == "openai") == null)
                Providers.Add(new ProviderInfo
                {
                    Id = "openai",
                    Name = "OpenAI (built-in account)",
                });
        }

        public static ProviderInfo Find(string id)
        {
            return Providers.FirstOrDefault(x => x.Id == id);
        }

        // ------------------------------------------------------------ switching
        public static void Switch(string providerId, string model, bool removeModel)
        {
            string[] lines = File.ReadAllLines(ConfigPath);
            File.Copy(ConfigPath, BackupPath, true);
            bool providerSet = false, needModel = !removeModel;
            var outp = new List<string>();
            foreach (string raw in lines)
            {
                string line = raw;
                string t = line.Trim();
                int eq = t.IndexOf('=');
                if (eq <= 0 || t.StartsWith("[")) { outp.Add(line); continue; }
                string key = t.Substring(0, eq).Trim();
                if (key == "model_provider")
                {
                    if (!providerSet) { outp.Add("model_provider = \"" + providerId + "\""); providerSet = true; }
                    else outp.Add(line);
                    continue;
                }
                if (key == "model" && !t.StartsWith("model_"))
                {
                    if (removeModel) continue;
                    if (needModel) { outp.Add("model = \"" + model + "\""); needModel = false; continue; }
                    outp.Add(line);
                    continue;
                }
                outp.Add(line);
            }
            if (!providerSet) outp.Insert(0, "model_provider = \"" + providerId + "\"");
            if (needModel)
            {
                int idx = outp.FindIndex(l => l.TrimStart().StartsWith("model_provider"));
                outp.Insert(idx >= 0 ? idx + 1 : 0, "model = \"" + model + "\"");
            }
            File.WriteAllLines(ConfigPath, outp, new UTF8Encoding(false));
            CurrentProvider = providerId;
            CurrentModel = removeModel ? null : model;
            SaveDefaultModel(providerId, removeModel ? null : model);
        }

        // ------------------------------------------------------------ add / update / remove
        public static void AddProvider(string id, string name, string baseUrl, string envKey, string wireApi)
        {
            string[] lines = File.ReadAllLines(ConfigPath);
            File.Copy(ConfigPath, BackupPath, true);
            // managed block: replace any previous block for this id (idempotent),
            // following the bifrost-model-router marker pattern.
            var outp = new List<string>();
            string beginMarker = "# BEGIN codex-provider-manager: " + id;
            string endMarker = "# END codex-provider-manager: " + id;
            bool skipping = false;
            bool inserted = false;
            foreach (string raw in lines)
            {
                string t = raw.Trim();
                if (t == beginMarker) { skipping = true; continue; }
                if (t == endMarker) { skipping = false; inserted = true; continue; }
                if (skipping) continue;
                outp.Add(raw);
            }
            var block = RenderManagedBlock(id, name, baseUrl, envKey, wireApi);
            int insertAt = outp.FindIndex(l => l.Trim() == "[desktop]");
            if (insertAt < 0) outp.AddRange(block);
            else outp.InsertRange(insertAt, block);
            File.WriteAllLines(ConfigPath, outp, new UTF8Encoding(false));
        }

        static List<string> RenderManagedBlock(string id, string name, string baseUrl, string envKey, string wireApi)
        {
            var block = new List<string>
            {
                "# BEGIN codex-provider-manager: " + id,
                "[model_providers." + id + "]",
                "name = \"" + name + "\"",
                "base_url = \"" + baseUrl + "\"",
                "wire_api = \"" + (string.IsNullOrEmpty(wireApi) ? "responses" : wireApi) + "\""
            };
            if (!string.IsNullOrEmpty(envKey)) AppendAuthBlock(block, id, envKey);
            block.Add("# END codex-provider-manager: " + id);
            return block;
        }

        // [auth] command reads the key at request time from a per-OS store:
        // immune to stale process environments.
        static void AppendAuthBlock(List<string> block, string id, string envKey)
        {
            string cmd = Platform.AuthCommand(envKey, out string argsJson);
            block.Add("");
            block.Add("[model_providers." + id + ".auth]");
            block.Add("command = \"" + cmd + "\"");
            block.Add("args = " + argsJson);
        }

        public static void UpdateProvider(string id, string name, string baseUrl, string envKey, string wireApi)
        {
            string[] lines = File.ReadAllLines(ConfigPath);
            File.Copy(ConfigPath, BackupPath, true);

            // managed block present? then update = remove block + re-render (idempotent, no parsing)
            string beginMarker = "# BEGIN codex-provider-manager: " + id;
            bool hasManagedBlock = lines.Any(l => l.Trim() == beginMarker);
            if (hasManagedBlock)
            {
                // keep the active-provider/model lines untouched; just re-render the block
                var cleaned = new List<string>();
                string endMarker = "# END codex-provider-manager: " + id;
                bool skipping = false;
                foreach (string raw in lines)
                {
                    string t = raw.Trim();
                    if (t == beginMarker) { skipping = true; continue; }
                    if (t == endMarker) { skipping = false; continue; }
                    if (skipping) continue;
                    cleaned.Add(raw);
                }
                var block = RenderManagedBlock(id, name, baseUrl, envKey, wireApi);
                int insertAt = cleaned.FindIndex(l => l.Trim() == "[desktop]");
                if (insertAt < 0) cleaned.AddRange(block);
                else cleaned.InsertRange(insertAt, block);
                File.WriteAllLines(ConfigPath, cleaned, new UTF8Encoding(false));
                return;
            }

            // legacy path: in-place line surgery (pre-managed-block providers)
            var outp = new List<string>();
            bool inSection = false, inAuthSub = false, wroteAuth = false;
            string marker = "[model_providers." + id + "]";
            string authHeader = "[model_providers." + id + ".auth]";
            List<string> authBlock = null;
            if (!string.IsNullOrEmpty(envKey))
            {
                authBlock = new List<string> { "", authHeader };
                AppendAuthBlock(authBlock, id, envKey);
                authBlock.RemoveAt(0); // the blank line goes before the header only when flushing mid-file
                authBlock.Insert(0, "");
            }
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("["))
                {
                    bool isAuthSub = t == authHeader;
                    if (inSection && !isAuthSub)
                    {
                        if (!wroteAuth && authBlock != null) { outp.AddRange(authBlock); wroteAuth = true; }
                        inSection = false;
                    }
                    inAuthSub = false;
                    if (t == marker) { inSection = true; outp.Add(line); continue; }
                    outp.Add(line);
                    continue;
                }
                if (inAuthSub)
                {
                    // regenerate the auth sub-table from scratch
                    if (t.StartsWith("command")) { if (authBlock != null) outp.Add(authBlock[2]); }
                    else if (t.StartsWith("args")) { if (authBlock != null) outp.Add(authBlock[3]); wroteAuth = true; }
                    else outp.Add(line);
                    continue;
                }
                if (inSection)
                {
                    int eq = t.IndexOf('=');
                    if (eq <= 0) { outp.Add(line); continue; }
                    string k = t.Substring(0, eq).Trim();
                    if (k == "name") outp.Add("name = \"" + name + "\"");
                    else if (k == "base_url") outp.Add("base_url = \"" + baseUrl + "\"");
                    else if (k == "wire_api") outp.Add("wire_api = \"" + (string.IsNullOrEmpty(wireApi) ? "responses" : wireApi) + "\"");
                    else if (k == "env_key")
                    {
                        // migrated to [auth]: drop the line; the block is flushed when leaving the section
                        if (!wroteAuth && authBlock != null) { /* flushed on section exit */ }
                    }
                    else outp.Add(line);
                }
                else outp.Add(line);
            }
            if (!wroteAuth && authBlock != null)
            {
                int idx = outp.FindIndex(l => l.Trim() == marker);
                if (idx >= 0) outp.InsertRange(idx + 1, authBlock);
            }
            File.WriteAllLines(ConfigPath, outp, new UTF8Encoding(false));
        }

        public static void RemoveProvider(string id)
        {
            string[] lines = File.ReadAllLines(ConfigPath);
            File.Copy(ConfigPath, BackupPath, true);
            var outp = new List<string>();
            bool skipping = false;
            string beginMarker = "# BEGIN codex-provider-manager: " + id;
            string endMarker = "# END codex-provider-manager: " + id;
            string marker = "[model_providers." + id + "]";
            foreach (string line in lines)
            {
                string t = line.Trim();
                // managed block: drop everything between the markers
                if (t == beginMarker) { skipping = true; continue; }
                if (t == endMarker) { skipping = false; continue; }
                if (skipping) continue;
                if (t.StartsWith("["))
                {
                    // legacy (unmarked) section: skip header + sub-tables
                    if (t == marker) { skipping = true; continue; }
                    if (skipping && !t.StartsWith(marker.TrimEnd(']') + ".")) skipping = false;
                }
                if (!skipping) outp.Add(line);
            }
            for (int i = 0; i < outp.Count; i++)
            {
                string t = outp[i].Trim();
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                string k = t.Substring(0, eq).Trim();
                if (k == "model_provider" && t.Contains("\"" + id + "\""))
                {
                    outp[i] = "model_provider = \"openai\"";
                    for (int j = i + 1; j < outp.Count; j++)
                    {
                        string t2 = outp[j].Trim();
                        if (t2.StartsWith("[")) break;
                        int eq2 = t2.IndexOf('=');
                        if (eq2 > 0 && t2.Substring(0, eq2).Trim() == "model") { outp.RemoveAt(j); break; }
                    }
                }
            }
            File.WriteAllLines(ConfigPath, outp, new UTF8Encoding(false));
            DefaultModels.Remove(id);
            SaveDefaultModel(null, null);
        }

        // ------------------------------------------------------------ api key handling
        public static bool SetApiKey(string envName, string keyValue)
        {
            string val = (keyValue ?? "").Trim();
            if (val.Length == 0 || string.IsNullOrEmpty(envName)) return false;
            Platform.SetUserEnvVar(envName, val);
            return true;
        }

        public static string KeyFor(ProviderInfo p)
        {
            if (p == null || string.IsNullOrEmpty(p.EnvKey)) return null;
            return Platform.GetUserEnvVar(p.EnvKey);
        }

        // ------------------------------------------------------------ models map (shared with codex-provider.ps1)
        public static void LoadModelMap()
        {
            DefaultModels.Clear();
            if (!File.Exists(ModelsMapPath)) return;
            try
            {
                var map = MiniJson.Parse(File.ReadAllText(ModelsMapPath));
                foreach (var kv in map) DefaultModels[kv.Key] = kv.Value;
            }
            catch { /* corrupted map: start fresh */ }
        }

        public static void SaveDefaultModel(string id, string model)
        {
            if (id != null)
            {
                if (model == null) DefaultModels.Remove(id);
                else DefaultModels[id] = model;
            }
            var sb = new StringBuilder("{\n");
            int i = 0;
            foreach (var kv in DefaultModels)
            {
                sb.Append("  \"").Append(kv.Key).Append("\": \"").Append(kv.Value ?? "").Append("\"");
                if (++i < DefaultModels.Count) sb.Append(",");
                sb.Append("\n");
            }
            sb.Append("}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(ModelsMapPath));
            File.WriteAllText(ModelsMapPath, sb.ToString(), new UTF8Encoding(false));
        }

        // ------------------------------------------------------------ settings
        public static void LoadSettings()
        {
            RestartAfterSwitch = true;
            if (!File.Exists(SettingsPath)) return;
            try
            {
                var s = MiniJson.Parse(File.ReadAllText(SettingsPath));
                if (s.TryGetValue("restartAfterSwitch", out string v)) RestartAfterSwitch = v == "true";
            }
            catch { }
        }

        public static void SaveSettings()
        {
            File.WriteAllText(SettingsPath, "{\n  \"restartAfterSwitch\": " + (RestartAfterSwitch ? "true" : "false") + "\n}\n", new UTF8Encoding(false));
        }
    }

    /// <summary>Platform-specific operations, implemented once per OS.</summary>
    public interface IPlatformHooks
    {
        /// <summary>First line of the [auth] command block (e.g. command = "powershell").</summary>
        string AuthCommand(string envVar, out string argsJson);
        /// <summary>Reads a user-level environment variable (registry / keyring / file).</summary>
        string GetUserEnvVar(string name);
        /// <summary>Persists a user-level environment variable.</summary>
        void SetUserEnvVar(string name, string value);
        /// <summary>Restarts the Codex desktop app if running. Returns a status message.</summary>
        string RestartDesktopApp();
        /// <summary>Fetches the model list for a provider (GET base_url/models with Bearer key).</summary>
        List<string> FetchModels(ProviderInfo p);
    }

    /// <summary>Minimal JSON reader/writer for the flat maps this app uses (no dependencies).</summary>
    public static class MiniJson
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return map;
            var keys = new List<string>();
            var vals = new List<string>();
            bool inStr = false, isKey = false; var cur = new StringBuilder(); int esc = 0;
            foreach (char c in json)
            {
                if (esc == 1) { if (c == 'n') cur.Append('\n'); else cur.Append(c); esc = 0; continue; }
                if (c == '\\') { esc = 1; continue; }
                if (c == '"')
                {
                    if (!inStr) { inStr = true; cur.Clear(); }
                    else
                    {
                        inStr = false;
                        if (!isKey) { keys.Add(cur.ToString()); isKey = true; }
                        else { vals.Add(cur.ToString()); isKey = false; }
                    }
                    continue;
                }
                if (inStr) cur.Append(c);
            }
            for (int i = 0; i < keys.Count; i++)
                if (i < vals.Count) map[keys[i]] = vals[i];
                else if (!map.ContainsKey(keys[i])) map[keys[i]] = null;
            return map;
        }

        public static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}