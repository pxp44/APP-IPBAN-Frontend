using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IPBanFrontend
{
    /// <summary>
    /// IPThreat: share failed logins (IPThreatApiKey) and receive community lists (FirewallUriRules).
    /// Lists: https://lists.ipthreat.net/file/ipthreat-lists/threat/threat-{level}.txt.gz
    /// </summary>
    static class IpThreat
    {
        public const string ApiKeyEnvName = "IPThreat_API_KEY";
        public const string ConfigKey = "IPThreatApiKey";
        public const string RulePrefix = "IPThreat";
        public const int DefaultLevel = 90;
        public const string DefaultInterval = "00:01:00:00"; // hourly; lists update about once per hour
        public const int DefaultMaxCount = 20000;
        public const string ListsPage = "https://ipthreat.net/lists";
        public const string DocsPage = "https://ipthreat.net/integrations/ipban";
        public const string PermissionsPage = "https://ipthreat.net/requestpermissions";

        public static readonly int[] Levels = { 100, 90, 80, 70, 50 };

        public static string ListUrl(int level)
        {
            return "https://lists.ipthreat.net/file/ipthreat-lists/threat/threat-" + ClampLevel(level) + ".txt.gz";
        }

        public static string FormatRule(int level)
        {
            return RulePrefix + "," + DefaultInterval + "," + ListUrl(level) + "," + DefaultMaxCount;
        }

        public static int ClampLevel(int level)
        {
            if (Array.IndexOf(Levels, level) >= 0) return level;
            return DefaultLevel;
        }

        public static string MaskKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "(geen)";
            key = key.Trim();
            if (key.Length <= 8) return "••••";
            return "••••" + key.Substring(key.Length - 4);
        }

        /// <summary>
        /// Trim, unquote, drop whitespace, and strip a trailing numeric id after "=="
        /// (ipthreat.net UI shows key and id side by side; paste often becomes "...==-1234").
        /// </summary>
        public static string NormalizeApiKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            key = key.Trim().Trim('\uFEFF');
            if (key.Length >= 2)
            {
                var q = key[0];
                if ((q == '"' || q == '\'') && key[key.Length - 1] == q)
                    key = key.Substring(1, key.Length - 2).Trim();
            }
            key = Regex.Replace(key, @"\s+", "");
            var m = Regex.Match(key, @"^(?<k>[A-Za-z0-9+/=]+==)-\d+$");
            if (m.Success)
                key = m.Groups["k"].Value;
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }

        public static string ResolveApiKey(string configPath)
        {
            var fromEnv = NormalizeApiKey(EnvFile.Get(ApiKeyEnvName));
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

            try
            {
                if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                {
                    var doc = IpBanConfig.Load(configPath);
                    var fromCfg = NormalizeApiKey(IpBanConfig.ReadValue(doc, ConfigKey));
                    if (!string.IsNullOrWhiteSpace(fromCfg)) return fromCfg;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        /// <summary>Friendly text for the last IPThreat upload failure in IPBan's logfile (never includes the key).</summary>
        public static string LastShareError(string logPath)
        {
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return null;
            string tail;
            try
            {
                var fi = new FileInfo(logPath);
                if (fi.Length == 0) return null;
                var take = (int)Math.Min(fi.Length, 96 * 1024);
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fi.Length > take)
                        fs.Seek(-take, SeekOrigin.End);
                    using (var sr = new StreamReader(fs))
                        tail = sr.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }

            var needle = "Failed to post json to ipthreat api";
            var idx = tail.LastIndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var okIdx = tail.LastIndexOf("failed logins to ipthreat api", StringComparison.OrdinalIgnoreCase);
            if (okIdx > idx) return null; // later successful submit
            var snippet = tail.Substring(idx, Math.Min(700, tail.Length - idx));
            if (snippet.IndexOf("403", StringComparison.Ordinal) >= 0
                || snippet.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) >= 0
                || snippet.IndexOf("Access denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "IPThreat weigert delen (403). De API-key ís gezet; vraag bulk-report rechten aan: "
                    + PermissionsPage;
            }
            return "IPThreat-upload mislukt. Controleer of IPThreatApiKey de kale key is (eindigt op ==).";
        }

        public static bool HasShare(XDocument doc)
        {
            var v = IpBanConfig.ReadValue(doc, ConfigKey);
            return !string.IsNullOrWhiteSpace(v);
        }

        public static bool HasReceive(string uriRules)
        {
            return TryParseLevel(uriRules) != null;
        }

        public static int? TryParseLevel(string uriRules)
        {
            foreach (var line in SplitLines(uriRules))
            {
                if (!line.StartsWith(RulePrefix + ",", StringComparison.OrdinalIgnoreCase))
                    continue;
                var m = Regex.Match(line, @"threat-(?<n>\d+)\.txt", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    int n;
                    if (int.TryParse(m.Groups["n"].Value, out n))
                        return ClampLevel(n);
                }
                return DefaultLevel;
            }
            return null;
        }

        public static string UpsertReceive(string uriRules, bool enable, int level)
        {
            var kept = new List<string>();
            foreach (var line in SplitLines(uriRules))
            {
                if (line.StartsWith(RulePrefix + ",", StringComparison.OrdinalIgnoreCase))
                    continue;
                kept.Add(line);
            }
            if (enable)
                kept.Insert(0, FormatRule(level));
            return string.Join("\n", kept);
        }

        public static ApplyResult Apply(string configPath, bool share, bool receive, int level)
        {
            var result = new ApplyResult { Level = ClampLevel(level) };
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                result.Message = "ipban.config niet gevonden.";
                return result;
            }

            var key = ResolveApiKey(configPath);
            if (share && string.IsNullOrWhiteSpace(key))
            {
                result.Message = "Geen IPThreat API-key. Zet IPThreat_API_KEY in .env of vul IPThreatApiKey in ipban.config.";
                return result;
            }

            try
            {
                if (share)
                    EnvFile.Upsert(ApiKeyEnvName, key);

                var doc = IpBanConfig.Load(configPath);
                IpBanConfig.WriteValue(doc, ConfigKey, share ? key : "");

                var uri = IpBanConfig.ReadValue(doc, "FirewallUriRules") ?? "";
                uri = UpsertReceive(uri, receive, result.Level);
                IpBanConfig.WriteValue(doc, "FirewallUriRules", uri);

                result.BackupPath = IpBanConfig.BackupAndSave(configPath, doc);
                result.ShareEnabled = share;
                result.ReceiveEnabled = receive;
                result.Ok = true;
                result.Message = Describe(share, receive, result.Level);
            }
            catch (Exception ex)
            {
                result.Message = "IPThreat opslaan mislukt: " + ex.Message;
            }
            return result;
        }

        public static string Describe(bool share, bool receive, int level)
        {
            if (!share && !receive) return "IPThreat uit.";
            var parts = new List<string>();
            if (share) parts.Add("delen (failed logins → IPThreat)");
            if (receive) parts.Add("ontvangen (community-lijst ≥ " + level + "%)");
            return "IPThreat: " + string.Join(" + ", parts);
        }

        static IEnumerable<string> SplitLines(string raw)
        {
            return (raw ?? "")
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0);
        }

        public sealed class ApplyResult
        {
            public bool Ok;
            public string Message;
            public bool ShareEnabled;
            public bool ReceiveEnabled;
            public int Level;
            public string BackupPath;
        }
    }
}
