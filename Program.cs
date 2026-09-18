using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace IPBanFrontend
{
    static class Program
    {
        internal const string GuiMutexName = @"Global\IPBanFrontendGuiSingleInstance";
        internal const int WmShowMe = 0x8001; // WM_APP + 1

        /// <summary>Wordt vrijgegeven vóór elevatie zodat het nieuwe proces de mutex kan pakken.</summary>
        internal static Mutex GuiMutex;

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        const int SwRestore = 9;

        [STAThread]
        static int Main(string[] args)
        {
            args = args ?? new string[0];

            if (HasFlag(args, "--service"))
            {
                ServiceBase.Run(new FrontendService());
                return 0;
            }

            if (HasFlag(args, "--install-autostart"))
            {
                try
                {
                    if (!Elevation.IsAdministrator())
                    {
                        AppSettings.AppendServiceLog("Install: admin vereist.");
                        return 2;
                    }
                    var gui = !HasFlag(args, "--no-logon-gui");
                    AutoStartManager.Install(gui);
                    return 0;
                }
                catch (Exception ex)
                {
                    AppSettings.AppendServiceLog("Install fout: " + ex.Message);
                    return 1;
                }
            }

            if (HasFlag(args, "--uninstall-autostart"))
            {
                try
                {
                    if (!Elevation.IsAdministrator())
                    {
                        AppSettings.AppendServiceLog("Uninstall: admin vereist.");
                        return 2;
                    }
                    AutoStartManager.Uninstall();
                    return 0;
                }
                catch (Exception ex)
                {
                    AppSettings.AppendServiceLog("Uninstall fout: " + ex.Message);
                    return 1;
                }
            }

            var tray = HasFlag(args, "--tray");
            var skipElevatePrompt = HasFlag(args, "--no-elevate-prompt");

            if (!TryAcquireGuiMutex(out GuiMutex))
            {
                // Na elevatie: kort wachten tot oude proces mutex vrijgeeft
                for (var i = 0; i < 40 && !TryAcquireGuiMutex(out GuiMutex); i++)
                    Thread.Sleep(100);

                if (GuiMutex == null)
                {
                    // Bestaande instantie naar voren (ook als die “onzichtbaar” in tray hing)
                    TryActivateExistingInstance();
                    return 0;
                }
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => ShowCrash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    ShowCrash(e.ExceptionObject as Exception);

                if (!skipElevatePrompt && !tray)
                {
                    bool restarted;
                    if (!Elevation.PromptAtStartup(out restarted))
                        return restarted ? 0 : 1;
                }

                Application.Run(new MainForm(tray));
                return 0;
            }
            catch (Exception ex)
            {
                ShowCrash(ex);
                return 1;
            }
            finally
            {
                ReleaseGuiMutex();
            }
        }

        static void ShowCrash(Exception ex)
        {
            var msg = ex == null ? "Onbekende fout." : ex.Message;
            try
            {
                MessageBox.Show(
                    "IPBan Frontend is gestopt door een fout:\n\n" + msg,
                    "IPBan Frontend",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { /* geen UI */ }
        }

        static void TryActivateExistingInstance()
        {
            try
            {
                var myPid = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcessesByName("IPBanFrontend"))
                {
                    if (p.Id == myPid) continue;
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero)
                        hwnd = FindTopLevelWindowForPid((uint)p.Id);
                    if (hwnd == IntPtr.Zero) continue;

                    PostMessage(hwnd, WmShowMe, IntPtr.Zero, IntPtr.Zero);
                    ShowWindow(hwnd, SwRestore);
                    SetForegroundWindow(hwnd);
                    return;
                }
            }
            catch { /* ignore */ }

            MessageBox.Show(
                "IPBan Frontend draait al, maar het venster kon niet worden geopend.\n\n" +
                "Sluit IPBanFrontend.exe via Taakbeheer en start opnieuw.",
                "IPBan Frontend", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static IntPtr FindTopLevelWindowForPid(uint pid)
        {
            var found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                uint wPid;
                GetWindowThreadProcessId(hWnd, out wPid);
                if (wPid != pid) return true;
                var sb = new System.Text.StringBuilder(256);
                GetClassName(hWnd, sb, sb.Capacity);
                // WinForms hoofvenster
                if (sb.ToString().Contains("WindowsForms10"))
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        internal static void ReleaseGuiMutex()
        {
            try
            {
                if (GuiMutex != null)
                {
                    GuiMutex.ReleaseMutex();
                    GuiMutex.Dispose();
                    GuiMutex = null;
                }
            }
            catch
            {
                try { GuiMutex?.Dispose(); } catch { }
                GuiMutex = null;
            }
        }

        static bool TryAcquireGuiMutex(out Mutex mutex)
        {
            mutex = null;
            try
            {
                bool created;
                var m = new Mutex(true, GuiMutexName, out created);
                if (created)
                {
                    mutex = m;
                    return true;
                }

                // Bestaande mutex: probeer over te nemen (incl. abandoned)
                try
                {
                    if (m.WaitOne(0))
                    {
                        mutex = m;
                        return true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    mutex = m;
                    return true;
                }

                m.Dispose();
                return false;
            }
            catch (AbandonedMutexException)
            {
                try
                {
                    bool created;
                    mutex = new Mutex(true, GuiMutexName, out created);
                    return true;
                }
                catch
                {
                    mutex = null;
                    return false;
                }
            }
            catch
            {
                mutex = null;
                return false;
            }
        }

        static bool HasFlag(string[] args, string flag)
        {
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
