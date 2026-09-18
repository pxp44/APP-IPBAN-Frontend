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
    enum TrendGrain
    {
        Hour,
        Day,
        Week,
        Month
    }

    sealed class TrendPoint
    {
        public string Label;
        public DateTime Start;
        public int Value;
    }

    sealed class TrendCompare
    {
        public int Recent;
        public int Previous;
        public double? PercentChange;
        public string Verdict = "Onvoldoende data";
        public string Detail = "";
        public Color Color = Color.FromArgb(100, 116, 139);
        public bool EnoughData;
    }

    /// <summary>
    /// Dagtellingen van mislukte logins + bans. Overleeft logfile-rotatie.
    /// %ProgramData%\IPBanFrontend\attempt-trend.json
    /// </summary>
    sealed class AttemptTrendStore
    {
        readonly Dictionary<string, int> _days = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> _hours = new Dictionary<string, int>(StringComparer.Ordinal);

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "IPBanFrontend", "attempt-trend.json");

        public int StoredDayCount => _days.Count;

        public DateTime? OldestDay
        {
            get
            {
                DateTime? min = null;
                foreach (var k in _days.Keys)
                {
                    DateTime d;
                    if (!DateTime.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out d))
                        continue;
                    if (!min.HasValue || d < min.Value) min = d;
                }
                return min;
            }
        }

        public static AttemptTrendStore Load()
        {
            var store = new AttemptTrendStore();
            try
            {
                if (!File.Exists(FilePath)) return store;
                var json = File.ReadAllText(FilePath);
                ReadMap(json, "days", store._days);
                ReadMap(json, "hours", store._hours);
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
            WriteMap(sb, "days", _days);
            sb.AppendLine(",");
            WriteMap(sb, "hours", _hours);
            sb.AppendLine();
            sb.AppendLine("}");
            File.WriteAllText(FilePath, sb.ToString());
        }

        /// <summary>
        /// Vervangt buckets vanaf het vroegste gescande tijdstip; oudere dagen blijven bewaard.
        /// </summary>
        public void Ingest(string logPath)
        {
            var scanDays = new Dictionary<string, int>(StringComparer.Ordinal);
            var scanHours = new Dictionary<string, int>(StringComparer.Ordinal);
            DateTime? earliest = null;

            LogMonitor.ForEachAttempt(logPath, 24L * 1024 * 1024, t =>
            {
                Inc(scanDays, DayKey(t));
                Inc(scanHours, HourKey(t));
                if (!earliest.HasValue || t < earliest.Value) earliest = t;
            });

            if (!earliest.HasValue)
            {
                Prune();
                Save();
                return;
            }

            var from = earliest.Value.Date;
            var to = DateTime.Now.Date;
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                var k = DayKey(d);
                int n;
                _days[k] = scanDays.TryGetValue(k, out n) ? n : 0;
            }

            var fromH = FloorHour(earliest.Value);
            var toH = FloorHour(DateTime.Now);
            for (var h = fromH; h <= toH; h = h.AddHours(1))
            {
                var k = HourKey(h);
                int n;
                _hours[k] = scanHours.TryGetValue(k, out n) ? n : 0;
            }

            Prune();
            Save();
        }

        public int GetDay(DateTime localDate)
        {
            int n;
            return _days.TryGetValue(DayKey(localDate), out n) ? n : 0;
        }

        public int GetHour(DateTime localHour)
        {
            int n;
            return _hours.TryGetValue(HourKey(localHour), out n) ? n : 0;
        }

        public int SumDays(DateTime fromInclusive, DateTime toExclusive)
        {
            var sum = 0;
            for (var d = fromInclusive.Date; d < toExclusive.Date; d = d.AddDays(1))
                sum += GetDay(d);
            return sum;
        }

        public string ToCsv()
        {
            var nl = CultureInfo.GetCultureInfo("nl-NL");
            var sb = new StringBuilder();
            sb.AppendLine("datum;eenheid;pogingen");
            foreach (var kv in _days.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append(";dag;").Append(kv.Value.ToString(nl)).AppendLine();
            foreach (var kv in _hours.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var hour = kv.Key.Replace("T", " ") + ":00";
                sb.Append(hour).Append(";uur;").Append(kv.Value.ToString(nl)).AppendLine();
            }
            return sb.ToString();
        }

        public double AveragePerDay(int lastDays)
        {
            if (lastDays <= 0) return 0;
            var end = DateTime.Now.Date.AddDays(1);
            var start = end.AddDays(-lastDays);
            return SumDays(start, end) / (double)lastDays;
        }

        public List<TrendPoint> BuildSeries(TrendGrain grain)
        {
            var nl = CultureInfo.GetCultureInfo("nl-NL");
            var now = DateTime.Now;
            var list = new List<TrendPoint>();

            if (grain == TrendGrain.Hour)
            {
                var end = FloorHour(now);
                for (var i = 47; i >= 0; i--)
                {
                    var h = end.AddHours(-i);
                    list.Add(new TrendPoint
                    {
                        Start = h,
                        Value = GetHour(h),
                        Label = h.ToString(h.Hour == 0 || i == 47 ? "d MMM HH:mm" : "HH:mm", nl)
                    });
                }
                return list;
            }

            if (grain == TrendGrain.Day)
            {
                var end = now.Date;
                for (var i = 29; i >= 0; i--)
                {
                    var d = end.AddDays(-i);
                    list.Add(new TrendPoint
                    {
                        Start = d,
                        Value = GetDay(d),
                        Label = d.ToString("d MMM", nl)
                    });
                }
                return list;
            }

            if (grain == TrendGrain.Week)
            {
                var end = StartOfWeek(now);
                for (var i = 11; i >= 0; i--)
                {
                    var w = end.AddDays(-7 * i);
                    var next = w.AddDays(7);
                    var to = next > now.Date.AddDays(1) ? now.Date.AddDays(1) : next;
                    list.Add(new TrendPoint
                    {
                        Start = w,
                        Value = SumDays(w, to),
                        Label = w.ToString("d MMM", nl)
                    });
                }
                return list;
            }

            var monthEnd = new DateTime(now.Year, now.Month, 1);
            for (var i = 11; i >= 0; i--)
            {
                var m = monthEnd.AddMonths(-i);
                var next = m.AddMonths(1);
                var to = next > now.Date.AddDays(1) ? now.Date.AddDays(1) : next;
                list.Add(new TrendPoint
                {
                    Start = m,
                    Value = SumDays(m, to),
                    Label = m.ToString("MMM yyyy", nl)
                });
            }
            return list;
        }

        public TrendCompare CompareDays(int recentDays)
        {
            var c = new TrendCompare();
            var end = DateTime.Now.Date.AddDays(1);
            var recentStart = end.AddDays(-recentDays);
            var prevStart = recentStart.AddDays(-recentDays);
            c.Recent = SumDays(recentStart, end);
            c.Previous = SumDays(prevStart, recentStart);

            var oldest = OldestDay;
            var havePrev = oldest.HasValue && oldest.Value <= prevStart;
            var haveRecent = _days.Count > 0;
            c.EnoughData = haveRecent && (havePrev || c.Previous > 0 || c.Recent > 0) &&
                           StoredDayCount >= Math.Min(3, recentDays);

            if (!c.EnoughData && c.Recent == 0 && c.Previous == 0)
            {
                c.Verdict = "Nog te weinig historie";
                c.Detail = "Cijfers vullen zich naarmate IPBan draait. Oudere dagen blijven bewaard, ook als de logfile roteert.";
                return c;
            }

            FillVerdict(c, recentDays + " dagen");
            return c;
        }

        public static void FillVerdict(TrendCompare c, string window)
        {
            var nl = CultureInfo.GetCultureInfo("nl-NL");
            if (c.Previous == 0 && c.Recent == 0)
            {
                c.PercentChange = 0;
                c.Verdict = "Geen aanvallen";
                c.Detail = "Geen pogingen in deze of de vorige " + window + ".";
                c.Color = Color.FromArgb(21, 128, 61);
                c.EnoughData = true;
                return;
            }

            if (c.Previous == 0)
            {
                c.PercentChange = null;
                c.Verdict = "Slechter dan voorheen";
                c.Detail = string.Format(nl,
                    "Vorige {0}: 0 pogingen, nu {1:n0}. Te weinig eerdere data om een percentage te geven.",
                    window, c.Recent);
                c.Color = Color.FromArgb(185, 28, 28);
                return;
            }

            var pct = (c.Recent - c.Previous) * 100.0 / c.Previous;
            c.PercentChange = pct;
            var abs = Math.Abs(pct).ToString("0", nl);
            var rec = c.Recent.ToString("n0", nl);
            var prev = c.Previous.ToString("n0", nl);

            if (pct <= -40)
            {
                c.Verdict = "Veel beter dan voorheen";
                c.Color = Color.FromArgb(21, 128, 61);
                c.Detail = string.Format(nl,
                    "{0}% minder pogingen dan de {1} ervoor ({2} nu vs {3} toen).",
                    abs, window, rec, prev);
            }
            else if (pct <= -15)
            {
                c.Verdict = "Beter dan voorheen";
                c.Color = Color.FromArgb(13, 148, 136);
                c.Detail = string.Format(nl,
                    "{0}% minder dan de {1} ervoor ({2} nu vs {3} toen).",
                    abs, window, rec, prev);
            }
            else if (pct < 15)
            {
                c.Verdict = "Ongeveer gelijk";
                c.Color = Color.FromArgb(161, 98, 7);
                c.Detail = string.Format(nl,
                    "Verschil {0}% t.o.v. de {1} ervoor ({2} nu vs {3} toen). Zou verder omlaag moeten.",
                    (pct >= 0 ? "+" : "") + pct.ToString("0", nl), window, rec, prev);
            }
            else if (pct < 40)
            {
                c.Verdict = "Slechter dan voorheen";
                c.Color = Color.FromArgb(194, 65, 12);
                c.Detail = string.Format(nl,
                    "{0}% meer pogingen dan de {1} ervoor ({2} nu vs {3} toen).",
                    abs, window, rec, prev);
            }
            else
            {
                c.Verdict = "Veel slechter dan voorheen";
                c.Color = Color.FromArgb(185, 28, 28);
                c.Detail = string.Format(nl,
                    "{0}% meer dan de {1} ervoor ({2} nu vs {3} toen).",
                    abs, window, rec, prev);
            }
        }

        static DateTime FloorHour(DateTime t) =>
            new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind);

        static DateTime StartOfWeek(DateTime d)
        {
            var diff = ((int)d.DayOfWeek + 6) % 7;
            return d.Date.AddDays(-diff);
        }

        static string DayKey(DateTime t) =>
            t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        static string HourKey(DateTime t) =>
            t.ToString("yyyy-MM-ddTHH", CultureInfo.InvariantCulture);

        static void Inc(Dictionary<string, int> map, string key)
        {
            int n;
            map.TryGetValue(key, out n);
            map[key] = n + 1;
        }

        void Prune()
        {
            var dayCut = DateTime.Now.Date.AddDays(-400);
            var dropDays = _days.Keys
                .Where(k =>
                {
                    DateTime d;
                    return DateTime.TryParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                               DateTimeStyles.None, out d) && d < dayCut;
                })
                .ToList();
            foreach (var k in dropDays) _days.Remove(k);

            var hourCut = FloorHour(DateTime.Now).AddHours(-72);
            var dropHours = _hours.Keys
                .Where(k =>
                {
                    DateTime h;
                    return DateTime.TryParseExact(k, "yyyy-MM-ddTHH", CultureInfo.InvariantCulture,
                               DateTimeStyles.None, out h) && h < hourCut;
                })
                .ToList();
            foreach (var k in dropHours) _hours.Remove(k);
        }

        static void ReadMap(string json, string name, Dictionary<string, int> map)
        {
            var block = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\\{([^}]*)\\}",
                RegexOptions.Singleline);
            if (!block.Success) return;
            foreach (Match m in Regex.Matches(block.Groups[1].Value,
                "\"((?:\\\\.|[^\"\\\\])+)\"\\s*:\\s*(-?\\d+)"))
            {
                int n;
                if (int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    map[Unescape(m.Groups[1].Value)] = n;
            }
        }

        static void WriteMap(StringBuilder sb, string name, Dictionary<string, int> map)
        {
            sb.Append("  \"").Append(name).AppendLine("\": {");
            var i = 0;
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (i++ > 0) sb.AppendLine(",");
                sb.Append("    \"").Append(Escape(kv.Key)).Append("\": ")
                    .Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            if (map.Count > 0) sb.AppendLine();
            sb.Append("  }");
        }

        static string Escape(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        static string Unescape(string s) =>
            (s ?? "").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
