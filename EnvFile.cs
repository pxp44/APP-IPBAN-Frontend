using System;
using System.Collections.Generic;
using System.IO;

namespace IPBanFrontend
{
    /// <summary>Minimal KEY=value loader for local .env files (never log values).</summary>
    static class EnvFile
    {
        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var path in CandidatePaths())
            {
                string value;
                if (TryRead(path, key, out value))
                    return value;
            }
            var env = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
        }

        public static string FoundPath()
        {
            foreach (var path in CandidatePaths())
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }

        static IEnumerable<string> CandidatePaths()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in CandidateDirs())
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var path = Path.Combine(dir, ".env");
                if (seen.Add(path))
                    yield return path;
            }
        }

        static IEnumerable<string> CandidateDirs()
        {
            yield return AppDomain.CurrentDomain.BaseDirectory;
            string cwd = null;
            try { cwd = Environment.CurrentDirectory; } catch { /* ignore */ }
            if (!string.IsNullOrEmpty(cwd)) yield return cwd;
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "IPBanFrontend");

            string dir = null;
            try { dir = AppDomain.CurrentDomain.BaseDirectory; } catch { /* ignore */ }
            for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                yield return dir;
                try { dir = Directory.GetParent(dir)?.FullName; }
                catch { break; }
            }
        }

        static bool TryRead(string path, string key, out string value)
        {
            value = null;
            try
            {
                if (!File.Exists(path)) return false;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (!line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    value = Unquote(line.Substring(eq + 1).Trim());
                    return !string.IsNullOrWhiteSpace(value);
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        static string Unquote(string s)
        {
            if (s.Length >= 2)
            {
                if ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\''))
                    return s.Substring(1, s.Length - 2);
            }
            return s;
        }

        /// <summary>Update KEY=value in existing .env files plus ProgramData (never logs the value).</summary>
        public static void Upsert(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            value = value ?? "";
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in CandidatePaths())
            {
                if (File.Exists(path))
                    targets.Add(path);
            }
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "IPBanFrontend");
                Directory.CreateDirectory(dir);
                targets.Add(Path.Combine(dir, ".env"));
            }
            catch { /* ignore */ }

            foreach (var path in targets)
                WriteKey(path, key, value);
        }

        static void WriteKey(string path, string key, string value)
        {
            try
            {
                var lines = File.Exists(path)
                    ? new List<string>(File.ReadAllLines(path))
                    : new List<string>
                    {
                        "# IPThreat API key from ipthreat.net",
                        "# Do not commit this file."
                    };
                var found = false;
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (!line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        continue;
                    lines[i] = key + "=" + value;
                    found = true;
                    break;
                }
                if (!found)
                    lines.Add(key + "=" + value);
                File.WriteAllLines(path, lines);
            }
            catch { /* ignore — config write is the source of truth for IPBan */ }
        }
    }
}
