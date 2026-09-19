using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace IPBanFrontend
{
    /// <summary>
    /// Maps IPs from IPBan FirewallUriRules (community blocklists) to list names.
    /// IPBan creates Windows firewall rules like IPBan_EmergingThreats_0 — we index those.
    /// </summary>
    static class CommunityLists
    {
        /// <summary>Well-known UriRule prefixes (same names we put in FirewallUriRules).</summary>
        public static readonly string[] KnownPrefixes =
        {
            "EmergingThreats",
            "CINSArmy",
            "GreenSnow",
            "BlocklistDe",
            "FireholLevel1",
            "SpamhausDROP",
            "IPThreat"
        };

        /// <summary>Suggested free lists — 8h sync, matching IPBan FirewallUriRules format.</summary>
        public const string DefaultUriRules =
            "EmergingThreats,00:08:00:00,https://rules.emergingthreats.net/fwrules/emerging-Block-IPs.txt,10000\n" +
            "CINSArmy,00:08:00:00,https://cinsscore.com/list/ci-badguys.txt,10000\n" +
            "GreenSnow,00:08:00:00,https://blocklist.greensnow.co/greensnow.txt,5000\n" +
            "BlocklistDe,00:08:00:00,https://lists.blocklist.de/lists/all.txt,10000\n" +
            "SpamhausDROP,00:08:00:00,https://www.spamhaus.org/drop/drop.txt,5000";

        static readonly object Gate = new object();
        static Dictionary<string, string> _ipToList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static List<CidrEntry> _cidrs = new List<CidrEntry>();
        static DateTime _loadedUtc = DateTime.MinValue;
        static string _lastError;

        public static string LastError => _lastError;
        public static int IndexedIps
        {
            get { lock (Gate) return _ipToList.Count; }
        }
        public static int IndexedCidrs
        {
            get { lock (Gate) return _cidrs.Count; }
        }

        public static void RefreshIfStale(string firewallPrefix, TimeSpan maxAge)
        {
            lock (Gate)
            {
                if (_loadedUtc != DateTime.MinValue && DateTime.UtcNow - _loadedUtc < maxAge)
                    return;
            }
            Refresh(firewallPrefix);
        }

        public static void Refresh(string firewallPrefix)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cidrs = new List<CidrEntry>();
            string err = null;
            try
            {
                if (string.IsNullOrWhiteSpace(firewallPrefix)) firewallPrefix = "IPBan_";
                var rules = FirewallHelper.GetIpBanRules(firewallPrefix);
                foreach (var rule in rules)
                {
                    if (rule == null || !rule.IsBlock || rule.Enabled == false) continue;
                    var listName = MatchPrefix(rule.Name, firewallPrefix);
                    if (listName == null) continue;
                    foreach (var token in SplitRemoteTokens(rule.RemoteAddresses))
                    {
                        if (token.IndexOf('/') >= 0)
                        {
                            CidrEntry c;
                            if (TryParseCidr(token, out c))
                            {
                                c.ListName = listName;
                                cidrs.Add(c);
                            }
                            continue;
                        }
                        IPAddress ip;
                        if (IPAddress.TryParse(token, out ip) && !map.ContainsKey(token))
                            map[token] = listName;
                    }
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }

            lock (Gate)
            {
                _ipToList = map;
                _cidrs = cidrs;
                _loadedUtc = DateTime.UtcNow;
                _lastError = err;
            }
        }

        public static string Lookup(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return null;
            ip = ip.Trim();
            lock (Gate)
            {
                string name;
                if (_ipToList.TryGetValue(ip, out name)) return name;
                IPAddress addr;
                if (!IPAddress.TryParse(ip, out addr)) return null;
                if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return null;
                var bytes = addr.GetAddressBytes();
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                uint val = BitConverter.ToUInt32(bytes, 0);
                foreach (var c in _cidrs)
                {
                    if ((val & c.Mask) == c.Network) return c.ListName;
                }
            }
            return null;
        }

        public static void Annotate(MonitorEvent ev)
        {
            if (ev == null) return;
            if (!string.IsNullOrEmpty(ev.CommunityList)) return;

            // Sync lines from IPBan itself
            if (!string.IsNullOrEmpty(ev.Raw))
            {
                var m = Regex.Match(ev.Raw,
                    @"firewall uri rule\s+(?<name>[A-Za-z0-9_\-]+)",
                    RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    ev.CommunityList = m.Groups["name"].Value;
                    return;
                }
            }

            if (!string.IsNullOrEmpty(ev.Ip))
                ev.CommunityList = Lookup(ev.Ip);
        }

        /// <summary>Append community tag to a raw logfile line when the IP is on a UriRule list.</summary>
        public static string AnnotateLogLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            if (line.IndexOf("[community:", StringComparison.OrdinalIgnoreCase) >= 0)
                return line;

            // Prefer labeled / failure / ban IPs
            string ip = null;
            var m = Regex.Match(line, @"Login failure:\s*(?<ip>\d+\.\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
            if (m.Success) ip = m.Groups["ip"].Value;
            if (ip == null)
            {
                m = Regex.Match(line, @"Banning ip address:\s*(?<ip>\d+\.\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
                if (m.Success) ip = m.Groups["ip"].Value;
            }
            if (ip == null)
            {
                m = Regex.Match(line, @"ip address\s+(?<ip>\d+\.\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
                if (m.Success) ip = m.Groups["ip"].Value;
            }
            if (ip == null) return line;

            var list = Lookup(ip);
            if (string.IsNullOrEmpty(list)) return line;
            return line.TrimEnd() + "  [community: " + list + "]";
        }

        static string MatchPrefix(string ruleName, string firewallPrefix)
        {
            if (string.IsNullOrEmpty(ruleName)) return null;
            var name = ruleName;
            if (name.StartsWith(firewallPrefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(firewallPrefix.Length);
            // EXTRA_ is for FirewallRules, not UriRules — skip allow extras
            if (name.StartsWith("EXTRA_", StringComparison.OrdinalIgnoreCase)) return null;
            if (name.StartsWith("Block_", StringComparison.OrdinalIgnoreCase)) return null;
            if (name.StartsWith("Global", StringComparison.OrdinalIgnoreCase)) return null;

            foreach (var p in KnownPrefixes)
            {
                if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            // Unknown uri-style rule: Name_0 / Name_1000
            var us = name.LastIndexOf('_');
            if (us > 0)
            {
                int n;
                if (int.TryParse(name.Substring(us + 1), out n))
                    return name.Substring(0, us);
            }
            return null;
        }

        static IEnumerable<string> SplitRemoteTokens(string remote)
        {
            if (string.IsNullOrWhiteSpace(remote)) yield break;
            if (remote.Equals("Any", StringComparison.OrdinalIgnoreCase)) yield break;
            foreach (var raw in remote.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                yield return t;
            }
        }

        static bool TryParseCidr(string token, out CidrEntry entry)
        {
            entry = null;
            var slash = token.IndexOf('/');
            if (slash <= 0) return false;
            IPAddress net;
            int prefix;
            if (!IPAddress.TryParse(token.Substring(0, slash), out net)) return false;
            if (!int.TryParse(token.Substring(slash + 1), out prefix)) return false;
            if (net.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
            if (prefix < 0 || prefix > 32) return false;
            var bytes = net.GetAddressBytes();
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            uint val = BitConverter.ToUInt32(bytes, 0);
            uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
            entry = new CidrEntry { Network = val & mask, Mask = mask };
            return true;
        }

        sealed class CidrEntry
        {
            public uint Network;
            public uint Mask;
            public string ListName;
        }
    }
}
