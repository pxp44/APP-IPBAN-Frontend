using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace IPBanFrontend
{
    static class AutoStartManager
    {
        public const string ServiceName = "IPBanFrontend";
        public const string TaskName = "IPBanFrontendLogonGui";

        public static bool IsServiceInstalled()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    var _ = sc.Status;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsLogonTaskPresent()
        {
            return Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"", ignoreErrors: true) == 0;
        }

        public static string ServiceStatusText()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                    return sc.Status.ToString();
            }
            catch
            {
                return "niet geïnstalleerd";
            }
        }

        public static void Install(bool startGuiAtLogon)
        {
            RemoveServiceWithRetry();

            var exe = ApplicationPath();
            var create = "create " + ServiceName +
                         " binPath= \"" + exe + " --service\"" +
                         " start= auto" +
                         " DisplayName= \"IPBan Frontend\"";

            var code = RunScWithRetry(create, maxAttempts: 15);
            if (code != 0)
                throw new InvalidOperationException(
                    "Service aanmaken mislukt (sc exit " + code + "). Probeer opnieuw als Administrator.");

            RunSc("description " + ServiceName +
                  " \"IPBan Frontend achtergronddienst — start bij systeemstart, ook zonder ingelogde gebruiker.\"",
                ignoreErrors: true);

            RunSc("start " + ServiceName, ignoreErrors: true);
            WaitForService(ServiceControllerStatus.Running, 15000);

            UnregisterLogonTask();
            if (startGuiAtLogon)
            {
                if (!RegisterLogonTask())
                    AppSettings.AppendServiceLog("Waarschuwing: logon-GUI taak niet aangemaakt; service wel OK.");
            }

            var settings = AppSettings.Load();
            settings.Enabled = true;
            settings.StartGuiAtLogon = startGuiAtLogon;
            settings.Save();

            if (!IsServiceInstalled())
                throw new InvalidOperationException("Service lijkt niet geïnstalleerd na create.");

            AppSettings.AppendServiceLog("Autostart OK. Service=" + ServiceStatusText() +
                                         ", logonGUI=" + startGuiAtLogon +
                                         ", task=" + IsLogonTaskPresent() +
                                         ", exe=" + exe);
        }

        public static void Uninstall()
        {
            RemoveServiceWithRetry();
            UnregisterLogonTask();

            var settings = AppSettings.Load();
            settings.Enabled = false;
            settings.Save();

            AppSettings.AppendServiceLog("Autostart verwijderd.");
        }

        static void RemoveServiceWithRetry()
        {
            RunSc("stop " + ServiceName, ignoreErrors: true);
            WaitForService(ServiceControllerStatus.Stopped, 20000);
            for (var i = 0; i < 10; i++)
            {
                var code = RunSc("delete " + ServiceName, ignoreErrors: true);
                if (code == 0 || code == 1060) break;
                Thread.Sleep(500 + i * 200);
            }
            Thread.Sleep(400);
        }

        static int RunScWithRetry(string args, int maxAttempts)
        {
            int code = -1;
            for (var i = 0; i < maxAttempts; i++)
            {
                code = RunSc(args, ignoreErrors: true);
                if (code == 0) return 0;
                AppSettings.AppendServiceLog("sc retry " + (i + 1) + "/" + maxAttempts + " code=" + code);
                Thread.Sleep(500 + i * 250);
                if (code == 1073)
                {
                    if (IsServiceInstalled()) return 0;
                    RemoveServiceWithRetry();
                }
            }
            return code;
        }

        static void WaitForService(ServiceControllerStatus wanted, int timeoutMs)
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < deadline)
                    {
                        sc.Refresh();
                        if (sc.Status == wanted) return;
                        Thread.Sleep(300);
                    }
                }
            }
            catch { /* not installed */ }
        }

        static bool RegisterLogonTask()
        {
            var exe = ApplicationPath();
            var xmlPath = Path.Combine(Path.GetTempPath(), "IPBanFrontend-logon-task.xml");
            try
            {
                WriteTaskXml(xmlPath, exe);
                var code = Run("schtasks.exe",
                    "/Create /F /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\"",
                    ignoreErrors: true);
                if (code == 0 && IsLogonTaskPresent())
                {
                    AppSettings.AppendServiceLog("Logon-taak aangemaakt via XML (HighestAvailable).");
                    return true;
                }

                var trInner = "\"" + exe + "\" --tray";
                code = Run("schtasks.exe",
                    "/Create /F /TN \"" + TaskName + "\" /SC ONLOGON /RL HIGHEST /TR \"" + trInner.Replace("\"", "\\\"") + "\"",
                    ignoreErrors: true);
                if (code == 0 && IsLogonTaskPresent())
                {
                    AppSettings.AppendServiceLog("Logon-taak aangemaakt via /TR HIGHEST.");
                    return true;
                }

                code = Run("schtasks.exe",
                    "/Create /F /TN \"" + TaskName + "\" /SC ONLOGON /RL LIMITED /TR \"" + trInner.Replace("\"", "\\\"") + "\"",
                    ignoreErrors: false);
                if (code == 0 && IsLogonTaskPresent())
                {
                    AppSettings.AppendServiceLog("Logon-taak aangemaakt via /TR LIMITED.");
                    return true;
                }

                return false;
            }
            finally
            {
                try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch { }
            }
        }

        static void WriteTaskXml(string path, string exePath)
        {
            var xml = @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>IPBan Frontend GUI in systeemvak na login</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>" + XmlEscape(exePath) + @"</Command>
      <Arguments>--tray</Arguments>
      <WorkingDirectory>" + XmlEscape(Path.GetDirectoryName(exePath) ?? "") + @"</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
            File.WriteAllText(path, xml, Encoding.Unicode);
        }

        static string XmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        static void UnregisterLogonTask()
        {
            Run("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"", ignoreErrors: true);
        }

        public static string ApplicationPath()
        {
            try
            {
                var mod = Process.GetCurrentProcess().MainModule;
                if (mod != null && !string.IsNullOrEmpty(mod.FileName))
                    return Path.GetFullPath(mod.FileName);
            }
            catch { /* fall through */ }

            var loc = Assembly.GetExecutingAssembly().Location;
            return Path.GetFullPath(loc);
        }

        static int RunSc(string args, bool ignoreErrors) => Run("sc.exe", args, ignoreErrors);

        static int Run(string file, string args, bool ignoreErrors)
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
                if (p == null) return -1;
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                p.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(90000))
                {
                    try { p.Kill(); } catch { }
                    if (!ignoreErrors)
                        AppSettings.AppendServiceLog(file + " timeout: " + args);
                    return -1;
                }
                p.WaitForExit();
                if (!ignoreErrors && p.ExitCode != 0)
                    AppSettings.AppendServiceLog(file + " " + args + " => " + p.ExitCode + " " + stdout + " " + stderr);
                return p.ExitCode;
            }
        }
    }
}
