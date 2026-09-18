using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace IPBanFrontend
{
    /// <summary>
    /// Backups en exports van %ProgramData%\IPBanFrontend (JSON, geen IPBan-sqlite).
    /// </summary>
    static class FrontendData
    {
        public static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "IPBanFrontend");

        public static string BackupDir => Path.Combine(Dir, "backups");

        static string StampPath => Path.Combine(BackupDir, "last.txt");

        public static DateTime? LastBackupUtc
        {
            get
            {
                try
                {
                    if (!File.Exists(StampPath)) return null;
                    DateTime dt;
                    if (DateTime.TryParse(File.ReadAllText(StampPath).Trim(),
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                        return dt.Kind == DateTimeKind.Unspecified
                            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                            : dt.ToUniversalTime();
                }
                catch { /* ignore */ }
                return null;
            }
        }

        public static int BackupCount
        {
            get
            {
                try
                {
                    if (!Directory.Exists(BackupDir)) return 0;
                    return Directory.GetFiles(BackupDir, "IPBanFrontend-*.zip").Length;
                }
                catch { return 0; }
            }
        }

        /// <summary>Maakt een zip als de laatste backup ouder is dan 7 dagen. Null = overgeslagen.</summary>
        public static string MaybeWeeklyBackup()
        {
            var last = LastBackupUtc;
            if (last.HasValue && DateTime.UtcNow - last.Value < TimeSpan.FromDays(7))
                return null;
            return CreateBackup();
        }

        public static string CreateBackup()
        {
            Directory.CreateDirectory(BackupDir);
            var name = "IPBanFrontend-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + ".zip";
            var path = Path.Combine(BackupDir, name);
            WriteZip(path);
            File.WriteAllText(StampPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            Prune(12);
            return path;
        }

        public static void ExportZip(string destPath)
        {
            WriteZip(destPath);
        }

        public static void ExportChartsZip(string destPath, AttemptTrendStore trend)
        {
            if (trend == null) throw new ArgumentNullException("trend");
            var tmp = Path.Combine(Path.GetTempPath(), "IPBanFrontend-charts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                WriteCharts(tmp, trend);
                if (File.Exists(destPath)) File.Delete(destPath);
                ZipFile.CreateFromDirectory(tmp, destPath, CompressionLevel.Optimal, false);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { /* ignore */ }
            }
        }

        public static void WriteCharts(string folder, AttemptTrendStore trend)
        {
            Directory.CreateDirectory(folder);
            SaveChartPng(Path.Combine(folder, "pogingen-per-uur.png"), trend, TrendGrain.Hour);
            SaveChartPng(Path.Combine(folder, "pogingen-per-dag.png"), trend, TrendGrain.Day);
            SaveChartPng(Path.Combine(folder, "pogingen-per-week.png"), trend, TrendGrain.Week);
            SaveChartPng(Path.Combine(folder, "pogingen-per-maand.png"), trend, TrendGrain.Month);
            File.WriteAllText(Path.Combine(folder, "pogingen.csv"), trend.ToCsv(), Encoding.UTF8);
        }

        static void SaveChartPng(string path, AttemptTrendStore trend, TrendGrain grain)
        {
            var points = trend.BuildSeries(grain);
            var avg = points.Count == 0 ? 0 : points.Average(p => p.Value);
            var title = grain == TrendGrain.Hour ? "Pogingen / uur"
                : grain == TrendGrain.Week ? "Pogingen / week"
                : grain == TrendGrain.Month ? "Pogingen / maand"
                : "Pogingen / dag";
            using (var bmp = TrendChartPanel.RenderBitmap(points, avg, title, grain, 1100, 520))
                bmp.Save(path, ImageFormat.Png);
        }

        static void WriteZip(string destPath)
        {
            Directory.CreateDirectory(Dir);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            if (File.Exists(destPath)) File.Delete(destPath);

            var files = Directory.Exists(Dir)
                ? Directory.GetFiles(Dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
                : new string[0];

            using (var zip = ZipFile.Open(destPath, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    var name = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(name)) continue;
                    zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                }
                if (files.Length == 0)
                    zip.CreateEntry("leeg.txt");
            }
        }

        static void Prune(int keep)
        {
            if (keep < 1 || !Directory.Exists(BackupDir)) return;
            var zips = Directory.GetFiles(BackupDir, "IPBanFrontend-*.zip")
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            for (var i = keep; i < zips.Count; i++)
            {
                try { zips[i].Delete(); } catch { /* locked */ }
            }
        }
    }
}
