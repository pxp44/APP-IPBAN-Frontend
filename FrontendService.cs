using System;
using System.ServiceProcess;
using System.Threading;
using System.Diagnostics;

namespace IPBanFrontend
{
    /// <summary>
    /// Windows-service: start bij boot (Session 0), ook zonder ingelogde gebruiker.
    /// Geen GUI hier — die draait via de logon-taak (--tray) of handmatig.
    /// </summary>
    public class FrontendService : ServiceBase
    {
        public const string Name = AutoStartManager.ServiceName;
        Timer _timer;

        public FrontendService()
        {
            ServiceName = Name;
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            AppSettings.AppendServiceLog("Service gestart (PID " + Process.GetCurrentProcess().Id + ").");
            _timer = new Timer(Heartbeat, null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
        }

        protected override void OnStop()
        {
            try { _timer?.Dispose(); } catch { }
            _timer = null;
            AppSettings.AppendServiceLog("Service gestopt.");
        }

        void Heartbeat(object state)
        {
            try
            {
                var settings = AppSettings.Load();
                var ipban = "onbekend";
                try
                {
                    using (var sc = new ServiceController("IPBan"))
                        ipban = sc.Status.ToString();
                }
                catch { ipban = "niet gevonden"; }

                AppSettings.AppendServiceLog("Heartbeat — AutoStart.enabled=" + settings.Enabled +
                                             ", IPBan-service=" + ipban);
            }
            catch (Exception ex)
            {
                AppSettings.AppendServiceLog("Heartbeat-fout: " + ex.Message);
            }
        }
    }
}
