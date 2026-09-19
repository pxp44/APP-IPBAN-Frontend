using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IPBanFrontend
{
    enum MonitorKind
    {
        Failed,
        Ban,
        Unban,
        Success,
        Warn,
        Error,
        Info
    }

    sealed class MonitorEvent
    {
        public DateTime? TimeLocal { get; set; }
        public MonitorKind Kind { get; set; }
        public string Ip { get; set; }
        public string User { get; set; }
        public string Source { get; set; }
        public int? Count { get; set; }
        public string Raw { get; set; }
        public string Title { get; set; }
        public string Hint { get; set; }
        /// <summary>FirewallUriRules / community blocklist name when known (e.g. EmergingThreats).</summary>
        public string CommunityList { get; set; }

        public Color RowColor
        {
            get
            {
                switch (Kind)
                {
                    case MonitorKind.Ban: return Color.FromArgb(254, 226, 226);      // rood
                    case MonitorKind.Failed: return Color.FromArgb(255, 237, 213);   // oranje
                    case MonitorKind.Success: return Color.FromArgb(220, 252, 231);  // groen
                    case MonitorKind.Unban: return Color.FromArgb(224, 242, 254);    // blauw
                    case MonitorKind.Error: return Color.FromArgb(255, 228, 230);    // roze
                    case MonitorKind.Warn: return Color.FromArgb(254, 249, 195);     // geel
                    default: return Color.FromArgb(248, 250, 252);
                }
            }
        }

        public Color AccentColor
        {
            get
            {
                switch (Kind)
                {
                    case MonitorKind.Ban: return Color.FromArgb(185, 28, 28);
                    case MonitorKind.Failed: return Color.FromArgb(194, 65, 12);
                    case MonitorKind.Success: return Color.FromArgb(21, 128, 61);
                    case MonitorKind.Unban: return Color.FromArgb(3, 105, 161);
                    case MonitorKind.Error: return Color.FromArgb(190, 18, 60);
                    case MonitorKind.Warn: return Color.FromArgb(161, 98, 7);
                    default: return Color.FromArgb(71, 85, 105);
                }
            }
        }

        public string KindLabel
        {
            get
            {
                switch (Kind)
                {
                    case MonitorKind.Ban: return "BAN";
                    case MonitorKind.Failed: return "MISLUKT";
                    case MonitorKind.Success: return "OK LOGIN";
                    case MonitorKind.Unban: return "UNBAN";
                    case MonitorKind.Error: return "FOUT";
                    case MonitorKind.Warn: return "WARN";
                    default: return "INFO";
                }
            }
        }
    }

    sealed class MonitorPeriodStats
    {
        public TimeSpan Period { get; set; }
        public int AttemptCount { get; set; }
        public int UniqueIps { get; set; }
        public int BanCount { get; set; }
        public int SuccessCount { get; set; }
        public List<KeyValuePair<string, int>> TopIps { get; set; } = new List<KeyValuePair<string, int>>();
        public List<MonitorEvent> Events { get; set; } = new List<MonitorEvent>();
    }

    static class LogMonitor
    {
        // Flexible parsers for common IPBan logfile lines
        static readonly Regex RxTs = new Regex(
            @"^(?<ts>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?)",
            RegexOptions.Compiled);

        // Alleen echte IP-labels — NOOIT los "ip" (matcht anders "IPBan" → "Ba")
        static readonly Regex RxIpLabeled = new Regex(
            @"(?:ip\s*address|ipaddress)\s*[:=]\s*(?<ip>[^\s,;\|]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxIpAfterFrom = new Regex(
            @"\bfrom\s+(?<ip>(?:\d{1,3}\.){3}\d{1,3}|[0-9a-fA-F]*:[0-9a-fA-F:]+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxIpV4 = new Regex(
            @"\b(?<ip>(?:\d{1,3}\.){3}\d{1,3})\b",
            RegexOptions.Compiled);

        static readonly Regex RxIpV6 = new Regex(
            @"\b(?<ip>(?:[0-9a-fA-F]{0,4}:){2,7}[0-9a-fA-F]{0,4})\b",
            RegexOptions.Compiled);

        static readonly Regex RxUser = new Regex(
            @"(?:user\s*name|username|account\s*name|account)\s*[:=]\s*(?<user>[^,\|\r\n]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSource = new Regex(
            @"(?:source|protocol)\s*[:=]\s*(?<src>[^,\|\r\n]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCount = new Regex(
            @"(?:count|failed\s*login\s*count|attempts?)\s*[:=]\s*(?<n>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static MonitorPeriodStats Analyze(string logPath, TimeSpan period, int maxEvents = 200)
        {
            var stats = new MonitorPeriodStats { Period = period };
            var cutoff = DateTime.Now - period;
            var events = new List<MonitorEvent>();
            var ipAttempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(logPath)) return stats;

            try
            {
                var info = new FileInfo(logPath);
                // Lees meer bij langere periodes
                var maxBytes = period.TotalHours <= 2 ? 256 * 1024
                    : period.TotalHours <= 24 ? 1024 * 1024
                    : 3 * 1024 * 1024;
                var start = Math.Max(0, info.Length - maxBytes);

                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(start, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        // Skip partial first line
                        if (start > 0) sr.ReadLine();
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var ev = ParseLine(line);
                            if (ev == null) continue;
                            if (ev.TimeLocal.HasValue && ev.TimeLocal.Value < cutoff) continue;
                            // Zonder timestamp: alleen meenemen als we midden in het bestand zaten en regel "vers" lijkt — anders skip
                            if (!ev.TimeLocal.HasValue) continue;

                            events.Add(ev);

                            if ((ev.Kind == MonitorKind.Failed || ev.Kind == MonitorKind.Ban) &&
                                !string.IsNullOrEmpty(ev.Ip))
                            {
                                stats.AttemptCount++;
                                int n;
                                ipAttempts.TryGetValue(ev.Ip, out n);
                                ipAttempts[ev.Ip] = n + 1;
                            }
                            if (ev.Kind == MonitorKind.Ban) stats.BanCount++;
                            if (ev.Kind == MonitorKind.Success) stats.SuccessCount++;
                        }
                    }
                }
            }
            catch
            {
                return stats;
            }

            stats.UniqueIps = ipAttempts.Count;
            stats.TopIps = ipAttempts
                .OrderByDescending(kv => kv.Value)
                .Take(8)
                .ToList();
            stats.Events = events
                .AsEnumerable()
                .Reverse()
                .Take(maxEvents)
                .ToList();
            return stats;
        }

        /// <summary>
        /// Alleen Failed + Ban met timestamp — voor trendgrafieken (geen IP-parse).
        /// </summary>
        public static void ForEachAttempt(string logPath, long maxBytes, Action<DateTime> onAttempt)
        {
            if (onAttempt == null || string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
                return;

            try
            {
                var info = new FileInfo(logPath);
                var start = Math.Max(0, info.Length - Math.Max(64 * 1024, maxBytes));
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(start, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        if (start > 0) sr.ReadLine();
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (!IsInteresting(line)) continue;
                            var kind = Classify(line);
                            if (kind != MonitorKind.Failed && kind != MonitorKind.Ban) continue;
                            var t = ParseTime(line);
                            if (!t.HasValue) continue;
                            onAttempt(t.Value);
                        }
                    }
                }
            }
            catch
            {
                /* logfile vergrendeld of onleesbaar */
            }
        }

        public static MonitorEvent ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            if (!IsInteresting(line)) return null;

            var kind = Classify(line);
            var ev = new MonitorEvent
            {
                Raw = line.Trim(),
                Kind = kind,
                TimeLocal = ParseTime(line)
            };

            var mIp = RxIpLabeled.Match(line);
            if (mIp.Success) ev.Ip = NormalizeIp(mIp.Groups["ip"].Value);
            if (string.IsNullOrEmpty(ev.Ip))
            {
                var from = RxIpAfterFrom.Match(line);
                if (from.Success) ev.Ip = NormalizeIp(from.Groups["ip"].Value);
            }
            if (string.IsNullOrEmpty(ev.Ip))
            {
                // Alleen losse IPv4/IPv6 als het geen logger-naam-fragment is
                foreach (Match m in RxIpV4.Matches(line))
                {
                    var cand = NormalizeIp(m.Groups["ip"].Value);
                    if (!string.IsNullOrEmpty(cand)) { ev.Ip = cand; break; }
                }
            }
            if (string.IsNullOrEmpty(ev.Ip))
            {
                foreach (Match m in RxIpV6.Matches(line))
                {
                    var cand = NormalizeIp(m.Groups["ip"].Value);
                    if (!string.IsNullOrEmpty(cand)) { ev.Ip = cand; break; }
                }
            }

            var mUser = RxUser.Match(line);
            if (mUser.Success)
            {
                ev.User = Clean(mUser.Groups["user"].Value);
                if (IsJunkUser(ev.User)) ev.User = null;
            }

            var mSrc = RxSource.Match(line);
            if (mSrc.Success) ev.Source = Clean(mSrc.Groups["src"].Value);

            // Heuristiek voor bron in tekst
            if (string.IsNullOrEmpty(ev.Source))
                ev.Source = DetectSource(line);

            var mCnt = RxCount.Match(line);
            if (mCnt.Success)
            {
                int n;
                if (int.TryParse(mCnt.Groups["n"].Value, out n)) ev.Count = n;
            }

            CommunityLists.Annotate(ev);
            Enrich(ev);
            return ev;
        }

        /// <summary>Accepteer alleen echte IP-adressen (voorkomt "Ba" uit "IPBan").</summary>
        static string NormalizeIp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim().TrimEnd('.', ',', ';', ')', ']');
            // Duidelijke nep-matches
            if (raw.Length < 7) return null; // min IPv4 a.b.c.d
            if (raw.Equals("IPBan", StringComparison.OrdinalIgnoreCase)) return null;
            if (!raw.Contains(".") && !raw.Contains(":")) return null;

            System.Net.IPAddress addr;
            if (System.Net.IPAddress.TryParse(raw, out addr))
            {
                // Filter rare parse-successen van hex-achtige woorden zonder ':'
                var s = addr.ToString();
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                    raw.IndexOf(':') < 0)
                    return null;
                return s;
            }
            return null;
        }

        static void Enrich(MonitorEvent ev)
        {
            var who = !string.IsNullOrEmpty(ev.User)
                ? "Gebruiker «" + ev.User + "»"
                : "Onbekende / lege gebruikersnaam";
            var where = !string.IsNullOrEmpty(ev.Ip) ? " vanaf " + ev.Ip : "";
            var via = !string.IsNullOrEmpty(ev.Source) ? " via " + FriendlySource(ev.Source) : "";
            var community = !string.IsNullOrEmpty(ev.CommunityList)
                ? " Community-list: «" + ev.CommunityList + "»."
                : "";

            // URI-list sync lines from IPBan
            if (!string.IsNullOrEmpty(ev.Raw) &&
                ev.Raw.IndexOf("firewall uri rule", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ev.Title = "Community-list sync";
                ev.Hint = "IPBan heeft een externe blocklist bijgewerkt" +
                          (!string.IsNullOrEmpty(ev.CommunityList) ? " («" + ev.CommunityList + "»)" : "") +
                          ". IP’s op die lijst worden in de Windows Firewall gezet vóór ze RDP raken." +
                          community;
                return;
            }

            switch (ev.Kind)
            {
                case MonitorKind.Failed:
                    ev.Title = string.IsNullOrEmpty(ev.CommunityList)
                        ? "Mislukte login"
                        : "Mislukte login · community";
                    ev.Hint = who + where + via +
                              " — probeerde in te loggen met deze accountnaam." +
                              (ev.Count.HasValue ? " Teller voor dit IP: " + ev.Count + "." : "") +
                              community +
                              (string.IsNullOrEmpty(ev.CommunityList)
                                  ? " Tip: eigen IP op whitelist; herhaalde aanvallen worden automatisch geband."
                                  : " Dit IP stond al op een gedeelde blocklist (FirewallUriRules).");
                    break;
                case MonitorKind.Ban:
                    ev.Title = string.IsNullOrEmpty(ev.CommunityList)
                        ? "IP geblokkeerd"
                        : "IP geblokkeerd · community";
                    ev.Hint = "IPBan heeft" + where + " in de firewall gezet" + via +
                              ". Dit IP mag (tijdelijk) niet meer verbinden. " + who + "." +
                              community +
                              " Unban via tab Actieve bans of ‘Unban nu’ als dit van jou was.";
                    break;
                case MonitorKind.Unban:
                    ev.Title = "IP gedeblokkeerd";
                    ev.Hint = "Ban opgeheven voor" + where + ". Verbindingen vanaf dit IP zijn weer toegestaan." + community;
                    break;
                case MonitorKind.Success:
                    ev.Title = "Geslaagde login";
                    ev.Hint = who + where + via +
                              " — login gelukt. Herken je dit niet? Direct whitelisten controleren / wachtwoord wijzigen / IP bannen." +
                              community;
                    break;
                case MonitorKind.Error:
                    ev.Title = "Fout";
                    ev.Hint = "IPBan of Windows gaf een fout. Bekijk de ruwe logregel voor details. Service-rechten / firewall-regels controleren.";
                    break;
                case MonitorKind.Warn:
                    ev.Title = "Waarschuwing";
                    ev.Hint = "Let op: iets vraagt aandacht (config, firewall-sync, drempel). Zie de logregel." + community;
                    break;
                default:
                    ev.Title = "Info";
                    ev.Hint = "Informatieve regel van IPBan." + community;
                    break;
            }
        }

        static string FriendlySource(string src)
        {
            if (string.IsNullOrEmpty(src)) return "";
            var s = src.Trim();
            if (s.IndexOf("RDP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Terminal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("RemoteDesktop", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RDP (Extern bureaublad)";
            if (s.IndexOf("SSH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("OpenSSH", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SSH";
            if (s.IndexOf("MSSQL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SQL Server";
            if (s.IndexOf("VNC", StringComparison.OrdinalIgnoreCase) >= 0)
                return "VNC";
            if (s.IndexOf("Exchange", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Exchange / mail";
            if (s.IndexOf("SMB", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SMB / bestandsdeling";
            return s;
        }

        static string DetectSource(string line)
        {
            if (Contains(line, "RDP") || Contains(line, "TerminalServices") || Contains(line, "RemoteDesktop"))
                return "RDP";
            if (Contains(line, "SSH") || Contains(line, "sshd") || Contains(line, "OpenSSH"))
                return "SSH";
            if (Contains(line, "MSSQL") || Contains(line, "SQL Server"))
                return "MSSQL";
            if (Contains(line, "VNC")) return "VNC";
            if (Contains(line, "Exchange")) return "Exchange";
            if (Contains(line, "MySQL") || Contains(line, "MariaDB")) return "MySQL";
            return null;
        }

        static MonitorKind Classify(string line)
        {
            // Let op: NOOIT op substring "ban" in "IPBan" matchen — gebruik woordgrenzen / zinnen
            if (Regex.IsMatch(line, @"\bun-?ban", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, @"\bremoving\s+ban\b", RegexOptions.IgnoreCase) ||
                (Contains(line, "forgot") && Regex.IsMatch(line, @"\bban", RegexOptions.IgnoreCase)))
                return MonitorKind.Unban;
            if (Regex.IsMatch(line, @"\bbanning\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, @"\bbanned\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(line, @"\bban(?:ned)?\s+ip\b", RegexOptions.IgnoreCase) ||
                Contains(line, "added to firewall") ||
                Contains(line, "firewall block") ||
                Regex.IsMatch(line, @"\bblocking\s+ip\b", RegexOptions.IgnoreCase))
                return MonitorKind.Ban;
            if (Contains(line, "success") && Contains(line, "login"))
                return MonitorKind.Success;
            if (Contains(line, "failed login") || Contains(line, "login attempt failed") ||
                Contains(line, "failed logon") || Contains(line, "authentication failed") ||
                Contains(line, "ipban failed login"))
                return MonitorKind.Failed;
            if (Contains(line, "ERROR") || Contains(line, "FATAL") || Contains(line, "Exception"))
                return MonitorKind.Error;
            if (Contains(line, "WARN"))
                return MonitorKind.Warn;
            return MonitorKind.Info;
        }

        static bool IsInteresting(string line)
        {
            // "IPBan" in logger-naam telt NIET als ban-event
            return Contains(line, "failed login") || Contains(line, "login attempt") ||
                   (Contains(line, "success") && Contains(line, "login")) ||
                   Regex.IsMatch(line, @"\bbanning\b|\bbanned\b|\bun-?ban", RegexOptions.IgnoreCase) ||
                   Contains(line, "firewall") ||
                   Contains(line, "firewall uri rule") ||
                   Contains(line, "ERROR") || Contains(line, "FATAL") ||
                   (Contains(line, "WARN") && (Contains(line, "login") || Contains(line, "firewall") ||
                                                Contains(line, "RDP") || Contains(line, "SSH")));
        }

        static DateTime? ParseTime(string line)
        {
            var m = RxTs.Match(line);
            if (!m.Success) return null;
            var raw = m.Groups["ts"].Value.Replace('T', ' ');
            DateTime dt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
                return dt;
            if (DateTime.TryParse(raw, out dt)) return dt;
            return null;
        }

        static string Clean(string s)
        {
            if (s == null) return null;
            s = s.Trim().Trim('"', '\'');
            var cut = s.IndexOf('|');
            if (cut > 0) s = s.Substring(0, cut).Trim();
            return s;
        }

        static bool IsJunkUser(string u)
        {
            if (string.IsNullOrWhiteSpace(u)) return true;
            return u == "-" || u == "?" || u.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                   u.Equals("unknown", StringComparison.OrdinalIgnoreCase);
        }

        static bool Contains(string hay, string needle) =>
            hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Alle logregels voor een IP (en optioneel gebruiker) — volledige historie.</summary>
        public static List<MonitorEvent> Search(string logPath, string ip, string user = null, int maxEvents = 800)
        {
            var list = new List<MonitorEvent>();
            if (!File.Exists(logPath)) return list;
            ip = (ip ?? "").Trim();
            user = (user ?? "").Trim();
            if (ip.Length == 0 && user.Length == 0) return list;

            try
            {
                var info = new FileInfo(logPath);
                var start = Math.Max(0, info.Length - 8L * 1024 * 1024);
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(start, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        if (start > 0) sr.ReadLine();
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (!LineMentions(line, ip, user)) continue;
                            var ev = ParseLine(line);
                            if (ev == null)
                            {
                                ev = new MonitorEvent
                                {
                                    Raw = line.Trim(),
                                    Kind = Classify(line),
                                    TimeLocal = ParseTime(line),
                                    Ip = ip,
                                    User = user,
                                    Title = "Logregel",
                                    Hint = line.Trim()
                                };
                            }
                            list.Add(ev);
                            if (list.Count > maxEvents * 2) break;
                        }
                    }
                }
            }
            catch
            {
                return list;
            }

            return list
                .AsEnumerable()
                .Reverse()
                .Take(maxEvents)
                .ToList();
        }

        static bool LineMentions(string line, string ip, string user)
        {
            if (ip.Length > 0 && line.IndexOf(ip, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (user.Length > 0 && user.Length >= 3 &&
                line.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0 &&
                (Contains(line, "user") || Contains(line, "login")))
                return true;
            return false;
        }

        public static string PeriodLabel(TimeSpan t)
        {
            if (t.TotalHours <= 1.1) return "afgelopen 1 uur";
            if (t.TotalHours <= 6.1) return "afgelopen 6 uur";
            if (t.TotalHours <= 24.1) return "afgelopen 24 uur";
            if (t.TotalDays <= 7.1) return "afgelopen 7 dagen";
            return "geselecteerde periode";
        }
    }
}
