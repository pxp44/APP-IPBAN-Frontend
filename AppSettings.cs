using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace IPBanFrontend
{
    /// <summary>App-eigen settings (niet ipban.config). Opgeslagen in ProgramData.</summary>
    sealed class AppSettings
    {
        /// <summary>Windows-service starten bij boot (ook zonder ingelogde gebruiker).</summary>
        public bool Enabled { get; set; }

        /// <summary>Na inloggen ook de GUI in het systeemvak starten.</summary>
        public bool StartGuiAtLogon { get; set; } = true;

        /// <summary>Na N ban-episodes automatisch op blacklist (0 = uit). Default: 3.</summary>
        public int AutoBlacklistAfterBans { get; set; } = 3;

        public string InstallDir { get; set; } = @"C:\Program Files\IPBan";

        static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "IPBanFrontend");

        public static string FilePath => Path.Combine(Dir, "settings.json");
        public static string LogPath => Path.Combine(Dir, "service.log");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var json = File.ReadAllText(FilePath);
                var s = new AppSettings
                {
                    Enabled = ReadBool(json, "enabled") ?? ReadBool(json, "Enabled") ?? false,
                    StartGuiAtLogon = ReadBool(json, "startGuiAtLogon")
                                      ?? ReadBool(json, "StartGuiAtLogon")
                                      ?? true,
                    AutoBlacklistAfterBans = ReadInt(json, "autoBlacklistAfterBans")
                                             ?? ReadInt(json, "AutoBlacklistAfterBans")
                                             ?? 3
                };
                var dir = ReadString(json, "installDir") ?? ReadString(json, "InstallDir");
                if (!string.IsNullOrWhiteSpace(dir)) s.InstallDir = dir;
                return s;
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"enabled\": " + (Enabled ? "true" : "false") + ",");
            sb.AppendLine("  \"startGuiAtLogon\": " + (StartGuiAtLogon ? "true" : "false") + ",");
            sb.AppendLine("  \"autoBlacklistAfterBans\": " + AutoBlacklistAfterBans.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"installDir\": \"" + Escape(InstallDir ?? "") + "\"");
            sb.AppendLine("}");
            File.WriteAllText(FilePath, sb.ToString());
        }

        public static void AppendServiceLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "  " + message + Environment.NewLine);
            }
            catch { /* ignore */ }
        }

        static bool? ReadBool(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false)",
                RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
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

        static string ReadString(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        static string Escape(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
