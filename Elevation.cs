using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace IPBanFrontend
{
    static class Elevation
    {
        public static bool IsAdministrator()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// true = doorgaan; false = dit proces afsluiten (geannuleerd of elevatie gestart).
        /// </summary>
        public static bool PromptAtStartup(out bool elevatedRestarted)
        {
            elevatedRestarted = false;
            if (IsAdministrator()) return true;

            var r = MessageBox.Show(
                "IPBan Frontend werkt het best als Administrator\n" +
                "(service starten/stoppen, config in Program Files, firewall).\n\n" +
                "Nu opnieuw starten als Administrator?\n\n" +
                "Ja = als admin starten (UAC)\n" +
                "Nee = doorgaan zonder admin (beperkt)\n" +
                "Annuleren = afsluiten",
                "Administrator-rechten",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) return true;

            elevatedRestarted = RestartElevated(Environment.GetCommandLineArgs());
            return !elevatedRestarted;
        }

        public static bool RestartElevated(string[] args = null)
        {
            try
            {
                // Mutex vrijgeven VOOR start van elevated proces, anders faalt single-instance
                Program.ReleaseGuiMutex();
                Thread.Sleep(150);

                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                if (args != null && args.Length > 1)
                {
                    var pass = new string[args.Length - 1];
                    Array.Copy(args, 1, pass, 0, pass.Length);
                    // Geen tweede elevatie-prompt in child
                    if (!ContainsFlag(pass, "--no-elevate-prompt") && !ContainsFlag(pass, "--tray"))
                    {
                        var with = new string[pass.Length + 1];
                        Array.Copy(pass, with, pass.Length);
                        with[pass.Length] = "--no-elevate-prompt";
                        pass = with;
                    }
                    psi.Arguments = QuoteArgs(pass);
                }
                else
                {
                    psi.Arguments = "--no-elevate-prompt";
                }

                Process.Start(psi);
                return true;
            }
            catch
            {
                MessageBox.Show(
                    "UAC is geannuleerd of elevatie mislukt. De app start niet als Administrator.",
                    "Administrator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        public static bool RunElevatedArgs(params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = QuoteArgs(args),
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(180000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        static bool ContainsFlag(string[] args, string flag)
        {
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static string QuoteArgs(string[] args)
        {
            if (args == null || args.Length == 0) return "";
            var parts = new string[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i] ?? "";
                parts[i] = a.IndexOfAny(new[] { ' ', '"' }) >= 0
                    ? "\"" + a.Replace("\"", "\\\"") + "\""
                    : a;
            }
            return string.Join(" ", parts);
        }
    }
}
