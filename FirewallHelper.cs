using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IPBanFrontend
{
    sealed class FirewallRuleInfo
    {
        public string Name { get; set; }
        public string Direction { get; set; }
        public string Action { get; set; }
        public string RemoteAddresses { get; set; }
        public int AddressCount { get; set; }
        public bool? Enabled { get; set; }

        public string EnabledLabel
        {
            get
            {
                if (!Enabled.HasValue) return "?";
                return Enabled.Value ? "Actief" : "Uit";
            }
        }

        public bool IsBlock
        {
            get
            {
                var a = Action ?? "";
                return a.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0
                       || a.IndexOf("blokkeer", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }

    sealed class FirewallStatus
    {
        public string Prefix { get; set; }
        public List<FirewallRuleInfo> Rules { get; set; } = new List<FirewallRuleInfo>();
        public string Error { get; set; }

        public int Total => Rules.Count;
        public int EnabledCount => Rules.Count(r => r.Enabled == true);
        public int DisabledCount => Rules.Count(r => r.Enabled == false);
        public int BlockRules => Rules.Count(r => r.IsBlock);
        public int TotalBlockedIps => Rules.Where(r => r.IsBlock).Sum(r => r.AddressCount);

        public bool HasRules => Total > 0;
        public bool AllEnabled => HasRules && DisabledCount == 0 && Rules.All(r => r.Enabled != false);

        public string ShortLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(Error)) return "Fout";
                if (!HasRules) return "Geen regels";
                if (AllEnabled) return "Actief (" + Total + ")";
                if (EnabledCount == 0) return "Uit (" + Total + ")";
                return EnabledCount + "/" + Total + " actief";
            }
        }

        public string DetailText
        {
            get
            {
                if (!string.IsNullOrEmpty(Error))
                    return "Firewall-status lezen mislukt: " + Error;
                if (!HasRules)
                    return "Geen Windows Firewall-regels met prefix «" + Prefix + "». " +
                           "IPBan maakt die aan zodra de service draait en bans/whitelist sync’t. " +
                           "Start de IPBan-service als die gestopt is.";
                var sb = new StringBuilder();
                sb.Append(Total).Append(" regel(s) met prefix «").Append(Prefix).Append("» — ");
                sb.Append(EnabledCount).Append(" actief");
                if (DisabledCount > 0) sb.Append(", ").Append(DisabledCount).Append(" uitgeschakeld");
                sb.Append(", ").Append(BlockRules).Append(" block-regel(s)");
                if (TotalBlockedIps > 0) sb.Append(", ~").Append(TotalBlockedIps).Append(" remote IP(s) in block-regels");
                sb.Append(".");
                if (!AllEnabled)
                    sb.Append(" Let op: niet alle IPBan-regels staan aan.");
                return sb.ToString();
            }
        }

        public Color StatusColor
        {
            get
            {
                if (!string.IsNullOrEmpty(Error)) return Color.FromArgb(185, 28, 28);
                if (!HasRules) return Color.FromArgb(180, 83, 9);
                if (AllEnabled) return Color.FromArgb(21, 128, 61);
                return Color.FromArgb(161, 98, 7);
            }
        }
    }

    static class FirewallHelper
    {
        static readonly Regex RxRemote = new Regex(
            @"^(?:RemoteIP|Remote\s*IP|Extern(?:e)?\s*IP(?:-adres(?:sen)?)?)\s*:\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxName = new Regex(
            @"^(?:Rule Name|Regelnaam)\s*:\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxDir = new Regex(
            @"^(?:Direction|Richting)\s*:\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxAction = new Regex(
            @"^(?:Action|Actie)\s*:\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxEnabled = new Regex(
            @"^(?:Enabled|Ingeschakeld)\s*:\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static FirewallStatus GetStatus(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "IPBan_";
            var status = new FirewallStatus { Prefix = prefix };
            try
            {
                status.Rules = GetIpBanRules(prefix);
            }
            catch (Exception ex)
            {
                status.Error = ex.Message;
            }
            return status;
        }

        public static List<FirewallRuleInfo> GetIpBanRules(string prefix)
        {
            var result = new List<FirewallRuleInfo>();
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "IPBan_";

            string output = Run("netsh", "advfirewall firewall show rule name=all verbose");

            FirewallRuleInfo current = null;
            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.TrimEnd();
                var mName = RxName.Match(line);
                if (mName.Success)
                {
                    if (current != null && current.Name != null &&
                        current.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        result.Add(current);

                    current = new FirewallRuleInfo { Name = mName.Groups[1].Value.Trim() };
                    continue;
                }

                if (current == null) continue;

                var mDir = RxDir.Match(line);
                if (mDir.Success) { current.Direction = mDir.Groups[1].Value.Trim(); continue; }

                var mAct = RxAction.Match(line);
                if (mAct.Success) { current.Action = mAct.Groups[1].Value.Trim(); continue; }

                var mEn = RxEnabled.Match(line);
                if (mEn.Success)
                {
                    var v = mEn.Groups[1].Value.Trim();
                    current.Enabled = v.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                                      || v.Equals("Ja", StringComparison.OrdinalIgnoreCase)
                                      || v.Equals("True", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                var mRem = RxRemote.Match(line);
                if (mRem.Success)
                {
                    current.RemoteAddresses = mRem.Groups[1].Value.Trim();
                    current.AddressCount = CountAddresses(current.RemoteAddresses);
                }
            }

            if (current != null && current.Name != null &&
                current.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                result.Add(current);

            return result
                .Where(IsInbound)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void OpenWindowsFirewallAdvanced()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "wf.msc",
                    UseShellExecute = true
                });
            }
            catch
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "control.exe",
                    Arguments = "firewall.cpl",
                    UseShellExecute = true
                });
            }
        }

        public static void OpenWindowsFirewallBasic()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "firewall.cpl",
                UseShellExecute = true
            });
        }

        static bool IsInbound(FirewallRuleInfo r)
        {
            if (string.IsNullOrEmpty(r.Direction)) return true;
            var d = r.Direction;
            return d.Equals("In", StringComparison.OrdinalIgnoreCase)
                   || d.Equals("Inbound", StringComparison.OrdinalIgnoreCase)
                   || d.IndexOf("Inkomend", StringComparison.OrdinalIgnoreCase) >= 0
                   || d.StartsWith("In", StringComparison.OrdinalIgnoreCase);
        }

        public static List<string> GetBlockedIps(FirewallStatus status)
        {
            var ips = new List<string>();
            if (status == null) return ips;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in status.Rules)
            {
                if (rule == null || !rule.IsBlock) continue;
                foreach (var ip in ParseRemoteIps(rule.RemoteAddresses))
                {
                    if (seen.Add(ip)) ips.Add(ip);
                }
            }
            ips.Sort(StringComparer.OrdinalIgnoreCase);
            return ips;
        }

        public static List<string> ParseRemoteIps(string remote)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(remote)) return list;
            if (remote.Equals("Any", StringComparison.OrdinalIgnoreCase)
                || remote.Equals("Willekeurig", StringComparison.OrdinalIgnoreCase))
                return list;

            foreach (var raw in remote.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                var slash = token.IndexOf('/');
                if (slash > 0) token = token.Substring(0, slash);
                var dash = token.IndexOf('-');
                if (dash > 0) token = token.Substring(0, dash);
                token = token.Trim();
                if (LooksLikeIp(token)) list.Add(token);
            }
            return list;
        }

        static bool LooksLikeIp(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            System.Net.IPAddress ip;
            return System.Net.IPAddress.TryParse(s, out ip);
        }

        static int CountAddresses(string remote)
        {
            return ParseRemoteIps(remote).Count;
        }

        static string Run(string file, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Default
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("Kon " + file + " niet starten.");
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                p.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(90000))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("netsh timeout — firewall-overzicht te groot of geblokkeerd.");
                }
                p.WaitForExit();
                if (p.ExitCode != 0 && stdout.Length == 0)
                    throw new InvalidOperationException("netsh mislukt (" + p.ExitCode + "): " + stderr);
                return stdout.ToString();
            }
        }
    }
}
