using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace IPBanFrontend
{
    /// <summary>Read/write helpers for free IPBan ipban.config appSettings.</summary>
    static class IpBanConfig
    {
        public static readonly string[] EditableKeys =
        {
            "FailedLoginAttemptsBeforeBan",
            "BanTime",
            "ExpireTime",
            "CycleTime",
            "MinimumTimeBetweenFailedLoginAttempts",
            "MinimumTimeBetweenSuccessfulLoginAttempts",
            "ClearBannedIPAddressesOnRestart",
            "ClearFailedLoginsOnSuccessfulLogin",
            "ResetFailedLoginCountForUnbannedIPAddresses",
            "ProcessInternalIPAddresses",
            "FirewallRulePrefix",
            "WhitelistRegex",
            "BlacklistRegex",
            "UserNameWhitelist",
            "UserNameWhitelistRegex",
            "UserNameWhitelistMinimumEditDistance",
            "FailedLoginAttemptsBeforeBanUserNameWhitelist",
            "UseDefaultBannedIPAddressHandler",
            "ProcessToRunOnBan",
            "ProcessToRunOnUnban",
            "ProcessToRunOnSuccessfulLogin",
            "FirewallUriRules",
            "FirewallRules",
            "ExternalIPAddressUrl"
        };

        public static readonly Dictionary<string, string> KeyHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FailedLoginAttemptsBeforeBan"] = "Aantal mislukte logins vóór ban (bijv. 5)",
            ["BanTime"] = "Banduur DD:HH:MM:SS — 00:00:00:00 = max (~9999 dagen). Meerdere waarden komma-gescheiden voor oplopende bans.",
            ["ExpireTime"] = "Vergeet mislukte logins na deze tijd (DD:HH:MM:SS)",
            ["CycleTime"] = "Hoe vaak IPBan housekeeping doet (DD:HH:MM:SS), standaard 15s",
            ["MinimumTimeBetweenFailedLoginAttempts"] = "Minimale tijd tussen tellende mislukte pogingen",
            ["MinimumTimeBetweenSuccessfulLoginAttempts"] = "Minimale tijd tussen succesvolle login-events",
            ["ClearBannedIPAddressesOnRestart"] = "true/false — wis alle bans bij service-start",
            ["ClearFailedLoginsOnSuccessfulLogin"] = "true/false — wis failed-count na succesvolle login",
            ["ResetFailedLoginCountForUnbannedIPAddresses"] = "true/false — bij meerdere BanTime-trappen",
            ["ProcessInternalIPAddresses"] = "true/false — ook private IP’s (10.x, 192.168.x) verwerken",
            ["FirewallRulePrefix"] = "Prefix voor Windows Firewall-regels (bijv. IPBan_)",
            ["WhitelistRegex"] = "Regex: match voorkomt ban (niet in firewall)",
            ["BlacklistRegex"] = "Regex: match zorgt voor ban",
            ["UserNameWhitelist"] = "Toegestane gebruikersnamen (komma). Leeg = uit.",
            ["UserNameWhitelistRegex"] = "Regex voor toegestane gebruikersnamen",
            ["UserNameWhitelistMinimumEditDistance"] = "Levenshtein-afstand: groter = sneller bannen bij foute namen",
            ["FailedLoginAttemptsBeforeBanUserNameWhitelist"] = "Drempel voor whitelisted usernames",
            ["UseDefaultBannedIPAddressHandler"] = "true/false — anonieme ban-sharing met IPBan-cloud",
            ["ProcessToRunOnBan"] = "pad|args — ###IPADDRESS### wordt vervangen",
            ["ProcessToRunOnUnban"] = "pad|args bij unban",
            ["ProcessToRunOnSuccessfulLogin"] = "pad|args bij succesvolle login",
            ["FirewallUriRules"] = "Één per regel: Prefix,DD:HH:MM:SS,https://…",
            ["FirewallRules"] = "Één per regel: naam;allow|block;ips;poorten;platform-regex",
            ["ExternalIPAddressUrl"] = "URL die het publieke IP teruggeeft (plain text)"
        };

        public static XDocument Load(string configPath)
        {
            return XDocument.Load(configPath);
        }

        public static IEnumerable<string> ReadList(XDocument doc, string key)
        {
            var raw = ReadValue(doc, key) ?? "";
            return SplitList(raw);
        }

        public static IEnumerable<string> SplitList(string raw)
        {
            return (raw ?? "")
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        }

        public static string ReadValue(XDocument doc, string key)
        {
            var node = doc.Descendants("add")
                .FirstOrDefault(x => (string)x.Attribute("key") == key);
            return node == null ? null : (string)node.Attribute("value");
        }

        public static void WriteValue(XDocument doc, string key, string value)
        {
            var node = doc.Descendants("add").FirstOrDefault(x => (string)x.Attribute("key") == key);
            if (node == null)
            {
                var app = doc.Descendants("appSettings").FirstOrDefault()
                    ?? throw new InvalidOperationException("Geen <appSettings> in ipban.config");
                app.Add(new XElement("add",
                    new XAttribute("key", key),
                    new XAttribute("value", value ?? "")));
            }
            else
            {
                node.SetAttributeValue("value", value ?? "");
            }
        }

        public static void WriteList(XDocument doc, string key, IEnumerable<string> values)
        {
            WriteValue(doc, key, string.Join(",", values ?? Enumerable.Empty<string>()));
        }

        public static string BackupAndSave(string configPath, XDocument doc)
        {
            var backup = configPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(configPath, backup, true);

            var tmp = configPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                doc.Save(tmp);
                // Atomic replace where possible
                if (File.Exists(configPath))
                {
                    try
                    {
                        File.Replace(tmp, configPath, null);
                    }
                    catch
                    {
                        File.Copy(tmp, configPath, true);
                        try { File.Delete(tmp); } catch { }
                    }
                }
                else
                {
                    File.Move(tmp, configPath);
                }
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }

            return backup;
        }
    }
}
