using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IPBanFrontend
{
    /// <summary>
    /// Telt ban-episodes per IP, bewaart opmerking (wanneer/waarom) en blacklist-moment.
    /// %ProgramData%\IPBanFrontend\ban-history.json
    /// </summary>
    sealed class BanHistoryStore
    {
        readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "IPBanFrontend", "ban-history.json");

        public sealed class Entry
        {
            public int Count;
            public string LastBanKey = "";
            public DateTime? FirstSeenUtc;
            public DateTime? LastSeenUtc;
            public string Users = "";
            public string Sources = "";
            public string Note = "";
            public DateTime? BlacklistedAtUtc;
            public long FailedPeak;
        }

        public sealed class Promotion
        {
            public string Ip;
            public Entry History;
        }

        public static BanHistoryStore Load()
        {
            var store = new BanHistoryStore();
            try
            {
                if (!File.Exists(FilePath)) return store;
                var json = File.ReadAllText(FilePath);
                foreach (Match m in Regex.Matches(json,
                    "\"((?:\\\\.|[^\"\\\\])+)\"\\s*:\\s*\\{([^}]*)\\}",
                    RegexOptions.IgnoreCase))
                {
                    var ip = Unescape(m.Groups[1].Value);
                    if (string.IsNullOrWhiteSpace(ip) ||
                        ip.Equals("entries", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var block = m.Groups[2].Value;
                    var e = new Entry
                    {
                        Count = ReadInt(block, "count") ?? 0,
                        LastBanKey = ReadStr(block, "lastBanKey") ?? "",
                        FirstSeenUtc = ReadTime(block, "firstSeen"),
                        LastSeenUtc = ReadTime(block, "lastSeen"),
                        Users = ReadStr(block, "users") ?? "",
                        Sources = ReadStr(block, "sources") ?? "",
                        Note = ReadStr(block, "note") ?? "",
                        BlacklistedAtUtc = ReadTime(block, "blacklistedAt"),
                        FailedPeak = ReadInt(block, "failedPeak") ?? 0
                    };
                    store._entries[ip] = e;
                }
            }
            catch { /* start leeg */ }
            return store;
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"entries\": {");
            var i = 0;
            foreach (var kv in _entries.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var e = kv.Value;
                if (i++ > 0) sb.AppendLine(",");
                sb.Append("    \"").Append(Escape(kv.Key)).Append("\": {");
                sb.Append(" \"count\": ").Append(e.Count.ToString(CultureInfo.InvariantCulture));
                sb.Append(", \"lastBanKey\": \"").Append(Escape(e.LastBanKey ?? "")).Append("\"");
                sb.Append(", \"firstSeen\": \"").Append(Fmt(e.FirstSeenUtc)).Append("\"");
                sb.Append(", \"lastSeen\": \"").Append(Fmt(e.LastSeenUtc)).Append("\"");
                sb.Append(", \"users\": \"").Append(Escape(e.Users ?? "")).Append("\"");
                sb.Append(", \"sources\": \"").Append(Escape(e.Sources ?? "")).Append("\"");
                sb.Append(", \"note\": \"").Append(Escape(e.Note ?? "")).Append("\"");
                sb.Append(", \"blacklistedAt\": \"").Append(Fmt(e.BlacklistedAtUtc)).Append("\"");
                sb.Append(", \"failedPeak\": ").Append(e.FailedPeak.ToString(CultureInfo.InvariantCulture));
                sb.Append(" }");
            }
            if (_entries.Count > 0) sb.AppendLine();
            sb.AppendLine("  }");
            sb.AppendLine("}");
            File.WriteAllText(FilePath, sb.ToString());
        }

        public Entry Get(string ip)
        {
            Entry e;
            return !string.IsNullOrEmpty(ip) && _entries.TryGetValue(ip, out e) ? e : null;
        }

        public int GetCount(string ip)
        {
            var e = Get(ip);
            return e != null ? e.Count : 0;
        }

        public string GetNote(string ip)
        {
            var e = Get(ip);
            return e != null ? e.Note ?? "" : "";
        }

        public void SetNote(string ip, string note)
        {
            if (string.IsNullOrWhiteSpace(ip)) return;
            var e = Ensure(ip);
            e.Note = note ?? "";
            Save();
        }

        public void MarkBlacklisted(string ip, string note)
        {
            if (string.IsNullOrWhiteSpace(ip)) return;
            var e = Ensure(ip);
            if (!e.BlacklistedAtUtc.HasValue)
                e.BlacklistedAtUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(note))
                e.Note = note;
            else if (string.IsNullOrWhiteSpace(e.Note))
                e.Note = BuildAutoNote(ip, e, "handmatig");
            Save();
        }

        public void ObserveLogEvents(IEnumerable<MonitorEvent> events)
        {
            if (events == null) return;
            var dirty = false;
            foreach (var ev in events)
            {
                if (ev == null || string.IsNullOrWhiteSpace(ev.Ip)) continue;
                if (ev.Kind != MonitorKind.Ban && ev.Kind != MonitorKind.Failed) continue;

                var when = ev.TimeLocal.HasValue
                    ? ev.TimeLocal.Value.ToUniversalTime()
                    : (DateTime?)null;
                var key = ev.Kind == MonitorKind.Ban && when.HasValue
                    ? "ban:" + when.Value.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)
                    : null;

                if (Touch(ev.Ip, when, ev.User, ev.Source, ev.Count, key, ev.Kind == MonitorKind.Ban))
                    dirty = true;
            }
            if (dirty) Save();
        }

        /// <summary>
        /// Verwerkt huidige bans. Retourneert IP’s die níu de drempel bereikten.
        /// </summary>
        public List<Promotion> ObserveAndPromote(
            IEnumerable<IpBanEntry> banned,
            int threshold,
            IEnumerable<string> whitelist,
            IEnumerable<string> alreadyBlacklisted)
        {
            var promoted = new List<Promotion>();
            if (threshold <= 0 || banned == null) return promoted;

            var white = new HashSet<string>(
                (whitelist ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);
            var black = new HashSet<string>(
                (alreadyBlacklisted ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);

            var dirty = false;
            foreach (var ban in banned)
            {
                var ip = (ban.Ip ?? "").Trim();
                if (ip.Length == 0 || white.Contains(ip)) continue;
                if (!ban.BanDateUtc.HasValue && !ban.IsBanned) continue;

                var when = ban.BanDateUtc ?? ban.LastFailedLoginUtc;
                // Zelfde minuut-sleutel als logfile, zodat log + sqlite niet dubbel tellen.
                // Firewall-only zonder datum: niet extra tellen (alleen metadata).
                string key = null;
                var countAsBan = false;
                if (ban.BanDateUtc.HasValue)
                {
                    key = "ban:" + ban.BanDateUtc.Value.ToUniversalTime()
                        .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
                    countAsBan = true;
                }

                if (Touch(ip, when, null, null, ban.FailedLoginCount, key, countAsBan))
                    dirty = true;

                var entry = Ensure(ip);
                if (entry.Count >= threshold && !black.Contains(ip))
                {
                    if (!entry.BlacklistedAtUtc.HasValue)
                        entry.BlacklistedAtUtc = DateTime.UtcNow;
                    entry.Note = BuildAutoNote(ip, entry, "drempel");
                    promoted.Add(new Promotion { Ip = ip, History = entry });
                    black.Add(ip);
                    dirty = true;
                }
            }

            if (dirty) Save();
            return promoted;
        }

        public static string BuildAutoNote(string ip, Entry e, string why)
        {
            var nl = CultureInfo.GetCultureInfo("nl-NL");
            var sb = new StringBuilder();
            sb.Append("Niet meer toelaten — ");
            if (why == "drempel")
                sb.Append("herhaaldelijk geband");
            else if (why == "handmatig")
                sb.Append("handmatig op blacklist");
            else
                sb.Append(why);
            sb.Append(". ");
            sb.Append(e.Count).Append("× geband");
            if (e.FailedPeak > 0)
                sb.Append(", piek ").Append(e.FailedPeak).Append(" mislukte logins");
            sb.Append(". ");
            if (!string.IsNullOrWhiteSpace(e.Users))
                sb.Append("Gebruiker: ").Append(e.Users).Append(". ");
            if (!string.IsNullOrWhiteSpace(e.Sources))
                sb.Append("Bron: ").Append(e.Sources).Append(". ");
            if (e.FirstSeenUtc.HasValue)
                sb.Append("Eerste: ").Append(e.FirstSeenUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm", nl)).Append(". ");
            if (e.LastSeenUtc.HasValue)
                sb.Append("Laatste: ").Append(e.LastSeenUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm", nl)).Append(". ");
            if (e.BlacklistedAtUtc.HasValue)
                sb.Append("Blacklist sinds ").Append(e.BlacklistedAtUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm", nl)).Append(".");
            return sb.ToString().Trim();
        }

        Entry Ensure(string ip)
        {
            Entry e;
            if (!_entries.TryGetValue(ip, out e))
            {
                e = new Entry();
                _entries[ip] = e;
            }
            return e;
        }

        bool Touch(string ip, DateTime? whenUtc, string user, string source, long? failed, string episodeKey, bool isBan)
        {
            var e = Ensure(ip);
            var dirty = false;

            if (whenUtc.HasValue)
            {
                if (!e.FirstSeenUtc.HasValue || whenUtc.Value < e.FirstSeenUtc.Value)
                {
                    e.FirstSeenUtc = whenUtc.Value;
                    dirty = true;
                }
                if (!e.LastSeenUtc.HasValue || whenUtc.Value > e.LastSeenUtc.Value)
                {
                    e.LastSeenUtc = whenUtc.Value;
                    dirty = true;
                }
            }

            if (MergeCsv(ref e.Users, user)) dirty = true;
            if (MergeCsv(ref e.Sources, source)) dirty = true;

            if (failed.HasValue && failed.Value > e.FailedPeak)
            {
                e.FailedPeak = failed.Value;
                dirty = true;
            }

            if (isBan && !string.IsNullOrEmpty(episodeKey) &&
                !string.Equals(e.LastBanKey, episodeKey, StringComparison.Ordinal))
            {
                var newer = true;
                if (whenUtc.HasValue && e.LastSeenUtc.HasValue &&
                    whenUtc.Value < e.LastSeenUtc.Value.AddSeconds(-2) &&
                    e.Count > 0)
                {
                    // ouder event uit de log — niet opnieuw tellen
                    newer = false;
                }
                if (newer)
                {
                    e.LastBanKey = episodeKey;
                    e.Count++;
                    dirty = true;
                }
            }

            return dirty;
        }

        static bool MergeCsv(ref string field, string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value == "-" || value == "?") return false;
            var have = new HashSet<string>(
                (field ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (!have.Add(value)) return false;
            field = string.Join(", ", have.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            return true;
        }

        static string Fmt(DateTime? utc) =>
            utc.HasValue
                ? utc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                : "";

        static DateTime? ReadTime(string block, string key)
        {
            var s = ReadStr(block, key);
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTime dt;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                return dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();
            return null;
        }

        static int? ReadInt(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)",
                RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            int n;
            return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                ? n : (int?)null;
        }

        static string ReadStr(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.IgnoreCase);
            return m.Success ? Unescape(m.Groups[1].Value) : null;
        }

        static string Escape(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        static string Unescape(string s) =>
            (s ?? "").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
