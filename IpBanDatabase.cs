using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace IPBanFrontend
{
    enum IpBanState
    {
        Active = 0,
        AddPending = 1,
        RemovePending = 2,
        FailedLogin = 3,
        RemovePendingBecomeFailedLogin = 4,
        FirewallOnly = 10
    }

    sealed class IpBanEntry
    {
        public string Ip { get; set; }
        public long FailedLoginCount { get; set; }
        public DateTime? LastFailedLoginUtc { get; set; }
        public DateTime? BanDateUtc { get; set; }
        public DateTime? BanEndDateUtc { get; set; }
        public IpBanState State { get; set; }
        public bool FromFirewallOnly { get; set; }

        public string StateLabel
        {
            get
            {
                if (FromFirewallOnly) return "Actief (firewall)";
                switch (State)
                {
                    case IpBanState.Active: return "Actief (database)";
                    case IpBanState.AddPending: return "Toevoegen…";
                    case IpBanState.RemovePending: return "Verwijderen…";
                    case IpBanState.FailedLogin: return "Mislukte login";
                    case IpBanState.RemovePendingBecomeFailedLogin: return "Unban → failed";
                    case IpBanState.FirewallOnly: return "Actief (firewall)";
                    default: return State.ToString();
                }
            }
        }

        public bool IsBanned =>
            FromFirewallOnly ||
            State == IpBanState.Active ||
            State == IpBanState.AddPending ||
            State == IpBanState.FirewallOnly ||
            BanDateUtc.HasValue;
    }

    static class IpBanDatabase
    {
        public static string LastError { get; private set; }

        public static string DbPath(string installDir) =>
            Path.Combine(installDir, "ipban.sqlite");

        public static bool Exists(string installDir) => File.Exists(DbPath(installDir));

        public static List<IpBanEntry> Query(string installDir, string whereSql = null)
        {
            LastError = null;
            var list = WithRetry(() => QueryOnce(installDir, whereSql, live: true), null);
            if (list != null && list.Count > 0) return list;

            // Live read leeg of gefaald: WAL-kopie (IPBan houdt de db vaak locked).
            var copy = WithRetry(() => QueryFromCopy(installDir, whereSql), null);
            if (copy != null) return copy;

            return list ?? new List<IpBanEntry>();
        }

        static List<IpBanEntry> QueryFromCopy(string installDir, string whereSql)
        {
            var src = DbPath(installDir);
            if (!File.Exists(src)) return new List<IpBanEntry>();

            var tmpDir = Path.Combine(Path.GetTempPath(), "IPBanFrontend");
            Directory.CreateDirectory(tmpDir);
            var dest = Path.Combine(tmpDir, "ipban-read.sqlite");
            CopyDbFiles(src, dest);
            return QueryOnce(Path.GetDirectoryName(dest), whereSql, live: false, explicitPath: dest);
        }

        static void CopyDbFiles(string src, string dest)
        {
            File.Copy(src, dest, true);
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            {
                var extra = src + suffix;
                var destExtra = dest + suffix;
                if (File.Exists(extra))
                    File.Copy(extra, destExtra, true);
                else if (File.Exists(destExtra))
                    File.Delete(destExtra);
            }
        }

        static List<IpBanEntry> QueryOnce(string installDir, string whereSql, bool live, string explicitPath = null)
        {
            var list = new List<IpBanEntry>();
            var path = explicitPath ?? DbPath(installDir);
            if (!File.Exists(path)) return list;

            using (var conn = Open(path, live))
            {
                EnsureTable(conn);

                var sql = "SELECT IPAddressText, FailedLoginCount, LastFailedLogin, BanDate, State";
                if (HasColumn(conn, "BanEndDate"))
                    sql += ", BanEndDate";
                sql += " FROM IPAddresses";
                if (!string.IsNullOrWhiteSpace(whereSql))
                    sql += " WHERE " + whereSql;
                sql += " ORDER BY COALESCE(BanDate, LastFailedLogin) DESC";

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 4;
                    using (var r = cmd.ExecuteReader())
                    {
                        var hasEnd = r.FieldCount > 5;
                        while (r.Read())
                        {
                            list.Add(new IpBanEntry
                            {
                                Ip = ReadString(r, 0),
                                FailedLoginCount = ReadInt64(r, 1),
                                LastFailedLoginUtc = FromUnixMs(ReadInt64Nullable(r, 2)),
                                BanDateUtc = FromUnixMs(ReadInt64Nullable(r, 3)),
                                State = (IpBanState)(int)ReadInt64(r, 4),
                                BanEndDateUtc = hasEnd ? FromUnixMs(ReadInt64Nullable(r, 5)) : null
                            });
                        }
                    }
                }
            }

            return list;
        }

        public static List<IpBanEntry> GetBanned(string installDir) =>
            Query(installDir, "BanDate IS NOT NULL OR State IN (0,1,2,4)");

        public static List<IpBanEntry> GetFailedOnly(string installDir) =>
            Query(installDir, "State = 3 AND BanDate IS NULL");

        public static (int banned, int failed, int total) Counts(string installDir)
        {
            LastError = null;
            var live = WithRetry(() => CountsOnce(installDir, live: true), ((int banned, int failed, int total)?)null);
            if (live.HasValue && live.Value.total > 0) return live.Value;

            var copy = WithRetry(() =>
            {
                var src = DbPath(installDir);
                if (!File.Exists(src)) return (0, 0, 0);
                var tmpDir = Path.Combine(Path.GetTempPath(), "IPBanFrontend");
                Directory.CreateDirectory(tmpDir);
                var dest = Path.Combine(tmpDir, "ipban-read.sqlite");
                CopyDbFiles(src, dest);
                return CountsOnce(Path.GetDirectoryName(dest), live: false, explicitPath: dest);
            }, ((int banned, int failed, int total)?)null);

            if (copy.HasValue) return copy.Value;
            return live ?? (0, 0, 0);
        }

        static (int banned, int failed, int total) CountsOnce(string installDir, bool live, string explicitPath = null)
        {
            var path = explicitPath ?? DbPath(installDir);
            if (!File.Exists(path)) return (0, 0, 0);

            using (var conn = Open(path, live))
            {
                EnsureTable(conn);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandTimeout = 4;
                    cmd.CommandText = @"
SELECT
  SUM(CASE WHEN BanDate IS NOT NULL OR State IN (0,1,2,4) THEN 1 ELSE 0 END),
  SUM(CASE WHEN State = 3 AND BanDate IS NULL THEN 1 ELSE 0 END),
  COUNT(*)
FROM IPAddresses";
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read()) return (0, 0, 0);
                        return (
                            (int)ReadInt64(r, 0),
                            (int)ReadInt64(r, 1),
                            (int)ReadInt64(r, 2));
                    }
                }
            }
        }

        static void EnsureTable(SqliteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='IPAddresses'";
                var ok = cmd.ExecuteScalar();
                if (ok == null || ok == DBNull.Value)
                    throw new InvalidOperationException("Tabel IPAddresses ontbreekt in ipban.sqlite.");
            }
        }

        static bool HasColumn(SqliteConnection conn, string column)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(IPAddresses)";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var name = ReadString(r, 1);
                        if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }

        static SqliteConnection Open(string path, bool live)
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = live ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 3
            };
            var conn = new SqliteConnection(csb.ConnectionString);
            conn.Open();
            return conn;
        }

        static T WithRetry<T>(Func<T> action, T fallback)
        {
            Exception last = null;
            for (var i = 0; i < 4; i++)
            {
                try
                {
                    return action();
                }
                catch (SqliteException ex)
                {
                    last = ex;
                    Thread.Sleep(150 + i * 180);
                }
                catch (IOException ex)
                {
                    last = ex;
                    Thread.Sleep(150 + i * 180);
                }
                catch (UnauthorizedAccessException ex)
                {
                    last = ex;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    break;
                }
            }
            if (last != null)
                LastError = last.Message;
            return fallback;
        }

        static string ReadString(SqliteDataReader r, int i)
        {
            if (r.IsDBNull(i)) return "";
            return Convert.ToString(r.GetValue(i)) ?? "";
        }

        static long ReadInt64(SqliteDataReader r, int i)
        {
            return ReadInt64Nullable(r, i) ?? 0;
        }

        static long? ReadInt64Nullable(SqliteDataReader r, int i)
        {
            if (r.IsDBNull(i)) return null;
            try
            {
                var v = r.GetValue(i);
                if (v == null || v == DBNull.Value) return null;
                var n = Convert.ToInt64(v);
                return n <= 0 ? (long?)null : n;
            }
            catch
            {
                return null;
            }
        }

        static DateTime? FromUnixMs(long? ms)
        {
            if (!ms.HasValue || ms.Value <= 0) return null;
            try
            {
                // IPBan slaat Unix-ms op; oudere rijen soms Unix-seconden.
                if (ms.Value < 100000000000L) // vóór ~1973 in ms → eerder seconden
                    return DateTimeOffset.FromUnixTimeSeconds(ms.Value).UtcDateTime;
                return DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }
    }
}
