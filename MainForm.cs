using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace IPBanFrontend
{
    public class MainForm : Form
    {
        const string DefaultInstallDir = @"C:\Program Files\IPBan";
        const string ServiceName = "IPBan";

        static readonly Color Accent = Color.FromArgb(15, 118, 110);
        static readonly Color Bg = Color.FromArgb(245, 247, 250);
        static readonly Color Card = Color.White;
        static readonly Color Muted = Color.FromArgb(100, 116, 139);

        readonly TextBox txtInstallDir = new TextBox();
        readonly Label lblService = new Label();
        readonly Label lblDirty = new Label();
        readonly TabControl tabs = new HiddenHeaderTabControl();
        readonly Panel navBar = new Panel();
        readonly Dictionary<string, Button> _navBtns = new Dictionary<string, Button>();
        readonly Label lblNavHint = new Label();
        readonly CheckBox chkWhiteAdvanced = new CheckBox();
        readonly StatusStrip status = new StatusStrip();
        readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel();
        readonly Timer logTimer = new Timer { Interval = 1500 };
        readonly Timer refreshTimer = new Timer { Interval = 5000 };

        // Dashboard / monitoring
        readonly Label dashBanned = new Label();
        readonly Label dashFailed = new Label();
        readonly Label dashWhite = new Label();
        readonly Label dashBlack = new Label();
        readonly Label dashService = new Label();
        readonly Label dashAttempts = new Label();
        readonly Label dashLocations = new Label();
        readonly Label dashPeriodBans = new Label();
        readonly Label dashPeriodOk = new Label();
        readonly Label dashFirewall = new Label();
        readonly Label lblMonitorSummary = new Label();
        readonly Label lblMonitorHint = new Label();
        readonly LinkLabel lnkOpenFwMonitor = new LinkLabel();
        readonly Label lblFwStatus = new Label();
        readonly LinkLabel lnkOpenFwAdvanced = new LinkLabel();
        readonly LinkLabel lnkOpenFwBasic = new LinkLabel();
        FirewallStatus _lastFwStatus;
        DateTime _fwStatusAt = DateTime.MinValue;
        readonly ListView lvRecent = new ListView();
        readonly ListView lvTopIps = new ListView();
        readonly ComboBox cmbPeriod = new ComboBox();
        readonly CheckBox chkFiltInfo = new CheckBox();
        readonly CheckBox chkFiltWarn = new CheckBox();
        readonly CheckBox chkFiltBan = new CheckBox();
        readonly CheckBox chkFiltFailed = new CheckBox();
        readonly CheckBox chkFiltOk = new CheckBox();
        readonly TextBox txtQuickIp = new TextBox();
        TimeSpan _monitorPeriod = TimeSpan.FromHours(24);
        MonitorPeriodStats _lastMonitor;

        // Bans / failed
        readonly ListView lvBans = new ListView();
        readonly ListView lvFailed = new ListView();
        readonly TextBox txtBanFilter = new TextBox();
        readonly TextBox txtFailedFilter = new TextBox();

        // Lists
        readonly ListBox lstWhite = new ListBox();
        readonly ListBox lstBlack = new ListBox();
        readonly ListView lvBlack = new ListView();
        readonly TextBox txtNewWhite = new TextBox();
        readonly TextBox txtNewBlack = new TextBox();
        readonly TextBox txtWhiteFilter = new TextBox();
        readonly TextBox txtBlackFilter = new TextBox();
        readonly TextBox txtWhiteRegex = new TextBox();
        readonly TextBox txtBlackRegex = new TextBox();
        readonly TextBox txtWhiteBulk = new TextBox();
        readonly Label lblWhiteCount = new Label();
        readonly Label lblWhitePasteStatus = new Label();

        // Settings
        readonly DataGridView gridSettings = new DataGridView();
        readonly TextBox txtUserNames = new TextBox();
        readonly TextBox txtFirewallRules = new TextBox();
        readonly TextBox txtFirewallUriRules = new TextBox();

        // App settings / autostart
        readonly CheckBox chkAutoStart = new CheckBox();
        readonly CheckBox chkGuiAtLogon = new CheckBox();
        readonly CheckBox chkAutoBlacklist = new CheckBox();
        readonly Label lblAutoStartStatus = new Label();
        readonly Label lblElevBanner = new Label();
        readonly NotifyIcon trayIcon = new NotifyIcon();
        Icon _trayIconOwned;
        readonly bool _startInTray;
        bool _reallyExit;
        bool _balloonShown;

        // Firewall
        readonly ListView lvFw = new ListView();

        // Log
        readonly TextBox txtLog = new TextBox();
        readonly TextBox txtLogFilter = new TextBox();
        readonly CheckBox chkLogPause = new CheckBox();
        readonly ComboBox cmbLogLevel = new ComboBox();

        long _logPointer;
        bool _dirty;
        string _firewallPrefix = "IPBan_";
        readonly List<string> _whiteAll = new List<string>();
        readonly List<string> _blackAll = new List<string>();
        readonly List<IpBanEntry> _bansCache = new List<IpBanEntry>();
        readonly List<IpBanEntry> _failedCache = new List<IpBanEntry>();
        BanHistoryStore _banHistory = BanHistoryStore.Load();
        AttemptTrendStore _trend = AttemptTrendStore.Load();
        DateTime _trendAt = DateTime.MinValue;
        readonly TrendChartPanel chartTrend = new TrendChartPanel();
        readonly RadioButton rbTrendHour = new RadioButton();
        readonly RadioButton rbTrendDay = new RadioButton();
        readonly RadioButton rbTrendWeek = new RadioButton();
        readonly RadioButton rbTrendMonth = new RadioButton();
        readonly Label lblTrendVerdict = new Label();
        readonly Label lblTrendDetail = new Label();
        readonly Label lblTrendToday = new Label();
        readonly Label lblTrendAvg = new Label();
        readonly Label lblTrendWeek = new Label();
        readonly Label lblTrendVs = new Label();
        readonly Label lblTrendHint = new Label();
        readonly Panel pnlTrendVerdict = new Panel();
        readonly Label lblBackupStatus = new Label();
        DateTime _backupCheck = DateTime.MinValue;
        int _autoBlacklistAfter = 3;
        DateTime _autoBlacklistAt = DateTime.MinValue;
        bool _loadingAppSettings;

        string InstallDir => txtInstallDir.Text.Trim().TrimEnd('\\', '/');
        string ConfigPath => Path.Combine(InstallDir, "ipban.config");
        string LogPath => Path.Combine(InstallDir, "logfile.txt");

        public MainForm() : this(false) { }

        public MainForm(bool startInTray)
        {
            _startInTray = startInTray;
            Text = "IPBan Frontend";
            Width = 1120;
            Height = 780;
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            BackColor = Bg;
            ShowInTaskbar = !startInTray;
            KeyPreview = true;
            KeyDown += MainForm_KeyDown;

            BuildMenu();
            BuildUi();
            SetupTray();

            var appSet = AppSettings.Load();
            txtInstallDir.Text = string.IsNullOrWhiteSpace(appSet.InstallDir)
                ? DefaultInstallDir
                : appSet.InstallDir;

            logTimer.Tick += (s, e) => RefreshLog(false);
            refreshTimer.Tick += (s, e) =>
            {
                try
                {
                    RefreshService();
                    RefreshAutoStartStatus();
                    ProcessAutoBlacklist(false);
                    var name = tabs.SelectedTab != null ? tabs.SelectedTab.Text : "";
                    if (name == "Home") RefreshDashboard(true);
                    else if (name == "Actieve bans") RefreshBans(false);
                    else if (name == "Pogingen") RefreshFailed(false);
                    else if (name == "Blacklist") RefreshBlacklistView();
                    else if (name == "Trend") RefreshTrend(false);
                    else if (name == "Firewall") { /* zware netsh niet elke 5s */ }
                    if ((DateTime.UtcNow - _backupCheck).TotalHours >= 6)
                        RunWeeklyBackupIfDue();
                }
                catch (Exception ex)
                {
                    SetStatus("Refresh: " + ex.Message);
                }
            };

            Load += (s, e) =>
            {
                try
                {
                    UpdateElevationBanner();
                    LoadAppSettingsUi();
                    FullReload();
                    RunWeeklyBackupIfDue();
                    logTimer.Start();
                    refreshTimer.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Startfout: " + ex.Message, "IPBan Frontend",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                if (_startInTray)
                    HideToTray();
                else
                    ForceShowWindow();
            };
            FormClosing += (s, e) =>
            {
                // X / Alt+F4 → tray (niet echt afsluiten). Echt stoppen: tray → Afsluiten.
                if (!_reallyExit)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }
                if (_dirty)
                {
                    var r = MessageBox.Show(this,
                        "Er zijn niet-opgeslagen configwijzigingen. Toch afsluiten?",
                        "Niet opgeslagen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        _reallyExit = false;
                    }
                }
            };
            FormClosed += (s, e) =>
            {
                logTimer.Stop();
                refreshTimer.Stop();
                try { trayIcon.Visible = false; } catch { }
                try { trayIcon.Dispose(); } catch { }
                try { _trayIconOwned?.Dispose(); } catch { }
            };
        }

        void BuildMenu()
        {
            var menu = new MenuStrip();
            var mFile = new ToolStripMenuItem("Bestand");
            mFile.DropDownItems.Add("Config herladen", null, (s, e) => FullReload());
            mFile.DropDownItems.Add("Config opslaan", null, (s, e) => SaveConfig(true));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("IPBan-map openen", null, (s, e) => OpenFolder(InstallDir));
            mFile.DropDownItems.Add("ipban.config openen", null, (s, e) => OpenFile(ConfigPath));
            mFile.DropDownItems.Add("Frontend-data openen", null, (s, e) =>
            {
                Directory.CreateDirectory(FrontendData.Dir);
                OpenFolder(FrontendData.Dir);
            });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Data exporteren (zip)…", null, (s, e) => ExportFrontendZip());
            mFile.DropDownItems.Add("Grafieken exporteren…", null, (s, e) => ExportAllCharts());
            mFile.DropDownItems.Add("Nu backup maken", null, (s, e) => MakeFrontendBackup(true));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Afsluiten", null, (s, e) => ExitApp());

            var mSvc = new ToolStripMenuItem("Service");
            mSvc.DropDownItems.Add("Start IPBan", null, (s, e) => ControlService(true));
            mSvc.DropDownItems.Add("Stop IPBan", null, (s, e) => ControlService(false));
            mSvc.DropDownItems.Add("Herstart IPBan", null, (s, e) => RestartService());
            mSvc.DropDownItems.Add(new ToolStripSeparator());
            mSvc.DropDownItems.Add("Opnieuw starten als Administrator…", null, (s, e) =>
            {
                if (Elevation.RestartElevated())
                    ExitApp();
            });

            var mAct = new ToolStripMenuItem("Acties");
            mAct.DropDownItems.Add("Whitelist beheren…", null, (s, e) => GoToWhitelistTab());
            mAct.DropDownItems.Add("Mijn publieke IP whitelisten", null, async (s, e) => await WhitelistMyIpAsync());
            mAct.DropDownItems.Add(new ToolStripSeparator());
            mAct.DropDownItems.Add("IP ban…", null, (s, e) => PromptBan(true));
            mAct.DropDownItems.Add("IP unban…", null, (s, e) => PromptBan(false));
            mAct.DropDownItems.Add(new ToolStripSeparator());
            mAct.DropDownItems.Add("Bans exporteren (CSV)…", null, (s, e) => ExportBans());

            var mHelp = new ToolStripMenuItem("Help");
            mHelp.DropDownItems.Add("Over", null, (s, e) =>
                MessageBox.Show(this,
                    "IPBan Frontend — lokale GUI voor de gratis IPBan-service.\n\n" +
                    "Beheert whitelist/blacklist, instellingen, actieve bans (sqlite + firewall),\n" +
                    "mislukte logins en live logs — zonder XML handmatig te bewerken.\n\n" +
                    "Geen multi-server / country-block / IPBan Shield (dat is Pro).\n\n" +
                    "Sneltoetsen:  Ctrl+1 Home  ·  Ctrl+2 Whitelist  ·  Ctrl+S Opslaan  ·  Ctrl+R Herladen\n" +
                    "https://ipban.com",
                    "Over IPBan Frontend", MessageBoxButtons.OK, MessageBoxIcon.Information));

            menu.Items.AddRange(new ToolStripItem[] { mFile, mSvc, mAct, mHelp });
            MainMenuStrip = menu;
        }

        void BuildUi()
        {
            lblElevBanner.Dock = DockStyle.Top;
            lblElevBanner.Height = 28;
            lblElevBanner.TextAlign = ContentAlignment.MiddleLeft;
            lblElevBanner.Padding = new Padding(12, 0, 8, 0);
            lblElevBanner.Visible = false;
            lblElevBanner.Cursor = Cursors.Hand;
            lblElevBanner.Click += (s, e) =>
            {
                if (!Elevation.IsAdministrator() && Elevation.RestartElevated())
                    ExitApp();
            };

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                ColumnCount = 3,
                BackColor = Card,
                Padding = new Padding(10, 6, 10, 6)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310f));

            var pathBox = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                Margin = new Padding(0)
            };
            pathBox.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44f));
            pathBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pathBox.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36f));
            var lblDir = new Label
            {
                Text = "IPBan",
                AutoSize = true,
                ForeColor = Muted,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 4, 0)
            };
            txtInstallDir.Dock = DockStyle.Fill;
            txtInstallDir.Margin = new Padding(0, 4, 6, 4);
            var btnBrowse = MakeButton("…", 0, 0, 34);
            btnBrowse.Dock = DockStyle.Fill;
            btnBrowse.Margin = new Padding(0, 4, 0, 4);
            btnBrowse.Click += (s, e) => BrowseFolder();
            pathBox.Controls.Add(lblDir, 0, 0);
            pathBox.Controls.Add(txtInstallDir, 1, 0);
            pathBox.Controls.Add(btnBrowse, 2, 0);

            var statusBox = new Panel { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 6, 0) };
            lblService.Dock = DockStyle.Top;
            lblService.Height = 20;
            lblService.Font = new Font(Font, FontStyle.Bold);
            lblService.AutoEllipsis = true;
            lblDirty.Dock = DockStyle.Fill;
            lblDirty.ForeColor = Color.DarkOrange;
            lblDirty.Text = "";
            statusBox.Controls.Add(lblDirty);
            statusBox.Controls.Add(lblService);

            var btns = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0, 2, 0, 0)
            };
            var btnStart = MakeButton("Start", 0, 0, 64);
            btnStart.Click += (s, e) => ControlService(true);
            var btnStop = MakeButton("Stop", 0, 0, 64);
            btnStop.Click += (s, e) => ControlService(false);
            var btnRestart = MakeButton("Herstart", 0, 0, 78);
            btnRestart.Click += (s, e) => RestartService();
            var btnSave = MakeButton("Opslaan", 0, 0, 84);
            btnSave.BackColor = Accent;
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Click += (s, e) => SaveConfig(true);
            btns.Controls.AddRange(new Control[] { btnStart, btnStop, btnRestart, btnSave });

            top.Controls.Add(pathBox, 0, 0);
            top.Controls.Add(statusBox, 1, 0);
            top.Controls.Add(btns, 2, 0);

            BuildNavBar();

            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(8, 4);

            tabs.TabPages.Add(BuildDashboardTab());
            tabs.TabPages.Add(BuildWhitelistTab());
            tabs.TabPages.Add(BuildBlacklistTab());
            tabs.TabPages.Add(BuildBansTab());
            tabs.TabPages.Add(BuildFailedTab());
            tabs.TabPages.Add(BuildTrendTab());
            tabs.TabPages.Add(BuildFirewallTab());
            tabs.TabPages.Add(BuildLogTab());
            tabs.TabPages.Add(BuildSettingsTab());
            tabs.SelectedIndexChanged += (s, e) =>
            {
                HighlightNav();
                var name = tabs.SelectedTab != null ? tabs.SelectedTab.Text : "";
                if (name == "Home") RefreshDashboard(true);
                else if (name == "Whitelist") RefreshWhitelistUi();
                else if (name == "Blacklist") RefreshBlacklistView();
                else if (name == "Actieve bans") RefreshBans(true);
                else if (name == "Pogingen") RefreshFailed(true);
                else if (name == "Trend") RefreshTrend(true);
                else if (name == "Firewall") RefreshFirewall();
            };

            statusLabel.Spring = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            status.Items.Add(statusLabel);
            status.SizingGrip = false;
            status.Dock = DockStyle.Bottom;

            // Shell: menu blijft op form-niveau (MainMenuStrip); rest in body zodat niets overlapt
            var body = new Panel { Dock = DockStyle.Fill, Name = "bodyShell" };
            body.Controls.Add(tabs);
            body.Controls.Add(status);
            body.Controls.Add(navBar);
            body.Controls.Add(top);
            body.Controls.Add(lblElevBanner);

            Controls.Add(body);
            if (MainMenuStrip != null)
                Controls.Add(MainMenuStrip);

            HighlightNav();

            Shown += (s, e) =>
            {
                FixSplitters();
                if (!_startInTray)
                    ForceShowWindow();
            };
            Resize += (s, e) => FixSplitters();
        }

        void FixSplitters()
        {
            foreach (TabPage page in tabs.TabPages)
            {
                foreach (Control c in page.Controls)
                {
                    var sc = c as SplitContainer;
                    if (sc == null) continue;
                    SafeSplitterDistance(sc, page.Text == "Whitelist" ? 0.48 : 0.55);
                }
            }
        }

        static void SafeSplitterDistance(SplitContainer sc, double leftRatio)
        {
            if (sc == null || sc.Width < 80) return;
            var min = 160;
            var max = Math.Max(min + 40, sc.Width - min - sc.SplitterWidth);
            var desired = (int)(sc.Width * leftRatio);
            desired = Math.Max(min, Math.Min(max, desired));
            try
            {
                if (Math.Abs(sc.SplitterDistance - desired) > 8)
                    sc.SplitterDistance = desired;
            }
            catch
            {
                /* layout nog niet klaar */
            }
        }

        void BuildNavBar()
        {
            navBar.Dock = DockStyle.Top;
            navBar.Height = 46;
            navBar.BackColor = Color.FromArgb(15, 23, 42);
            lblNavHint.Dock = DockStyle.Right;
            lblNavHint.AutoSize = true;
            lblNavHint.ForeColor = Color.FromArgb(148, 163, 184);
            lblNavHint.Padding = new Padding(0, 14, 14, 0);
            lblNavHint.Text = "Altijd terug via Home";
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(8, 7, 8, 7)
            };
            AddNavButton(flow, "Home", "Home", true);
            AddNavButton(flow, "Whitelist", "Whitelist", false);
            AddNavButton(flow, "Blacklist", "Blacklist", false);
            AddNavButton(flow, "Bans", "Actieve bans", false);
            AddNavButton(flow, "Pogingen", "Pogingen", false);
            AddNavButton(flow, "Trend", "Trend", false);
            AddNavButton(flow, "Firewall", "Firewall", false);
            AddNavButton(flow, "Log", "Log", false);
            AddNavButton(flow, "Instellingen", "Instellingen", false);
            navBar.Controls.Add(flow);
            navBar.Controls.Add(lblNavHint);
        }

        void AddNavButton(FlowLayoutPanel flow, string label, string tabText, bool home)
        {
            var b = new Button
            {
                Text = home ? "←  Home" : label,
                AutoSize = true,
                Height = 32,
                MinimumSize = new Size(home ? 110 : 86, 32),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(51, 65, 85),
                Cursor = Cursors.Hand,
                Tag = tabText,
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(12, 0, 12, 0),
                Font = new Font(Font.FontFamily, 9.5f, home ? FontStyle.Bold : FontStyle.Regular)
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => SelectTab(tabText);
            _navBtns[tabText] = b;
            flow.Controls.Add(b);
        }

        void SelectTab(string tabText)
        {
            foreach (TabPage p in tabs.TabPages)
            {
                if (p.Text == tabText)
                {
                    tabs.SelectedTab = p;
                    HighlightNav();
                    return;
                }
            }
        }

        void HighlightNav()
        {
            var cur = tabs.SelectedTab != null ? tabs.SelectedTab.Text : "";
            foreach (var kv in _navBtns)
            {
                var on = kv.Key == cur;
                var home = kv.Key == "Home";
                kv.Value.BackColor = on
                    ? Accent
                    : home ? Color.FromArgb(30, 64, 75) : Color.FromArgb(51, 65, 85);
                kv.Value.Font = new Font(Font.FontFamily, 9.5f, on || home ? FontStyle.Bold : FontStyle.Regular);
            }
            lblNavHint.Text = cur == "Home" || string.IsNullOrEmpty(cur)
                ? "Ctrl+1 Home  ·  Ctrl+6 Trend  ·  Ctrl+S Opslaan"
                : "← Home om terug te gaan";
        }

        void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Control) return;
            switch (e.KeyCode)
            {
                case Keys.D1: SelectTab("Home"); e.Handled = true; break;
                case Keys.D2: SelectTab("Whitelist"); e.Handled = true; break;
                case Keys.D3: SelectTab("Blacklist"); e.Handled = true; break;
                case Keys.D4: SelectTab("Actieve bans"); e.Handled = true; break;
                case Keys.D5: SelectTab("Pogingen"); e.Handled = true; break;
                case Keys.D6: SelectTab("Trend"); e.Handled = true; break;
                case Keys.S: SaveConfig(true); e.Handled = true; break;
                case Keys.R: FullReload(); e.Handled = true; break;
            }
        }

        static Button MakeButton(string text, int left, int top, int width)
        {
            return new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 28,
                FlatStyle = FlatStyle.System
            };
        }

        TabPage BuildDashboardTab()
        {
            var page = new TabPage("Home");
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3,
                Padding = new Padding(12, 10, 12, 8)
            };
            for (int i = 0; i < 4; i++)
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(MakeStatCard("Service", dashService, Color.FromArgb(21, 128, 61)), 0, 0);
            var fwCard = MakeStatCard("Firewall", dashFirewall, Color.FromArgb(3, 105, 161));
            BindCardClick(fwCard, () => SelectTab("Firewall"));
            root.Controls.Add(fwCard, 1, 0);
            var banCard = MakeStatCard("Actieve bans", dashBanned, Color.FromArgb(185, 28, 28));
            BindCardClick(banCard, () => SelectTab("Actieve bans"));
            root.Controls.Add(banCard, 2, 0);
            var failCard = MakeStatCard("Open failed", dashFailed, Color.FromArgb(180, 83, 9));
            BindCardClick(failCard, () => SelectTab("Pogingen"));
            root.Controls.Add(failCard, 3, 0);

            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = new Padding(4, 2, 4, 0)
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));

            var leftTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Padding = new Padding(0, 2, 0, 0)
            };
            cmbPeriod.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbPeriod.Items.AddRange(new object[] { "1 uur", "6 uur", "24 uur", "7 dagen" });
            cmbPeriod.SelectedIndex = 2;
            cmbPeriod.Width = 88;
            cmbPeriod.Margin = new Padding(0, 4, 10, 0);
            cmbPeriod.SelectedIndexChanged += (s, e) =>
            {
                switch (cmbPeriod.SelectedIndex)
                {
                    case 0: _monitorPeriod = TimeSpan.FromHours(1); break;
                    case 1: _monitorPeriod = TimeSpan.FromHours(6); break;
                    case 3: _monitorPeriod = TimeSpan.FromDays(7); break;
                    default: _monitorPeriod = TimeSpan.FromHours(24); break;
                }
                RefreshDashboard(true);
            };
            SetupFilt(chkFiltFailed, "Mislukt", true);
            SetupFilt(chkFiltBan, "Ban", true);
            SetupFilt(chkFiltWarn, "Warn", true);
            SetupFilt(chkFiltInfo, "Info", false);
            SetupFilt(chkFiltOk, "OK", true);
            var lnkTrend = new LinkLabel
            {
                Text = "Trend →",
                AutoSize = true,
                LinkColor = Accent,
                ActiveLinkColor = Accent,
                Margin = new Padding(10, 8, 0, 0)
            };
            lnkTrend.LinkClicked += (s, e) => SelectTab("Trend");
            leftTools.Controls.AddRange(new Control[]
            {
                cmbPeriod, chkFiltFailed, chkFiltBan, chkFiltWarn, chkFiltInfo, chkFiltOk, lnkTrend
            });

            var rightTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 2, 0, 0)
            };
            var btnRefresh = MakeButton("Vernieuwen", 0, 0, 90);
            btnRefresh.Click += (s, e) => RefreshDashboard(true);
            var btnBanSel = MakeButton("Ban selectie", 0, 0, 100);
            btnBanSel.Click += (s, e) => BanMonitorSelection();
            var btnUnban = MakeButton("Unban", 0, 0, 70);
            btnUnban.Click += (s, e) => WriteDropFile("unban.txt", GetCueText(txtQuickIp));
            var btnBan = MakeButton("Ban", 0, 0, 64);
            btnBan.Click += (s, e) => WriteDropFile("ban.txt", GetCueText(txtQuickIp));
            txtQuickIp.Width = 140;
            txtQuickIp.Margin = new Padding(0, 4, 6, 0);
            SetCue(txtQuickIp, "IP, bijv. 1.2.3.4");
            var btnHistHome = MakeButton("Log van IP", 0, 0, 90);
            btnHistHome.Click += (s, e) => ShowHistoryForSelection();
            rightTools.Controls.AddRange(new Control[] { btnRefresh, btnBanSel, btnUnban, btnBan, btnHistHome, txtQuickIp });

            toolbar.Controls.Add(leftTools, 0, 0);
            toolbar.Controls.Add(rightTools, 1, 0);
            root.SetColumnSpan(toolbar, 4);
            root.Controls.Add(toolbar, 0, 1);

            var lists = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 720,
                Margin = new Padding(4, 4, 4, 0)
            };

            StyleListView(lvRecent);
            lvRecent.Dock = DockStyle.Fill;
            lvRecent.Columns.Add("Tijd", 120);
            lvRecent.Columns.Add("Type", 75);
            lvRecent.Columns.Add("IP / locatie", 130);
            lvRecent.Columns.Add("Gebruiker", 140);
            lvRecent.Columns.Add("Bron", 90);
            lvRecent.Columns.Add("Issue / hint", 280);
            lvRecent.Columns.Add("#", 40);
            lvRecent.FullRowSelect = true;
            lvRecent.MultiSelect = true;
            lvRecent.SelectedIndexChanged += (s, e) => ShowMonitorSelectionHint();
            lvRecent.DoubleClick += (s, e) => ShowMonitorEventDetail();
            var ctx = new ContextMenuStrip();
            ctx.Items.Add("Ban dit IP", null, (s, e) => BanMonitorSelection());
            ctx.Items.Add("Unban dit IP", null, (s, e) =>
            {
                var ips = GetSelectedMonitorIps();
                if (ips.Count > 0) WriteDropFile("unban.txt", string.Join(Environment.NewLine, ips));
            });
            ctx.Items.Add("Naar whitelist", null, (s, e) => WhitelistMonitorSelection());
            ctx.Items.Add("Permanente blacklist + opmerking", null, (s, e) => BlacklistMonitorSelection());
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("Log van dit IP (alle historie)", null, (s, e) => ShowHistoryForSelection());
            ctx.Items.Add("Details…", null, (s, e) => ShowMonitorEventDetail());
            lvRecent.ContextMenuStrip = ctx;

            var leftWrap = new Panel { Dock = DockStyle.Fill };
            lblMonitorSummary.Dock = DockStyle.Top;
            lblMonitorSummary.Height = 22;
            lblMonitorSummary.Font = new Font("Segoe UI Semibold", 9.5f);
            lblMonitorSummary.ForeColor = Accent;
            lblMonitorSummary.Text = "Events laden…";
            lblMonitorHint.Dock = DockStyle.Bottom;
            lblMonitorHint.Height = 32;
            lblMonitorHint.ForeColor = Color.FromArgb(51, 65, 85);
            lblMonitorHint.Text = "Selecteer een regel — of rechtsklik voor Ban / Whitelist.";
            lnkOpenFwMonitor.Text = "Windows Firewall openen";
            lnkOpenFwMonitor.AutoSize = true;
            lnkOpenFwMonitor.LinkColor = Accent;
            lnkOpenFwMonitor.Dock = DockStyle.Bottom;
            lnkOpenFwMonitor.Height = 18;
            lnkOpenFwMonitor.LinkClicked += (s, e) => FirewallHelper.OpenWindowsFirewallAdvanced();
            leftWrap.Controls.Add(lvRecent);
            leftWrap.Controls.Add(lblMonitorHint);
            leftWrap.Controls.Add(lnkOpenFwMonitor);
            leftWrap.Controls.Add(lblMonitorSummary);

            StyleListView(lvTopIps);
            lvTopIps.Dock = DockStyle.Fill;
            lvTopIps.Columns.Add("IP", 140);
            lvTopIps.Columns.Add("Pogingen", 70);
            lvTopIps.Columns.Add("%", 50);
            lvTopIps.FullRowSelect = true;
            lvTopIps.SelectedIndexChanged += (s, e) =>
            {
                if (lvTopIps.SelectedItems.Count == 0) return;
                var ip = lvTopIps.SelectedItems[0].Text;
                txtQuickIp.ForeColor = SystemColors.WindowText;
                txtQuickIp.Text = ip;
                lblMonitorHint.Text = ip + " — Ban, Unban of rechtsklik op een event.";
            };

            var rightWrap = new Panel { Dock = DockStyle.Fill };
            var lblTop = new Label
            {
                Text = "  Meeste pogingen",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Muted
            };
            rightWrap.Controls.Add(lvTopIps);
            rightWrap.Controls.Add(lblTop);

            lists.Panel1.Controls.Add(leftWrap);
            lists.Panel2.Controls.Add(rightWrap);
            root.SetColumnSpan(lists, 4);
            root.Controls.Add(lists, 0, 2);

            page.Controls.Add(root);
            return page;
        }

        void GoToWhitelistTab()
        {
            SelectTab("Whitelist");
            RefreshWhitelistUi();
        }

        TabPage BuildTrendTab()
        {
            var page = new TabPage("Trend");
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 5,
                Padding = new Padding(12, 10, 12, 8)
            };
            for (int i = 0; i < 4; i++)
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            pnlTrendVerdict.Dock = DockStyle.Fill;
            pnlTrendVerdict.BackColor = Color.FromArgb(240, 253, 250);
            pnlTrendVerdict.Padding = new Padding(14, 8, 14, 8);
            pnlTrendVerdict.Margin = new Padding(4, 0, 4, 4);
            lblTrendVerdict.Dock = DockStyle.Top;
            lblTrendVerdict.Height = 26;
            lblTrendVerdict.Font = new Font("Segoe UI Semibold", 13f);
            lblTrendVerdict.ForeColor = Accent;
            lblTrendVerdict.Text = "Trend laden…";
            lblTrendDetail.Dock = DockStyle.Fill;
            lblTrendDetail.ForeColor = Color.FromArgb(51, 65, 85);
            lblTrendDetail.Text = "Vergelijkt de laatste 7 dagen met de 7 dagen ervoor.";
            pnlTrendVerdict.Controls.Add(lblTrendDetail);
            pnlTrendVerdict.Controls.Add(lblTrendVerdict);
            root.SetColumnSpan(pnlTrendVerdict, 4);
            root.Controls.Add(pnlTrendVerdict, 0, 0);

            root.Controls.Add(MakeStatCard("Vandaag", lblTrendToday, Color.FromArgb(194, 65, 12)), 0, 1);
            root.Controls.Add(MakeStatCard("Gemiddeld / dag (30 d)", lblTrendAvg, Accent), 1, 1);
            root.Controls.Add(MakeStatCard("Deze week", lblTrendWeek, Color.FromArgb(3, 105, 161)), 2, 1);
            root.Controls.Add(MakeStatCard("vs. week ervoor", lblTrendVs, Color.FromArgb(21, 128, 61)), 3, 1);

            var tools = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Padding = new Padding(4, 4, 4, 0)
            };
            var lblView = new Label
            {
                Text = "Grafiek",
                AutoSize = true,
                ForeColor = Muted,
                Margin = new Padding(0, 8, 8, 0)
            };
            rbTrendDay.Checked = true;
            SetupTrendRadio(rbTrendHour, "Per uur");
            SetupTrendRadio(rbTrendDay, "Per dag");
            SetupTrendRadio(rbTrendWeek, "Per week");
            SetupTrendRadio(rbTrendMonth, "Per maand");
            var btnRef = MakeButton("Vernieuwen", 0, 0, 100);
            btnRef.Margin = new Padding(16, 2, 0, 0);
            btnRef.Click += (s, e) => RefreshTrend(true);
            var btnPng = MakeButton("Exporteer grafiek", 0, 0, 130);
            btnPng.Margin = new Padding(8, 2, 0, 0);
            btnPng.Click += (s, e) => ExportCurrentChart();
            var btnAll = MakeButton("Alle grafieken", 0, 0, 120);
            btnAll.Margin = new Padding(6, 2, 0, 0);
            btnAll.Click += (s, e) => ExportAllCharts();
            var btnZip = MakeButton("Zip data", 0, 0, 90);
            btnZip.Margin = new Padding(6, 2, 0, 0);
            btnZip.Click += (s, e) => ExportFrontendZip();
            tools.Controls.AddRange(new Control[]
            {
                lblView, rbTrendHour, rbTrendDay, rbTrendWeek, rbTrendMonth, btnRef, btnPng, btnAll, btnZip
            });
            root.SetColumnSpan(tools, 4);
            root.Controls.Add(tools, 0, 2);

            chartTrend.Dock = DockStyle.Fill;
            var chartWrap = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Card,
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(4, 2, 4, 2)
            };
            chartWrap.Controls.Add(chartTrend);
            root.SetColumnSpan(chartWrap, 4);
            root.Controls.Add(chartWrap, 0, 3);

            lblTrendHint.Dock = DockStyle.Fill;
            lblTrendHint.ForeColor = Muted;
            lblTrendHint.Text = "Staven = pogingen (mislukte login + ban). Stippellijn = gemiddelde van deze weergave. Groen onder het gemiddelde, oranje erboven.";
            root.SetColumnSpan(lblTrendHint, 4);
            root.Controls.Add(lblTrendHint, 0, 4);

            page.Controls.Add(root);
            return page;
        }

        void SetupTrendRadio(RadioButton rb, string text)
        {
            rb.Text = text;
            rb.AutoSize = true;
            rb.Margin = new Padding(0, 6, 14, 0);
            rb.CheckedChanged += (s, e) =>
            {
                if (rb.Checked) PaintTrendChart();
            };
        }

        TrendGrain CurrentTrendGrain()
        {
            if (rbTrendHour.Checked) return TrendGrain.Hour;
            if (rbTrendWeek.Checked) return TrendGrain.Week;
            if (rbTrendMonth.Checked) return TrendGrain.Month;
            return TrendGrain.Day;
        }

        void IngestTrend(bool force)
        {
            if (!force && (DateTime.UtcNow - _trendAt).TotalSeconds < 60)
                return;
            if (_trend == null) _trend = AttemptTrendStore.Load();
            _trend.Ingest(LogPath);
            _trendAt = DateTime.UtcNow;
        }

        void RefreshTrend(bool force)
        {
            try
            {
                IngestTrend(force);
                var nl = CultureInfo.GetCultureInfo("nl-NL");
                var today = DateTime.Now.Date;
                var weekStart = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
                var cmp = _trend.CompareDays(7);

                lblTrendToday.Text = _trend.GetDay(today).ToString("n0", nl);
                lblTrendAvg.Text = _trend.AveragePerDay(30).ToString("0.0", nl);
                lblTrendWeek.Text = _trend.SumDays(weekStart, today.AddDays(1)).ToString("n0", nl);

                if (cmp.PercentChange.HasValue)
                {
                    var pct = cmp.PercentChange.Value;
                    lblTrendVs.Text = (pct > 0 ? "+" : "") + pct.ToString("0", nl) + "%";
                    lblTrendVs.ForeColor = pct <= -15
                        ? Color.FromArgb(21, 128, 61)
                        : pct >= 15 ? Color.FromArgb(185, 28, 28) : Color.FromArgb(15, 23, 42);
                }
                else
                {
                    lblTrendVs.Text = cmp.Recent == 0 && cmp.Previous == 0 ? "0" : "n.v.t.";
                    lblTrendVs.ForeColor = Color.FromArgb(15, 23, 42);
                }

                lblTrendVerdict.Text = cmp.Verdict;
                lblTrendVerdict.ForeColor = cmp.Color;
                lblTrendDetail.Text = cmp.Detail;
                pnlTrendVerdict.BackColor = Color.FromArgb(
                    255,
                    Math.Min(255, 245 + (cmp.Color.R - 245) / 8),
                    Math.Min(255, 247 + (cmp.Color.G - 247) / 8),
                    Math.Min(255, 250 + (cmp.Color.B - 250) / 8));

                PaintTrendChart();
                SetStatus("Trend: " + cmp.Verdict.ToLowerInvariant());
            }
            catch (Exception ex)
            {
                SetStatus("Trend: " + ex.Message);
            }
        }

        void PaintTrendChart()
        {
            if (_trend == null || chartTrend == null || chartTrend.IsDisposed) return;
            var grain = CurrentTrendGrain();
            var points = _trend.BuildSeries(grain);
            var avg = points.Count == 0 ? 0 : points.Average(p => p.Value);
            var yTitle = grain == TrendGrain.Hour ? "Pogingen / uur"
                : grain == TrendGrain.Week ? "Pogingen / week"
                : grain == TrendGrain.Month ? "Pogingen / maand"
                : "Pogingen / dag";
            chartTrend.SetData(points, avg, yTitle, grain);

            var nl = CultureInfo.GetCultureInfo("nl-NL");
            var window = grain == TrendGrain.Hour ? "laatste 48 uur"
                : grain == TrendGrain.Week ? "laatste 12 weken"
                : grain == TrendGrain.Month ? "laatste 12 maanden"
                : "laatste 30 dagen";
            lblTrendHint.Text = string.Format(nl,
                "{0}  ·  gemiddelde {1:0.0}  ·  groen = onder gemiddelde, oranje = erboven. Oudere dagen blijven bewaard als de logfile roteert.",
                char.ToUpper(window[0]) + window.Substring(1), avg);
        }

        static Panel MakeChip(Color c)
        {
            return new Panel { Width = 14, Height = 14, BackColor = c, Margin = new Padding(2, 2, 2, 2) };
        }

        Control MakeStatCard(string title, Label valueLabel, Color accent)
        {
            var p = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                BackColor = Card,
                Padding = new Padding(12, 8, 12, 8)
            };
            var bar = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accent };
            var t = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Muted
            };
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.Font = new Font("Segoe UI Semibold", 16f);
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.Text = "—";
            p.Controls.Add(valueLabel);
            p.Controls.Add(t);
            p.Controls.Add(bar);
            return p;
        }

        static void BindCardClick(Control card, Action onClick)
        {
            card.Cursor = Cursors.Hand;
            EventHandler go = (s, e) => onClick();
            card.Click += go;
            foreach (Control c in card.Controls)
            {
                c.Cursor = Cursors.Hand;
                c.Click += go;
            }
        }

        TabPage BuildBansTab()
        {
            var page = new TabPage("Actieve bans");
            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            var lbl = new Label { Text = "Filter", AutoSize = true, Left = 8, Top = 12 };
            txtBanFilter.SetBounds(50, 8, 220, 26);
            txtBanFilter.TextChanged += (s, e) => ApplyBanFilter();
            var btnRef = MakeButton("Vernieuwen", 280, 7, 100);
            btnRef.Click += (s, e) => RefreshBans(true);
            var btnUnban = MakeButton("Geselecteerde unbannen", 388, 7, 170);
            btnUnban.Click += (s, e) => UnbanSelected(lvBans);
            var btnWl = MakeButton("Naar whitelist + unban", 566, 7, 170);
            btnWl.Click += (s, e) => MoveSelectedToWhitelist(lvBans);
            var btnExport = MakeButton("Exporteer CSV", 744, 7, 110);
            btnExport.Click += (s, e) => ExportBans();
            var btnPerm = MakeButton("Permanente blacklist", 862, 7, 150);
            btnPerm.Click += (s, e) => PermanentBlacklistSelected(lvBans);
            var btnHist = MakeButton("Log van dit IP", 1020, 7, 120);
            btnHist.Click += (s, e) => ShowHistoryFromList(lvBans);
            top.Height = 44;
            top.Controls.AddRange(new Control[] { lbl, txtBanFilter, btnRef, btnUnban, btnWl, btnExport, btnPerm, btnHist });

            StyleListView(lvBans);
            lvBans.Dock = DockStyle.Fill;
            lvBans.Columns.Add("IP", 130);
            lvBans.Columns.Add("Status", 120);
            lvBans.Columns.Add("Mislukte logins", 90);
            lvBans.Columns.Add("Keren geband", 80);
            lvBans.Columns.Add("Laatste mislukte (UTC)", 140);
            lvBans.Columns.Add("Ban sinds (UTC)", 140);
            lvBans.Columns.Add("Opmerking", 280);
            lvBans.DoubleClick += (s, e) => ShowHistoryFromList(lvBans);
            lvBans.FullRowSelect = true;
            lvBans.MultiSelect = true;
            lvBans.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C) CopySelectedIps(lvBans);
            };

            var tip = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                Text = "  Tijdelijke bans (firewall). Bij herhaalde bans → Permanente blacklist. Dubbelklik of «Log van dit IP» voor alle historie.",
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleLeft
            };

            page.Controls.Add(lvBans);
            page.Controls.Add(tip);
            page.Controls.Add(top);
            return page;
        }

        TabPage BuildFailedTab()
        {
            var page = new TabPage("Pogingen");
            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            var lbl = new Label { Text = "Filter", AutoSize = true, Left = 8, Top = 12 };
            txtFailedFilter.SetBounds(50, 8, 220, 26);
            txtFailedFilter.TextChanged += (s, e) => ApplyFailedFilter();
            var btnRef = MakeButton("Vernieuwen", 280, 7, 100);
            btnRef.Click += (s, e) => RefreshFailed(true);
            var btnBan = MakeButton("Nu bannen", 388, 7, 120);
            btnBan.Click += (s, e) => BanSelected(lvFailed);
            var btnWl = MakeButton("Naar whitelist", 516, 7, 130);
            btnWl.Click += (s, e) => MoveSelectedToWhitelist(lvFailed);
            var btnHistFail = MakeButton("Log van dit IP", 654, 7, 120);
            btnHistFail.Click += (s, e) => ShowHistoryFromList(lvFailed);
            top.Controls.AddRange(new Control[] { lbl, txtFailedFilter, btnRef, btnBan, btnWl, btnHistFail });

            StyleListView(lvFailed);
            lvFailed.Dock = DockStyle.Fill;
            lvFailed.Columns.Add("IP", 180);
            lvFailed.Columns.Add("Pogingen", 100);
            lvFailed.Columns.Add("Laatste mislukte (UTC)", 200);
            lvFailed.Columns.Add("Status", 160);

            var tip = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Text = "  IP’s die al mislukte logins hebben maar nog niet geband zijn (onder de drempel).",
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleLeft
            };

            page.Controls.Add(lvFailed);
            page.Controls.Add(tip);
            page.Controls.Add(top);
            return page;
        }

        TabPage BuildWhitelistTab()
        {
            var page = new TabPage("Whitelist");
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Padding = new Padding(12, 8, 12, 8)
            };

            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var leftTop = new Panel { Dock = DockStyle.Top, Height = 48 };
            var title = new Label
            {
                Text = "Whitelist — nooit bannen",
                AutoSize = true,
                Left = 0,
                Top = 4,
                Font = new Font(Font, FontStyle.Bold)
            };
            lblWhiteCount.AutoSize = true;
            lblWhiteCount.Left = 0;
            lblWhiteCount.Top = 26;
            lblWhiteCount.ForeColor = Muted;
            lblWhiteCount.Text = "0 items — leeg is normaal";
            leftTop.Controls.AddRange(new Control[] { title, lblWhiteCount });

            var leftTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                WrapContents = false
            };
            txtWhiteFilter.Width = 220;
            txtWhiteFilter.Margin = new Padding(0, 4, 8, 0);
            SetCue(txtWhiteFilter, "Zoeken…");
            txtWhiteFilter.TextChanged += (s, e) => ApplyListFilter(true);
            var btnDel = MakeButton("Verwijderen", 0, 0, 100);
            btnDel.Click += (s, e) => { RemoveFromList(_whiteAll, lstWhite, txtWhiteFilter); MarkDirty(); RefreshWhitelistUi(); };
            var btnCopy = MakeButton("Kopieer", 0, 0, 80);
            btnCopy.Click += (s, e) =>
            {
                if (_whiteAll.Count == 0) return;
                Clipboard.SetText(string.Join(Environment.NewLine, _whiteAll));
                SetStatus("Whitelist gekopieerd (" + _whiteAll.Count + ")");
            };
            leftTools.Controls.AddRange(new Control[] { txtWhiteFilter, btnDel, btnCopy });

            lstWhite.Dock = DockStyle.Fill;
            lstWhite.IntegralHeight = false;
            lstWhite.SelectionMode = SelectionMode.MultiExtended;
            lstWhite.Font = new Font("Consolas", 10f);
            lstWhite.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    RemoveFromList(_whiteAll, lstWhite, txtWhiteFilter);
                    MarkDirty();
                    RefreshWhitelistUi();
                }
            };

            var emptyHint = new Label
            {
                Name = "lblWhiteEmpty",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Muted,
                Padding = new Padding(24),
                Text =
                    "Nog geen whitelist-items in ipban.config.\n\n" +
                    "Dat is normaal. RDP werkt zolang jouw IP niet geband is —\n" +
                    "whitelist is alleen een permanente uitzondering.\n\n" +
                    "Rechts: Mijn IP → Opslaan → service herstarten."
            };

            var leftAdv = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            chkWhiteAdvanced.Text = "Regex";
            chkWhiteAdvanced.AutoSize = true;
            chkWhiteAdvanced.Left = 0;
            chkWhiteAdvanced.Top = 4;
            txtWhiteRegex.SetBounds(70, 2, 320, 24);
            txtWhiteRegex.Visible = false;
            SetCue(txtWhiteRegex, "WhitelistRegex (optioneel)");
            txtWhiteRegex.TextChanged += (s, e) => MarkDirty();
            chkWhiteAdvanced.CheckedChanged += (s, e) =>
            {
                txtWhiteRegex.Visible = chkWhiteAdvanced.Checked;
            };
            leftAdv.Controls.AddRange(new Control[] { chkWhiteAdvanced, txtWhiteRegex });

            var listHost = new Panel { Dock = DockStyle.Fill };
            listHost.Controls.Add(lstWhite);
            listHost.Controls.Add(emptyHint);
            emptyHint.BringToFront();

            left.Controls.Add(listHost);
            left.Controls.Add(leftTools);
            left.Controls.Add(leftTop);
            left.Controls.Add(leftAdv);

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 4, 4), BackColor = Color.FromArgb(240, 253, 250) };
            var rTitle = new Label
            {
                Text = "Toevoegen",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Accent
            };
            var rEx = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                ForeColor = Muted,
                Text = "Plak IP’s (één per regel of komma). Dit is géén firewall-banlijst."
            };
            txtWhiteBulk.Multiline = true;
            txtWhiteBulk.Dock = DockStyle.Fill;
            txtWhiteBulk.ScrollBars = ScrollBars.Vertical;
            txtWhiteBulk.Font = new Font("Consolas", 11f);
            txtWhiteBulk.AcceptsReturn = true;
            txtWhiteBulk.TextChanged += (s, e) => UpdateWhitePastePreview();

            var rBottom = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.FromArgb(240, 253, 250) };
            lblWhitePasteStatus.SetBounds(0, 4, 440, 20);
            lblWhitePasteStatus.ForeColor = Muted;
            lblWhitePasteStatus.Text = "Tip: klik Mijn IP om je RDP-verbinding te beschermen.";
            var btnAdd = MakeButton("Toevoegen", 0, 30, 110);
            btnAdd.BackColor = Accent;
            btnAdd.ForeColor = Color.White;
            btnAdd.FlatStyle = FlatStyle.Flat;
            btnAdd.Click += (s, e) => AddBulkToWhitelist();
            var btnClip = MakeButton("Plakken", 118, 30, 90);
            btnClip.Click += (s, e) =>
            {
                if (!Clipboard.ContainsText()) return;
                txtWhiteBulk.AppendText(Clipboard.GetText());
                UpdateWhitePastePreview();
            };
            var btnMy = MakeButton("Mijn IP", 216, 30, 90);
            btnMy.BackColor = Accent;
            btnMy.ForeColor = Color.White;
            btnMy.FlatStyle = FlatStyle.Flat;
            btnMy.Click += async (s, e) => await WhitelistMyIpAsync();
            rBottom.Controls.AddRange(new Control[] { lblWhitePasteStatus, btnAdd, btnClip, btnMy });

            right.Controls.Add(txtWhiteBulk);
            right.Controls.Add(rBottom);
            right.Controls.Add(rEx);
            right.Controls.Add(rTitle);

            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            page.Controls.Add(split);
            page.Resize += (s, e) => SafeSplitterDistance(split, 0.48);
            return page;
        }

        TabPage BuildBlacklistTab()
        {
            var page = new TabPage("Blacklist");
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

            var title = new Label
            {
                Text = "Blacklist — nooit meer toelaten (ipban.config + opmerking)",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Muted
            };

            txtBlackFilter.Dock = DockStyle.Top;
            txtBlackFilter.Height = 26;
            SetCue(txtBlackFilter, "Zoeken…");
            txtBlackFilter.TextChanged += (s, e) => ApplyListFilter(false);

            StyleListView(lvBlack);
            lvBlack.Dock = DockStyle.Fill;
            lvBlack.Columns.Add("IP", 160);
            lvBlack.Columns.Add("Sinds", 130);
            lvBlack.Columns.Add("Keren geband", 90);
            lvBlack.Columns.Add("Opmerking (wanneer / waarom)", 520);
            lvBlack.FullRowSelect = true;
            lvBlack.MultiSelect = true;
            lvBlack.DoubleClick += (s, e) => ShowHistoryFromList(lvBlack);
            lvBlack.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Delete)
                {
                    RemoveSelectedBlack();
                    ev.Handled = true;
                }
            };

            lstBlack.Visible = false;
            lstBlack.Height = 0;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 100 };
            txtNewBlack.SetBounds(0, 6, 220, 26);
            SetCue(txtNewBlack, "IP / CIDR / host / URL");
            var add = MakeButton("Toevoegen", 228, 5, 90);
            add.Click += (s, e) =>
            {
                AddToList(_blackAll, lstBlack, txtNewBlack, txtBlackFilter);
                MarkDirty();
                RefreshBlacklistView();
            };
            var del = MakeButton("Verwijderen", 324, 5, 100);
            del.Click += (s, e) => RemoveSelectedBlack();
            var paste = MakeButton("Plakken (bulk)", 430, 5, 110);
            paste.Click += (s, e) => { BulkPaste(false); RefreshBlacklistView(); };
            var btnNote = MakeButton("Opmerking…", 548, 5, 110);
            btnNote.Click += (s, e) => EditBlacklistNote();
            var btnLog = MakeButton("Log van dit IP", 666, 5, 120);
            btnLog.Click += (s, e) => ShowHistoryFromList(lvBlack);
            txtNewBlack.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Enter)
                {
                    AddToList(_blackAll, lstBlack, txtNewBlack, txtBlackFilter);
                    MarkDirty();
                    RefreshBlacklistView();
                    ev.SuppressKeyPress = true;
                }
            };

            var lblRx = new Label
            {
                Text = "BlacklistRegex",
                Left = 0,
                Top = 42,
                AutoSize = true,
                ForeColor = Muted
            };
            txtBlackRegex.SetBounds(0, 62, 540, 26);
            txtBlackRegex.TextChanged += (s, e) => MarkDirty();
            bottom.Controls.AddRange(new Control[]
            {
                txtNewBlack, add, del, paste, btnNote, btnLog, lblRx, txtBlackRegex
            });

            wrap.Controls.Add(lvBlack);
            wrap.Controls.Add(bottom);
            wrap.Controls.Add(txtBlackFilter);
            wrap.Controls.Add(title);

            var tip = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                Text =
                    "  Vaste blokkade in ipban.config. Bij 3e ban-episode automatisch hier + opmerking (datum, reden, hoe vaak).\n" +
                    "  «Log van dit IP» toont alle historie uit logfile.txt. Opslaan + IPBan herstarten om actief te maken.",
                ForeColor = Color.FromArgb(180, 83, 9)
            };
            page.Controls.Add(wrap);
            page.Controls.Add(tip);
            return page;
        }

        void RefreshWhitelistUi()
        {
            ApplyListFilter(true);
            lblWhiteCount.Text = _whiteAll.Count == 0
                ? "0 items — leeg is normaal (RDP ≠ whitelist)"
                : _whiteAll.Count + " item(s) op whitelist";
            dashWhite.Text = _whiteAll.Count.ToString();
            var rx = GetCueText(txtWhiteRegex).Trim();
            if (rx.Length > 0) chkWhiteAdvanced.Checked = true;

            if (lstWhite.Parent != null)
            {
                foreach (Control c in lstWhite.Parent.Controls)
                {
                    if (c.Name == "lblWhiteEmpty")
                    {
                        c.Visible = _whiteAll.Count == 0;
                        break;
                    }
                }
            }
            lstWhite.Visible = _whiteAll.Count > 0;

            UpdateWhitePastePreview();
            FixSplitters();
        }

        void UpdateWhitePastePreview()
        {
            var entries = ParseBulkEntries(txtWhiteBulk.Text);
            if (entries.Count == 0)
            {
                lblWhitePasteStatus.Text = "Nog niets geplakt — plak regels of een komma-lijst.";
                return;
            }
            var newOnes = entries.Count(e =>
                !_whiteAll.Any(x => string.Equals(x, e, StringComparison.OrdinalIgnoreCase)));
            lblWhitePasteStatus.Text = entries.Count + " herkend, waarvan " + newOnes + " nieuw (rest staat al op de lijst).";
        }

        void AddBulkToWhitelist()
        {
            var entries = ParseBulkEntries(txtWhiteBulk.Text);
            if (entries.Count == 0)
            {
                MessageBox.Show(this,
                    "Plak eerst IP’s in het tekstvak.\n\nVoorbeeld:\n1.2.3.4\n5.6.7.8\n\nof:\n1.2.3.4, 5.6.7.8",
                    "Whitelist", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var added = 0;
            var skipped = 0;
            foreach (var item in entries)
            {
                if (_whiteAll.Any(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }
                _whiteAll.Add(item);
                added++;
            }
            _whiteAll.Sort(StringComparer.OrdinalIgnoreCase);
            MarkDirty();
            txtWhiteBulk.Clear();
            RefreshWhitelistUi();
            SetStatus(added + " toegevoegd aan whitelist" + (skipped > 0 ? ", " + skipped + " overgeslagen" : "") + " — nog Opslaan bovenin");
        }

        static List<string> ParseBulkEntries(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            // Ondersteunt: newlines, komma, puntkomma, spaties rondom
            return IpBanConfig.SplitList(text.Replace('\t', ' ')).ToList();
        }

        Control MakeEditableList(
            string title, ListBox list, TextBox input, TextBox filter, TextBox regexBox,
            Action onAdd, Action onRemove, bool isWhite)
        {
            var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            var lbl = new Label { Text = title, Dock = DockStyle.Top, Height = 22, ForeColor = Muted };

            filter.Dock = DockStyle.Top;
            filter.Height = 26;
            SetCue(filter, "Zoeken…");
            filter.TextChanged += (s, e) => ApplyListFilter(isWhite);

            list.Dock = DockStyle.Fill;
            list.IntegralHeight = false;
            list.SelectionMode = SelectionMode.MultiExtended;

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 96 };
            input.SetBounds(0, 6, 220, 26);
            SetCue(input, "IP / CIDR / host / URL");
            var add = MakeButton("Toevoegen", 228, 5, 90);
            add.Click += (s, e) => onAdd();
            var del = MakeButton("Verwijderen", 324, 5, 100);
            del.Click += (s, e) => onRemove();
            var paste = MakeButton("Plakken (bulk)", 430, 5, 110);
            paste.Click += (s, e) => BulkPaste(isWhite);

            var lblRx = new Label
            {
                Text = isWhite ? "WhitelistRegex" : "BlacklistRegex",
                Left = 0,
                Top = 42,
                AutoSize = true,
                ForeColor = Muted
            };
            regexBox.SetBounds(0, 62, 540, 26);
            regexBox.TextChanged += (s, e) => MarkDirty();

            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { onAdd(); e.SuppressKeyPress = true; }
            };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) onRemove();
            };

            bottom.Controls.AddRange(new Control[] { input, add, del, paste, lblRx, regexBox });
            p.Controls.Add(list);
            p.Controls.Add(bottom);
            p.Controls.Add(filter);
            p.Controls.Add(lbl);
            return p;
        }

        TabPage BuildSettingsTab()
        {
            var page = new TabPage("Instellingen");

            var autoPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 148,
                Padding = new Padding(12, 10, 12, 8),
                BackColor = Card
            };
            var title = new Label
            {
                Text = "Applicatie — autostart & auto-blacklist",
                Left = 12,
                Top = 6,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold)
            };
            chkAutoStart.Text = "enabled — Windows-service starten bij systeemstart (ook zonder ingelogde gebruiker)";
            chkAutoStart.AutoSize = true;
            chkAutoStart.Left = 12;
            chkAutoStart.Top = 30;
            chkGuiAtLogon.Text = "GUI in systeemvak na login (default: ja — aanbevolen; service heeft geen desktop)";
            chkGuiAtLogon.AutoSize = true;
            chkGuiAtLogon.Left = 12;
            chkGuiAtLogon.Top = 52;
            chkGuiAtLogon.Checked = true;
            chkAutoBlacklist.Text = "Na 3e ban niet meer toelaten: blacklist + opmerking (wanneer/waarom/hoe vaak)";
            chkAutoBlacklist.AutoSize = true;
            chkAutoBlacklist.Left = 12;
            chkAutoBlacklist.Top = 74;
            chkAutoBlacklist.Checked = true;
            chkAutoBlacklist.CheckedChanged += (s, e) =>
            {
                if (_loadingAppSettings) return;
                _autoBlacklistAfter = chkAutoBlacklist.Checked ? 3 : 0;
                try
                {
                    var prefs = AppSettings.Load();
                    prefs.AutoBlacklistAfterBans = _autoBlacklistAfter;
                    prefs.InstallDir = InstallDir;
                    prefs.Save();
                    SetStatus("Auto-blacklist: " + (_autoBlacklistAfter > 0 ? "aan (na 3 bans)" : "uit"));
                }
                catch { /* ignore */ }
            };
            lblAutoStartStatus.SetBounds(12, 100, 760, 36);
            lblAutoStartStatus.ForeColor = Muted;
            var btnApply = MakeButton("Toepassen", 880, 40, 100);
            btnApply.Click += (s, e) => ApplyAutoStartSetting();
            var btnLog = MakeButton("Service-log", 880, 74, 100);
            btnLog.Click += (s, e) =>
            {
                if (File.Exists(AppSettings.LogPath)) OpenFile(AppSettings.LogPath);
                else MessageBox.Show(this, "Nog geen service.log.\n" + AppSettings.LogPath, "Log",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            autoPanel.Controls.AddRange(new Control[]
            {
                title, chkAutoStart, chkGuiAtLogon, chkAutoBlacklist, lblAutoStartStatus, btnApply, btnLog
            });

            var dataPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 86,
                Padding = new Padding(12, 8, 12, 6),
                BackColor = Card
            };
            var dataTitle = new Label
            {
                Text = "Gegevens — %ProgramData%\\IPBanFrontend  (wekelijkse backup, zip, grafieken)",
                Left = 12,
                Top = 6,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold)
            };
            lblBackupStatus.SetBounds(12, 28, 760, 20);
            lblBackupStatus.ForeColor = Muted;
            lblBackupStatus.Text = "Backup: —";
            var btnZipData = MakeButton("Zip downloaden…", 12, 50, 130);
            btnZipData.Click += (s, e) => ExportFrontendZip();
            var btnBackupNow = MakeButton("Nu backup maken", 150, 50, 130);
            btnBackupNow.Click += (s, e) => MakeFrontendBackup(true);
            var btnBackupFolder = MakeButton("Backups-map", 288, 50, 110);
            btnBackupFolder.Click += (s, e) =>
            {
                Directory.CreateDirectory(FrontendData.BackupDir);
                OpenFolder(FrontendData.BackupDir);
            };
            var btnCharts = MakeButton("Grafieken exporteren…", 406, 50, 160);
            btnCharts.Click += (s, e) => ExportAllCharts();
            dataPanel.Controls.AddRange(new Control[]
            {
                dataTitle, lblBackupStatus, btnZipData, btnBackupNow, btnBackupFolder, btnCharts
            });

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 280,
                Padding = new Padding(8)
            };

            gridSettings.Dock = DockStyle.Fill;
            gridSettings.AllowUserToAddRows = false;
            gridSettings.AllowUserToDeleteRows = false;
            gridSettings.RowHeadersVisible = false;
            gridSettings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridSettings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridSettings.BackgroundColor = Card;
            gridSettings.BorderStyle = BorderStyle.None;
            gridSettings.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Key",
                HeaderText = "Sleutel",
                ReadOnly = true,
                FillWeight = 40
            });
            gridSettings.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Value",
                HeaderText = "Waarde",
                FillWeight = 35
            });
            gridSettings.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Hint",
                HeaderText = "Uitleg",
                ReadOnly = true,
                FillWeight = 55
            });
            gridSettings.CellValueChanged += (s, e) =>
            {
                if (e.ColumnIndex == 1) MarkDirty();
            };
            gridSettings.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (gridSettings.IsCurrentCellDirty) gridSettings.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            var lower = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0, 8, 0, 0)
            };
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            lower.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            lower.Controls.Add(new Label { Text = "UserNameWhitelist (komma)", Dock = DockStyle.Fill, ForeColor = Muted }, 0, 0);
            lower.Controls.Add(new Label { Text = "FirewallRules / FirewallUriRules", Dock = DockStyle.Fill, ForeColor = Muted }, 1, 0);
            txtUserNames.Multiline = true;
            txtUserNames.Dock = DockStyle.Fill;
            txtUserNames.ScrollBars = ScrollBars.Vertical;
            txtUserNames.TextChanged += (s, e) => MarkDirty();

            var rulesSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 280 };
            txtFirewallRules.Multiline = true;
            txtFirewallRules.Dock = DockStyle.Fill;
            txtFirewallRules.ScrollBars = ScrollBars.Both;
            txtFirewallRules.Font = new Font("Consolas", 8.5f);
            txtFirewallRules.WordWrap = false;
            txtFirewallRules.TextChanged += (s, e) => MarkDirty();
            txtFirewallUriRules.Multiline = true;
            txtFirewallUriRules.Dock = DockStyle.Fill;
            txtFirewallUriRules.ScrollBars = ScrollBars.Both;
            txtFirewallUriRules.Font = new Font("Consolas", 8.5f);
            txtFirewallUriRules.WordWrap = false;
            txtFirewallUriRules.TextChanged += (s, e) => MarkDirty();

            var l1 = new Label { Text = "FirewallRules", Dock = DockStyle.Top, Height = 18, ForeColor = Muted };
            var l2 = new Label { Text = "FirewallUriRules (externe blocklists)", Dock = DockStyle.Top, Height = 18, ForeColor = Muted };
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(txtFirewallRules);
            left.Controls.Add(l1);
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(txtFirewallUriRules);
            right.Controls.Add(l2);
            rulesSplit.Panel1.Controls.Add(left);
            rulesSplit.Panel2.Controls.Add(right);

            lower.Controls.Add(txtUserNames, 0, 1);
            lower.Controls.Add(rulesSplit, 1, 1);

            split.Panel1.Controls.Add(gridSettings);
            split.Panel2.Controls.Add(lower);
            page.Controls.Add(split);
            page.Controls.Add(dataPanel);
            page.Controls.Add(autoPanel);
            return page;
        }

        TabPage BuildFirewallTab()
        {
            var page = new TabPage("Firewall");

            var banner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110,
                Padding = new Padding(12, 10, 12, 8),
                BackColor = Color.FromArgb(240, 253, 250)
            };
            lblFwStatus.Dock = DockStyle.Top;
            lblFwStatus.Height = 48;
            lblFwStatus.Font = new Font("Segoe UI Semibold", 10f);
            lblFwStatus.Text = "Firewall-status wordt geladen…";

            lnkOpenFwAdvanced.Text = "Open Windows Firewall met geavanceerde beveiliging (wf.msc) — filter/zoek op IPBan_";
            lnkOpenFwAdvanced.AutoSize = true;
            lnkOpenFwAdvanced.Left = 12;
            lnkOpenFwAdvanced.Top = 58;
            lnkOpenFwAdvanced.LinkColor = Accent;
            lnkOpenFwAdvanced.LinkClicked += (s, e) => FirewallHelper.OpenWindowsFirewallAdvanced();

            lnkOpenFwBasic.Text = "Open Windows Defender Firewall (firewall.cpl)";
            lnkOpenFwBasic.AutoSize = true;
            lnkOpenFwBasic.Left = 12;
            lnkOpenFwBasic.Top = 80;
            lnkOpenFwBasic.LinkColor = Accent;
            lnkOpenFwBasic.LinkClicked += (s, e) => FirewallHelper.OpenWindowsFirewallBasic();

            banner.Controls.Add(lnkOpenFwBasic);
            banner.Controls.Add(lnkOpenFwAdvanced);
            banner.Controls.Add(lblFwStatus);

            var top = new Panel { Dock = DockStyle.Top, Height = 44 };
            var btn = MakeButton("Vernieuwen", 12, 8, 120);
            btn.Click += (s, e) => RefreshFirewall();
            var tip = new Label
            {
                Left = 140,
                Top = 12,
                AutoSize = true,
                Text = "Regels met prefix " + (_firewallPrefix ?? "IPBan_") + " — kolom Status = actief/uit in Windows Firewall.",
                ForeColor = Muted
            };
            top.Controls.AddRange(new Control[] { btn, tip });

            StyleListView(lvFw);
            lvFw.Dock = DockStyle.Fill;
            lvFw.Columns.Add("Regel", 200);
            lvFw.Columns.Add("Status", 70);
            lvFw.Columns.Add("Actie", 80);
            lvFw.Columns.Add("# adressen", 90);
            lvFw.Columns.Add("Remote IP’s (samenvatting)", 520);
            lvFw.FullRowSelect = true;

            page.Controls.Add(lvFw);
            page.Controls.Add(top);
            page.Controls.Add(banner);
            return page;
        }

        TabPage BuildLogTab()
        {
            var page = new TabPage("Log");
            var top = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
            var lbl = new Label { Text = "Filter", AutoSize = true, Left = 8, Top = 12 };
            txtLogFilter.SetBounds(50, 8, 200, 26);
            cmbLogLevel.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLogLevel.Items.AddRange(new object[] { "Alles", "Ban/Unban", "Failed", "Success", "ERROR/WARN" });
            cmbLogLevel.SelectedIndex = 0;
            cmbLogLevel.SetBounds(260, 8, 120, 26);
            chkLogPause.Text = "Pauze";
            chkLogPause.AutoSize = true;
            chkLogPause.Left = 400;
            chkLogPause.Top = 10;
            var btnClear = MakeButton("Weergave wissen", 470, 7, 130);
            btnClear.Click += (s, e) => { txtLog.Clear(); };
            var btnOpen = MakeButton("logfile.txt openen", 608, 7, 140);
            btnOpen.Click += (s, e) => OpenFile(LogPath);
            var btnJump = MakeButton("Naar einde", 756, 7, 100);
            btnJump.Click += (s, e) =>
            {
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            };
            top.Controls.AddRange(new Control[] { lbl, txtLogFilter, cmbLogLevel, chkLogPause, btnClear, btnOpen, btnJump });

            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Both;
            txtLog.WordWrap = false;
            txtLog.Dock = DockStyle.Fill;
            txtLog.Font = new Font("Consolas", 8.5f);
            txtLog.BackColor = Color.FromArgb(24, 28, 32);
            txtLog.ForeColor = Color.FromArgb(210, 230, 210);

            page.Controls.Add(txtLog);
            page.Controls.Add(top);
            return page;
        }

        static void StyleListView(ListView lv)
        {
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = true;
            lv.HideSelection = false;
            lv.BackColor = Card;
        }

        // ───────── data / actions ─────────

        void PersistInstallDir()
        {
            try
            {
                var s = AppSettings.Load();
                s.InstallDir = InstallDir;
                s.Save();
            }
            catch { /* ignore */ }
        }

        void FullReload()
        {
            PersistInstallDir();
            ReloadConfig();
            RefreshService();
            RefreshLog(true);
            RefreshDashboard(true);
            RefreshBans(true);
            RefreshFailed(true);
            SetDirty(false);
        }

        void BrowseFolder()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.SelectedPath = Directory.Exists(InstallDir) ? InstallDir : @"C:\Program Files";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    txtInstallDir.Text = d.SelectedPath;
                    PersistInstallDir();
                    FullReload();
                }
            }
        }

        void ReloadConfig()
        {
            _whiteAll.Clear();
            _blackAll.Clear();
            lstWhite.Items.Clear();
            lstBlack.Items.Clear();
            lvBlack.Items.Clear();
            gridSettings.Rows.Clear();
            txtUserNames.Text = "";
            txtFirewallRules.Text = "";
            txtFirewallUriRules.Text = "";
            txtWhiteRegex.Text = "";
            txtBlackRegex.Text = "";

            if (!File.Exists(ConfigPath))
            {
                SetStatus("ipban.config niet gevonden in " + InstallDir);
                return;
            }

            try
            {
                var doc = IpBanConfig.Load(ConfigPath);
                _whiteAll.AddRange(IpBanConfig.ReadList(doc, "Whitelist"));
                _blackAll.AddRange(IpBanConfig.ReadList(doc, "Blacklist"));
                ApplyListFilter(true);
                ApplyListFilter(false);

                txtWhiteRegex.Text = IpBanConfig.ReadValue(doc, "WhitelistRegex") ?? "";
                txtWhiteRegex.ForeColor = SystemColors.WindowText;
                if (!string.IsNullOrWhiteSpace(txtWhiteRegex.Text))
                    chkWhiteAdvanced.Checked = true;
                txtBlackRegex.Text = IpBanConfig.ReadValue(doc, "BlacklistRegex") ?? "";
                txtUserNames.Text = IpBanConfig.ReadValue(doc, "UserNameWhitelist") ?? "";
                txtFirewallRules.Text = NormalizeMultiline(IpBanConfig.ReadValue(doc, "FirewallRules"));
                txtFirewallUriRules.Text = NormalizeMultiline(IpBanConfig.ReadValue(doc, "FirewallUriRules"));
                _firewallPrefix = IpBanConfig.ReadValue(doc, "FirewallRulePrefix") ?? "IPBan_";

                foreach (var key in IpBanConfig.EditableKeys)
                {
                    if (key == "UserNameWhitelist" || key == "FirewallRules" || key == "FirewallUriRules"
                        || key == "WhitelistRegex" || key == "BlacklistRegex")
                        continue;
                    var val = IpBanConfig.ReadValue(doc, key) ?? "";
                    string hint;
                    IpBanConfig.KeyHints.TryGetValue(key, out hint);
                    gridSettings.Rows.Add(key, val, hint ?? "");
                }

                SetStatus("Config geladen: " + ConfigPath);
                SetDirty(false);
                RefreshWhitelistUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Config lezen mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string NormalizeMultiline(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }

        void SaveConfig(bool askRestart)
        {
            if (!File.Exists(ConfigPath))
            {
                MessageBox.Show(this, "ipban.config niet gevonden.", "Opslaan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var doc = IpBanConfig.Load(ConfigPath);
                IpBanConfig.WriteList(doc, "Whitelist", _whiteAll);
                IpBanConfig.WriteList(doc, "Blacklist", _blackAll);
                IpBanConfig.WriteValue(doc, "WhitelistRegex", GetCueText(txtWhiteRegex).Trim());
                IpBanConfig.WriteValue(doc, "BlacklistRegex", GetCueText(txtBlackRegex).Trim());
                IpBanConfig.WriteValue(doc, "UserNameWhitelist", txtUserNames.Text.Trim());
                IpBanConfig.WriteValue(doc, "FirewallRules", txtFirewallRules.Text.Replace("\r\n", "\n"));
                IpBanConfig.WriteValue(doc, "FirewallUriRules", txtFirewallUriRules.Text.Replace("\r\n", "\n"));

                foreach (DataGridViewRow row in gridSettings.Rows)
                {
                    if (row.IsNewRow) continue;
                    var key = Convert.ToString(row.Cells[0].Value);
                    var val = Convert.ToString(row.Cells[1].Value) ?? "";
                    if (!string.IsNullOrEmpty(key))
                        IpBanConfig.WriteValue(doc, key, val);
                }

                var backup = IpBanConfig.BackupAndSave(ConfigPath, doc);
                _firewallPrefix = IpBanConfig.ReadValue(doc, "FirewallRulePrefix") ?? "IPBan_";
                SetDirty(false);
                SetStatus("Opgeslagen. Backup: " + Path.GetFileName(backup));

                if (askRestart)
                {
                    var r = MessageBox.Show(this,
                        "Config opgeslagen.\nIPBan-service herstarten zodat alles actief wordt?",
                        "Herstarten?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) RestartService();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Opslaan mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void MarkDirty() => SetDirty(true);

        void SetDirty(bool dirty)
        {
            _dirty = dirty;
            lblDirty.Text = dirty ? "● Niet opgeslagen" : "";
        }

        void ApplyListFilter(bool white)
        {
            if (!white)
            {
                RefreshBlacklistView();
                return;
            }
            var src = _whiteAll;
            var lst = lstWhite;
            var q = GetCueText(txtWhiteFilter).Trim();
            lst.BeginUpdate();
            lst.Items.Clear();
            foreach (var item in src)
            {
                if (q.Length == 0 || item.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    lst.Items.Add(item);
            }
            lst.EndUpdate();
        }

        void RefreshBlacklistView()
        {
            if (_banHistory == null) _banHistory = BanHistoryStore.Load();
            var q = GetCueText(txtBlackFilter).Trim();
            var nl = CultureInfo.GetCultureInfo("nl-NL");
            lvBlack.BeginUpdate();
            lvBlack.Items.Clear();
            lstBlack.Items.Clear();
            foreach (var ip in _blackAll)
            {
                if (q.Length > 0 && ip.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                lstBlack.Items.Add(ip);
                var hist = _banHistory.Get(ip);
                var item = new ListViewItem(ip);
                var since = hist != null && hist.BlacklistedAtUtc.HasValue
                    ? hist.BlacklistedAtUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm", nl)
                    : (hist != null && hist.LastSeenUtc.HasValue
                        ? hist.LastSeenUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm", nl)
                        : "—");
                item.SubItems.Add(since);
                item.SubItems.Add(hist != null && hist.Count > 0 ? hist.Count.ToString() : "—");
                var note = hist != null && !string.IsNullOrWhiteSpace(hist.Note)
                    ? hist.Note
                    : "Geen opmerking — dubbelklik voor log, of «Opmerking…»";
                item.SubItems.Add(note);
                item.Tag = ip;
                lvBlack.Items.Add(item);
            }
            lvBlack.EndUpdate();
        }

        void RemoveSelectedBlack()
        {
            var sel = lvBlack.SelectedItems.Cast<ListViewItem>()
                .Select(i => i.Text)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            if (sel.Count == 0) return;
            _blackAll.RemoveAll(x => sel.Any(s => string.Equals(s, x, StringComparison.OrdinalIgnoreCase)));
            RefreshBlacklistView();
            MarkDirty();
            SetStatus("Verwijderd van blacklist (nog opslaan)");
        }

        void EditBlacklistNote()
        {
            if (lvBlack.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst een IP op de blacklist.", "Opmerking",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var ip = lvBlack.SelectedItems[0].Text;
            if (_banHistory == null) _banHistory = BanHistoryStore.Load();
            var current = _banHistory.GetNote(ip);
            using (var dlg = new Form())
            {
                dlg.Text = "Opmerking — " + ip;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(520, 220);
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                var tb = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Left = 12,
                    Top = 12,
                    Width = 496,
                    Height = 150,
                    Text = current
                };
                var ok = MakeButton("Opslaan", 328, 176, 90);
                var cancel = MakeButton("Annuleren", 426, 176, 82);
                ok.DialogResult = DialogResult.OK;
                cancel.DialogResult = DialogResult.Cancel;
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                dlg.Controls.AddRange(new Control[] { tb, ok, cancel });
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _banHistory.SetNote(ip, tb.Text.Trim());
                RefreshBlacklistView();
                SetStatus("Opmerking bewaard voor " + ip);
            }
        }

        void AddToList(List<string> all, ListBox list, TextBox box, TextBox filter)
        {
            var v = GetCueText(box).Trim();
            if (v.Length == 0) return;
            if (all.Any(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)))
            {
                SetStatus("Staat al in de lijst: " + v);
                return;
            }
            all.Add(v);
            all.Sort(StringComparer.OrdinalIgnoreCase);
            box.Clear();
            if (all == _blackAll)
            {
                if (_banHistory == null) _banHistory = BanHistoryStore.Load();
                _banHistory.MarkBlacklisted(v, null);
            }
            ApplyListFilter(list == lstWhite);
            SetStatus("Toegevoegd (nog opslaan): " + v);
        }

        void RemoveFromList(List<string> all, ListBox list, TextBox filter)
        {
            var sel = list.SelectedItems.Cast<object>().Select(o => o.ToString()).ToList();
            if (sel.Count == 0) return;
            all.RemoveAll(x => sel.Any(s => string.Equals(s, x, StringComparison.OrdinalIgnoreCase)));
            ApplyListFilter(list == lstWhite);
            SetStatus("Verwijderd (nog opslaan)");
        }

        void BulkPaste(bool white)
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "Klembord is leeg. Kopieer eerst IP’s (één per regel of komma).", "Plakken",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var all = white ? _whiteAll : _blackAll;
            var added = 0;
            foreach (var item in IpBanConfig.SplitList(text))
            {
                if (all.Any(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase))) continue;
                all.Add(item);
                added++;
            }
            all.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyListFilter(white);
            MarkDirty();
            SetStatus(added + " item(s) geplakt (nog opslaan)");
        }

        void RefreshDashboard(bool forceLogScan)
        {
            try
            {
                var counts = IpBanDatabase.Exists(InstallDir)
                    ? IpBanDatabase.Counts(InstallDir)
                    : (banned: 0, failed: 0, total: 0);
                dashFailed.Text = counts.failed.ToString();
                dashWhite.Text = _whiteAll.Count.ToString();
                dashBlack.Text = _blackAll.Count.ToString();

                try
                {
                    using (var sc = new ServiceController(ServiceName))
                        dashService.Text = ShortStatus(sc.Status);
                }
                catch { dashService.Text = "n.v.t."; }

                if (forceLogScan || _lastMonitor == null)
                    _lastMonitor = LogMonitor.Analyze(LogPath, _monitorPeriod);
                if (_lastMonitor != null)
                {
                    if (_banHistory == null) _banHistory = BanHistoryStore.Load();
                    _banHistory.ObserveLogEvents(_lastMonitor.Events);
                }

                var m = _lastMonitor ?? new MonitorPeriodStats { Period = _monitorPeriod };
                dashAttempts.Text = m.AttemptCount.ToString();
                dashLocations.Text = m.UniqueIps.ToString();
                dashPeriodBans.Text = m.BanCount.ToString();
                dashPeriodOk.Text = m.SuccessCount.ToString();

                // Firewall-status max 1× / 30s (netsh is zwaar); force op Firewall-tab via RefreshFirewall
                if (forceLogScan && (DateTime.UtcNow - _fwStatusAt).TotalSeconds > 30)
                    RefreshFirewallStatusOnly();
                else if (_lastFwStatus == null)
                    RefreshFirewallStatusOnly();
                UpdateFirewallDashboardCard();

                var bannedShown = counts.banned;
                if (bannedShown == 0 && _lastFwStatus != null)
                    bannedShown = FirewallHelper.GetBlockedIps(_lastFwStatus).Count;
                dashBanned.Text = bannedShown.ToString();

                // Kleur accent op pogingen-card via label
                dashAttempts.ForeColor = m.AttemptCount > 50 ? Color.FromArgb(153, 27, 27)
                    : m.AttemptCount > 10 ? Color.FromArgb(194, 65, 12)
                    : Color.FromArgb(15, 23, 42);
                dashLocations.ForeColor = m.UniqueIps > 20 ? Color.FromArgb(153, 27, 27)
                    : Color.FromArgb(15, 23, 42);

                if ((DateTime.UtcNow - _trendAt).TotalMinutes >= 5)
                    IngestTrend(false);

                var period = LogMonitor.PeriodLabel(m.Period);
                if (m.AttemptCount == 0 && m.Events.Count == 0)
                {
                    lblMonitorSummary.Text = "Geen relevante events in " + period + ".";
                    lblMonitorHint.Text = "Geen mislukte logins/bans in de logfile voor deze periode. " +
                                          "Draait IPBan? Staat logfile.txt in de IPBan-map? Probeer een langere periode.";
                }
                else
                {
                    lblMonitorSummary.Text = string.Format(
                        CultureInfo.GetCultureInfo("nl-NL"),
                        "{0}: {1:n0} pogingen (mislukt/ban) vanaf {2:n0} verschillende IP-locaties  ·  {3} bans  ·  {4} geslaagde logins",
                        char.ToUpper(period[0]) + period.Substring(1),
                        m.AttemptCount, m.UniqueIps, m.BanCount, m.SuccessCount);

                    if (m.TopIps.Count > 0)
                    {
                        var top = m.TopIps[0];
                        lblMonitorHint.Text = "Drukste bron: " + top.Key + " met " + top.Value +
                                              " events. Selecteer een regel voor wie/wat/waarom. Oranje = mislukt, rood = ban, groen = OK-login.";
                    }
                    else
                    {
                        lblMonitorHint.Text = "Selecteer een event voor heldere uitleg (wie, protocol, wat te doen).";
                    }
                }

                ApplyMonitorFilter();

                lvTopIps.BeginUpdate();
                lvTopIps.Items.Clear();
                var totalAtt = Math.Max(1, m.AttemptCount);
                foreach (var kv in m.TopIps)
                {
                    var item = new ListViewItem(kv.Key);
                    item.UseItemStyleForSubItems = false;
                    var heat = kv.Value >= 20 ? Color.FromArgb(254, 226, 226)
                        : kv.Value >= 5 ? Color.FromArgb(255, 237, 213)
                        : Color.FromArgb(240, 253, 250);
                    item.BackColor = heat;
                    item.SubItems.Add(kv.Value.ToString());
                    item.SubItems[1].BackColor = heat;
                    var pct = (100.0 * kv.Value / totalAtt);
                    item.SubItems.Add(pct.ToString("0") + "%");
                    item.SubItems[2].BackColor = heat;
                    item.Tag = kv.Key;
                    lvTopIps.Items.Add(item);
                }
                lvTopIps.EndUpdate();
            }
            catch (Exception ex)
            {
                SetStatus("Monitoring: " + ex.Message);
                lblMonitorSummary.Text = "Monitoring-fout";
                lblMonitorHint.Text = ex.Message;
            }
        }

        void SetupFilt(CheckBox chk, string text, bool on)
        {
            chk.Text = text;
            chk.AutoSize = true;
            chk.Checked = on;
            chk.Margin = new Padding(4, 8, 4, 0);
            chk.CheckedChanged += (s, e) => ApplyMonitorFilter();
        }

        void SetAllMonitorFilters(bool on)
        {
            chkFiltFailed.Checked = chkFiltBan.Checked = chkFiltWarn.Checked =
                chkFiltInfo.Checked = chkFiltOk.Checked = on;
            ApplyMonitorFilter();
        }

        bool PassesMonitorFilter(MonitorEvent ev)
        {
            if (ev == null) return false;
            switch (ev.Kind)
            {
                case MonitorKind.Failed: return chkFiltFailed.Checked;
                case MonitorKind.Ban:
                case MonitorKind.Unban: return chkFiltBan.Checked;
                case MonitorKind.Warn: return chkFiltWarn.Checked;
                case MonitorKind.Info:
                case MonitorKind.Error: return chkFiltInfo.Checked;
                case MonitorKind.Success: return chkFiltOk.Checked;
                default: return chkFiltInfo.Checked;
            }
        }

        void ApplyMonitorFilter()
        {
            var m = _lastMonitor;
            if (m == null) return;

            lvRecent.BeginUpdate();
            lvRecent.Items.Clear();
            foreach (var ev in m.Events)
            {
                if (!PassesMonitorFilter(ev)) continue;

                var item = new ListViewItem(ev.TimeLocal.HasValue
                    ? ev.TimeLocal.Value.ToString("dd-MM HH:mm:ss")
                    : "—");
                item.UseItemStyleForSubItems = false;
                item.BackColor = ev.RowColor;
                item.ForeColor = Color.FromArgb(30, 41, 59);
                item.SubItems.Add(ev.KindLabel);
                item.SubItems[1].BackColor = ev.RowColor;
                item.SubItems[1].ForeColor = ev.AccentColor;
                item.SubItems[1].Font = new Font(Font, FontStyle.Bold);
                item.SubItems.Add(ev.Ip ?? "—");
                item.SubItems[2].BackColor = ev.RowColor;
                item.SubItems.Add(string.IsNullOrEmpty(ev.User) ? "—" : ev.User);
                item.SubItems[3].BackColor = ev.RowColor;
                item.SubItems.Add(string.IsNullOrEmpty(ev.Source) ? "—" : ev.Source);
                item.SubItems[4].BackColor = ev.RowColor;
                var shortHint = ev.Title;
                if (!string.IsNullOrEmpty(ev.User)) shortHint += " · " + ev.User;
                if (!string.IsNullOrEmpty(ev.Source)) shortHint += " · " + ev.Source;
                item.SubItems.Add(shortHint);
                item.SubItems[5].BackColor = ev.RowColor;
                item.SubItems.Add(ev.Count.HasValue ? ev.Count.Value.ToString() : "");
                item.SubItems[6].BackColor = ev.RowColor;
                item.Tag = ev;
                lvRecent.Items.Add(item);
            }
            lvRecent.EndUpdate();
        }

        void ShowMonitorSelectionHint()
        {
            if (lvRecent.SelectedItems.Count == 0) return;
            var ev = lvRecent.SelectedItems[0].Tag as MonitorEvent;
            if (ev == null) return;
            lblMonitorSummary.Text = ev.KindLabel + "  ·  " + (ev.Ip ?? "geen IP") +
                                     (string.IsNullOrEmpty(ev.User) ? "" : "  ·  user «" + ev.User + "»") +
                                     (string.IsNullOrEmpty(ev.Source) ? "" : "  ·  " + ev.Source);
            lblMonitorHint.Text = ev.Hint;
            if (!string.IsNullOrEmpty(ev.Ip))
            {
                txtQuickIp.ForeColor = SystemColors.WindowText;
                txtQuickIp.Text = ev.Ip;
            }
        }

        List<string> GetSelectedMonitorIps()
        {
            var ips = lvRecent.SelectedItems.Cast<ListViewItem>()
                .Select(i => i.Tag as MonitorEvent)
                .Where(e => e != null && !string.IsNullOrEmpty(e.Ip))
                .Select(e => e.Ip)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ips.Count == 0 && lvTopIps.SelectedItems.Count > 0)
                ips.Add(lvTopIps.SelectedItems[0].Text);
            return ips;
        }

        void ShowMonitorEventDetail()
        {
            if (lvRecent.SelectedItems.Count == 0) return;
            var ev = lvRecent.SelectedItems[0].Tag as MonitorEvent;
            if (ev == null) return;
            MessageBox.Show(this,
                ev.Title + "\n\n" + ev.Hint + "\n\n—\n" + ev.Raw,
                "Event", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void WhitelistMonitorSelection()
        {
            var ips = GetSelectedMonitorIps();
            if (ips.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst een event of een IP in de top-lijst.", "Whitelist",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var added = 0;
            foreach (var ip in ips)
            {
                if (_whiteAll.Any(x => string.Equals(x, ip, StringComparison.OrdinalIgnoreCase))) continue;
                _whiteAll.Add(ip);
                added++;
            }
            _whiteAll.Sort(StringComparer.OrdinalIgnoreCase);
            RefreshWhitelistUi();
            MarkDirty();
            SetStatus(added + " naar whitelist (nog Opslaan) — " + string.Join(", ", ips));
            SelectTab("Whitelist");
        }

        void BanMonitorSelection()
        {
            var ips = GetSelectedMonitorIps();
            if (ips.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst event(s) of een IP in de top-lijst.", "Ban",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            WriteDropFile("ban.txt", string.Join(Environment.NewLine, ips));
        }

        void BlacklistMonitorSelection()
        {
            var ips = GetSelectedMonitorIps();
            if (ips.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst event(s) of een IP in de top-lijst.", "Blacklist",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PermanentBlacklistIps(ips);
        }

        void ShowHistoryForSelection()
        {
            string ip = null, user = null;
            if (lvRecent.SelectedItems.Count > 0)
            {
                var ev = lvRecent.SelectedItems[0].Tag as MonitorEvent;
                if (ev != null)
                {
                    ip = ev.Ip;
                    user = ev.User;
                }
            }
            if (string.IsNullOrEmpty(ip) && lvTopIps.SelectedItems.Count > 0)
                ip = lvTopIps.SelectedItems[0].Text;
            if (string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(GetCueText(txtQuickIp)))
                ip = GetCueText(txtQuickIp).Trim();
            if (string.IsNullOrEmpty(ip) && string.IsNullOrEmpty(user))
            {
                MessageBox.Show(this, "Selecteer eerst een event of IP.", "Historie",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowIpHistory(ip, user);
        }

        void ShowHistoryFromList(ListView lv)
        {
            if (lv.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst een IP.", "Historie",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ShowIpHistory(lv.SelectedItems[0].Text, null);
        }

        void PermanentBlacklistSelected(ListView lv)
        {
            var ips = SelectedIps(lv).ToList();
            if (ips.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst één of meer IP’s.", "Blacklist",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PermanentBlacklistIps(ips);
        }

        void PermanentBlacklistIps(IList<string> ips)
        {
            if (_banHistory == null) _banHistory = BanHistoryStore.Load();
            var added = 0;
            foreach (var ip in ips)
            {
                if (string.IsNullOrWhiteSpace(ip)) continue;
                if (_whiteAll.Any(x => string.Equals(x, ip, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!_blackAll.Any(x => string.Equals(x, ip, StringComparison.OrdinalIgnoreCase)))
                {
                    _blackAll.Add(ip);
                    added++;
                }
                _banHistory.MarkBlacklisted(ip, null);
            }
            if (added == 0 && ips.Count > 0)
            {
                SetStatus("Stonden al op blacklist. Opmerking bijgewerkt.");
                RefreshBlacklistView();
                return;
            }
            _blackAll.Sort(StringComparer.OrdinalIgnoreCase);
            RefreshBlacklistView();
            dashBlack.Text = _blackAll.Count.ToString();
            MarkDirty();
            WriteDropFile("ban.txt", string.Join(Environment.NewLine, ips));
            SetStatus(added + " op permanente blacklist (nog Opslaan) + ban.txt");
            SelectTab("Blacklist");
        }

        void ShowIpHistory(string ip, string user)
        {
            var events = LogMonitor.Search(LogPath, ip, user);
            if (_banHistory == null) _banHistory = BanHistoryStore.Load();
            _banHistory.ObserveLogEvents(events);
            var hist = !string.IsNullOrEmpty(ip) ? _banHistory.Get(ip) : null;
            var nl = CultureInfo.GetCultureInfo("nl-NL");

            using (var dlg = new Form())
            {
                dlg.Text = "Historie — " + (string.IsNullOrEmpty(ip) ? user : ip);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Size = new Size(920, 560);
                dlg.MinimumSize = new Size(720, 400);

                var summary = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 56,
                    Padding = new Padding(10, 8, 10, 4),
                    ForeColor = Color.FromArgb(51, 65, 85)
                };
                if (hist != null)
                    summary.Text = (hist.Note ?? "") +
                                   (string.IsNullOrWhiteSpace(hist.Note) ? "" : Environment.NewLine) +
                                   hist.Count + "× geband" +
                                   (hist.FailedPeak > 0 ? ", piek " + hist.FailedPeak + " mislukte logins" : "") +
                                   (hist.BlacklistedAtUtc.HasValue
                                       ? " · blacklist sinds " + hist.BlacklistedAtUtc.Value.ToLocalTime().ToString("dd-MM-yyyy HH:mm", nl)
                                       : "");
                else
                    summary.Text = "Nog geen blacklist-opmerking. Onderstaande regels komen uit logfile.txt.";

                var lv = new ListView { Dock = DockStyle.Fill };
                StyleListView(lv);
                lv.Columns.Add("Tijd", 130);
                lv.Columns.Add("Type", 80);
                lv.Columns.Add("Gebruiker", 140);
                lv.Columns.Add("Bron", 80);
                lv.Columns.Add("#", 40);
                lv.Columns.Add("Regel", 400);
                foreach (var ev in events)
                {
                    var row = new ListViewItem(ev.TimeLocal.HasValue
                        ? ev.TimeLocal.Value.ToString("dd-MM HH:mm:ss")
                        : "—");
                    row.UseItemStyleForSubItems = false;
                    row.BackColor = ev.RowColor;
                    row.SubItems.Add(ev.KindLabel);
                    row.SubItems[1].BackColor = ev.RowColor;
                    row.SubItems[1].ForeColor = ev.AccentColor;
                    row.SubItems.Add(string.IsNullOrEmpty(ev.User) ? "—" : ev.User);
                    row.SubItems[2].BackColor = ev.RowColor;
                    row.SubItems.Add(string.IsNullOrEmpty(ev.Source) ? "—" : ev.Source);
                    row.SubItems[3].BackColor = ev.RowColor;
                    row.SubItems.Add(ev.Count.HasValue ? ev.Count.Value.ToString() : "");
                    row.SubItems[4].BackColor = ev.RowColor;
                    row.SubItems.Add(ev.Raw ?? ev.Hint ?? "");
                    row.SubItems[5].BackColor = ev.RowColor;
                    lv.Items.Add(row);
                }

                var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
                var btnLog = MakeButton("Open in Log-tab", 12, 8, 140);
                btnLog.Click += (s, e) =>
                {
                    txtLogFilter.Text = !string.IsNullOrEmpty(ip) ? ip : user;
                    cmbLogLevel.SelectedIndex = 0;
                    dlg.Close();
                    SelectTab("Log");
                    RefreshLog(true);
                };
                var btnBlack = MakeButton("Permanente blacklist", 160, 8, 160);
                btnBlack.Click += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(ip))
                        PermanentBlacklistIps(new[] { ip });
                    dlg.Close();
                };
                if (string.IsNullOrEmpty(ip)) btnBlack.Enabled = false;
                var btnClose = MakeButton("Sluiten", 328, 8, 90);
                btnClose.Click += (s, e) => dlg.Close();
                var countLbl = new Label
                {
                    AutoSize = true,
                    Left = 430,
                    Top = 14,
                    ForeColor = Muted,
                    Text = events.Count + " regel(s) in logfile"
                };
                bar.Controls.AddRange(new Control[] { btnLog, btnBlack, btnClose, countLbl });

                dlg.Controls.Add(lv);
                dlg.Controls.Add(bar);
                dlg.Controls.Add(summary);
                dlg.ShowDialog(this);
            }
        }

        void RefreshBans(bool showStatus)
        {
            try
            {
                _bansCache.Clear();
                if (IpBanDatabase.Exists(InstallDir))
                    _bansCache.AddRange(IpBanDatabase.GetBanned(InstallDir));

                // sqlite leeg/locked → toon IP’s uit IPBan-firewallregels
                if (_lastFwStatus == null || (DateTime.UtcNow - _fwStatusAt).TotalSeconds > 30)
                    RefreshFirewallStatusOnly();
                MergeFirewallBans();

                ApplyBanFilter();
                ProcessAutoBlacklist(true);
                if (showStatus)
                {
                    var db = _bansCache.Count(e => !e.FromFirewallOnly);
                    var fw = _bansCache.Count(e => e.FromFirewallOnly);
                    var msg = db + " ban(s) in database";
                    if (fw > 0) msg += " · " + fw + " extra in firewall";
                    if (!string.IsNullOrEmpty(IpBanDatabase.LastError))
                        msg += " · sqlite: " + IpBanDatabase.LastError;
                    else if (db == 0 && fw == 0 && !IpBanDatabase.Exists(InstallDir))
                        msg += " · ipban.sqlite ontbreekt";
                    SetStatus(msg);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Bans lezen: " + ex.Message);
            }
        }

        void MergeFirewallBans()
        {
            var fwIps = FirewallHelper.GetBlockedIps(_lastFwStatus);
            if (fwIps.Count == 0) return;
            var have = new HashSet<string>(
                _bansCache.Select(e => e.Ip ?? ""),
                StringComparer.OrdinalIgnoreCase);
            foreach (var ip in fwIps)
            {
                if (have.Contains(ip)) continue;
                _bansCache.Add(new IpBanEntry
                {
                    Ip = ip,
                    State = IpBanState.FirewallOnly,
                    FromFirewallOnly = true
                });
            }
        }

        /// <summary>
        /// Telt ban-episodes; bij drempel (default 3) → automatisch blacklist + opmerking + opslaan.
        /// </summary>
        void ProcessAutoBlacklist(bool force)
        {
            if (_autoBlacklistAfter <= 0) return;
            if (!force && (DateTime.UtcNow - _autoBlacklistAt).TotalSeconds < 8) return;
            _autoBlacklistAt = DateTime.UtcNow;

            try
            {
                if (_banHistory == null) _banHistory = BanHistoryStore.Load();
                if (_lastMonitor != null)
                    _banHistory.ObserveLogEvents(_lastMonitor.Events);

                List<IpBanEntry> banned;
                if (_bansCache.Count > 0)
                    banned = _bansCache;
                else if (IpBanDatabase.Exists(InstallDir))
                    banned = IpBanDatabase.GetBanned(InstallDir);
                else
                    banned = new List<IpBanEntry>();

                var promoted = _banHistory.ObserveAndPromote(
                    banned, _autoBlacklistAfter, _whiteAll, _blackAll);

                if (promoted.Count == 0) return;

                var addedIps = new List<string>();
                foreach (var p in promoted)
                {
                    if (_blackAll.Any(x => string.Equals(x, p.Ip, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (_whiteAll.Any(x => string.Equals(x, p.Ip, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    _blackAll.Add(p.Ip);
                    addedIps.Add(p.Ip);
                }
                if (addedIps.Count == 0) return;

                _blackAll.Sort(StringComparer.OrdinalIgnoreCase);
                ApplyListFilter(false);
                dashBlack.Text = _blackAll.Count.ToString();

                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        var doc = IpBanConfig.Load(ConfigPath);
                        IpBanConfig.WriteList(doc, "Blacklist", _blackAll);
                        IpBanConfig.BackupAndSave(ConfigPath, doc);
                        SetDirty(false);
                    }
                    else
                    {
                        MarkDirty();
                    }
                }
                catch
                {
                    MarkDirty();
                }

                WriteDropFile("ban.txt", string.Join(Environment.NewLine, addedIps));

                var first = promoted.FirstOrDefault(p => addedIps.Contains(p.Ip));
                var msg = addedIps.Count == 1
                    ? addedIps[0] + " niet meer toelaten — blacklist (" +
                      (first != null && first.History != null ? first.History.Count + "× geband" : "herhaalde ban") + ")."
                    : addedIps.Count + " IP’s op blacklist gezet (herhaalde bans).";
                SetStatus(msg + " Opmerking bewaard. Herstart IPBan als het nog niet actief is.");
                try
                {
                    trayIcon.Visible = true;
                    trayIcon.ShowBalloonTip(6000, "Niet meer toelaten", msg, ToolTipIcon.Warning);
                }
                catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                SetStatus("Auto-blacklist: " + ex.Message);
            }
        }

        void ApplyBanFilter()
        {
            var q = txtBanFilter.Text.Trim();
            lvBans.BeginUpdate();
            lvBans.Items.Clear();
            foreach (var e in _bansCache)
            {
                if (q.Length > 0 && (e.Ip == null || e.Ip.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                var hist = _banHistory != null ? _banHistory.Get(e.Ip) : null;
                var item = new ListViewItem(e.Ip ?? "");
                item.SubItems.Add(e.StateLabel);
                item.SubItems.Add(e.FailedLoginCount > 0 ? e.FailedLoginCount.ToString() : (e.FromFirewallOnly ? "—" : "0"));
                var times = hist != null ? hist.Count : 0;
                item.SubItems.Add(times > 0 ? times.ToString() : "1");
                item.SubItems.Add(Fmt(e.LastFailedLoginUtc));
                item.SubItems.Add(Fmt(e.BanDateUtc));
                item.SubItems.Add(hist != null && !string.IsNullOrWhiteSpace(hist.Note)
                    ? hist.Note
                    : (times >= _autoBlacklistAfter && _autoBlacklistAfter > 0
                        ? "Herhaalde ban — komt op blacklist"
                        : ""));
                item.Tag = e;
                if (times >= Math.Max(2, _autoBlacklistAfter))
                    item.BackColor = Color.FromArgb(254, 226, 226);
                lvBans.Items.Add(item);
            }
            lvBans.EndUpdate();
        }

        void RefreshFailed(bool showStatus)
        {
            try
            {
                _failedCache.Clear();
                if (IpBanDatabase.Exists(InstallDir))
                    _failedCache.AddRange(IpBanDatabase.GetFailedOnly(InstallDir));
                ApplyFailedFilter();
                if (showStatus)
                    SetStatus(_failedCache.Count + " IP(s) met mislukte logins");
            }
            catch (Exception ex)
            {
                SetStatus("Failed logins: " + ex.Message);
            }
        }

        void ApplyFailedFilter()
        {
            var q = txtFailedFilter.Text.Trim();
            lvFailed.BeginUpdate();
            lvFailed.Items.Clear();
            foreach (var e in _failedCache)
            {
                if (q.Length > 0 && (e.Ip == null || e.Ip.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                var item = new ListViewItem(e.Ip ?? "");
                item.SubItems.Add(e.FailedLoginCount.ToString());
                item.SubItems.Add(Fmt(e.LastFailedLoginUtc));
                item.SubItems.Add(e.StateLabel);
                item.Tag = e;
                lvFailed.Items.Add(item);
            }
            lvFailed.EndUpdate();
        }

        static string Fmt(DateTime? utc)
        {
            return utc.HasValue ? utc.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";
        }

        void RefreshFirewallStatusOnly()
        {
            try
            {
                _lastFwStatus = FirewallHelper.GetStatus(_firewallPrefix);
                _fwStatusAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _lastFwStatus = new FirewallStatus
                {
                    Prefix = _firewallPrefix,
                    Error = ex.Message
                };
                _fwStatusAt = DateTime.UtcNow;
            }
        }

        void UpdateFirewallDashboardCard()
        {
            var st = _lastFwStatus;
            if (st == null)
            {
                dashFirewall.Text = "—";
                return;
            }
            dashFirewall.Text = st.ShortLabel;
            dashFirewall.ForeColor = st.StatusColor;
            if (!string.IsNullOrEmpty(st.DetailText) &&
                (lblMonitorHint.Text == null || lblMonitorHint.Text.StartsWith("Selecteer") ||
                 lblMonitorHint.Text.StartsWith("Geen relevante") || lblMonitorHint.Text.StartsWith("Drukste") ||
                 lblMonitorHint.Text.StartsWith("Firewall:")))
            {
                // Alleen firewallhint tonen als er geen event-selectie actief is
                if (lvRecent.SelectedItems.Count == 0)
                    lblMonitorHint.Text = "Firewall: " + st.DetailText;
            }
        }

        void RefreshFirewall()
        {
            lvFw.BeginUpdate();
            lvFw.Items.Clear();
            try
            {
                RefreshFirewallStatusOnly();
                var status = _lastFwStatus ?? new FirewallStatus { Prefix = _firewallPrefix };
                lblFwStatus.Text = status.DetailText;
                lblFwStatus.ForeColor = status.StatusColor;
                UpdateFirewallDashboardCard();

                foreach (var r in status.Rules)
                {
                    var item = new ListViewItem(r.Name ?? "");
                    item.UseItemStyleForSubItems = false;
                    var rowColor = r.Enabled == true ? Color.FromArgb(220, 252, 231)
                        : r.Enabled == false ? Color.FromArgb(254, 226, 226)
                        : Color.FromArgb(248, 250, 252);
                    item.BackColor = rowColor;
                    item.SubItems.Add(r.EnabledLabel);
                    item.SubItems[1].BackColor = rowColor;
                    item.SubItems[1].ForeColor = r.Enabled == true ? Color.FromArgb(21, 128, 61)
                        : r.Enabled == false ? Color.FromArgb(185, 28, 28)
                        : Muted;
                    item.SubItems[1].Font = new Font(Font, FontStyle.Bold);
                    item.SubItems.Add(r.Action ?? "");
                    item.SubItems[2].BackColor = rowColor;
                    item.SubItems.Add(r.AddressCount.ToString());
                    item.SubItems[3].BackColor = rowColor;
                    var summary = r.RemoteAddresses ?? "";
                    if (summary.Length > 120) summary = summary.Substring(0, 117) + "…";
                    item.SubItems.Add(summary);
                    item.SubItems[4].BackColor = rowColor;
                    item.Tag = r;
                    lvFw.Items.Add(item);
                }

                if (!string.IsNullOrEmpty(status.Error))
                    SetStatus("Firewall: " + status.Error);
                else
                    SetStatus(status.ShortLabel + " — prefix " + _firewallPrefix);
            }
            catch (Exception ex)
            {
                lblFwStatus.Text = "Firewall lezen mislukt: " + ex.Message;
                lblFwStatus.ForeColor = Color.FromArgb(185, 28, 28);
                SetStatus(ex.Message);
            }
            lvFw.EndUpdate();
        }

        IEnumerable<string> SelectedIps(ListView lv)
        {
            return lv.SelectedItems.Cast<ListViewItem>()
                .Select(i => i.Text)
                .Where(s => !string.IsNullOrWhiteSpace(s));
        }

        void UnbanSelected(ListView lv)
        {
            var ips = SelectedIps(lv).ToList();
            if (ips.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst één of meer IP’s.", "Unban", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            WriteDropFile("unban.txt", string.Join(Environment.NewLine, ips));
        }

        void BanSelected(ListView lv)
        {
            var ips = SelectedIps(lv).ToList();
            if (ips.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst één of meer IP’s.", "Ban", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            WriteDropFile("ban.txt", string.Join(Environment.NewLine, ips));
        }

        void MoveSelectedToWhitelist(ListView lv)
        {
            var ips = SelectedIps(lv).ToList();
            if (ips.Count == 0)
            {
                MessageBox.Show(this, "Selecteer eerst één of meer IP’s.", "Whitelist", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var added = 0;
            foreach (var ip in ips)
            {
                if (_whiteAll.Any(x => string.Equals(x, ip, StringComparison.OrdinalIgnoreCase))) continue;
                _whiteAll.Add(ip);
                added++;
            }
            _whiteAll.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyListFilter(true);
            RefreshWhitelistUi();
            MarkDirty();
            WriteDropFile("unban.txt", string.Join(Environment.NewLine, ips));
            SetStatus(added + " naar whitelist (nog opslaan) + unban.txt geschreven");
            MessageBox.Show(this,
                "IP’s toegevoegd aan whitelist en unban aangevraagd.\nVergeet niet te Opslaan en de service te herstarten.",
                "Whitelist", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void CopySelectedIps(ListView lv)
        {
            var text = string.Join(Environment.NewLine, SelectedIps(lv));
            if (text.Length > 0) Clipboard.SetText(text);
        }

        void RunWeeklyBackupIfDue()
        {
            _backupCheck = DateTime.UtcNow;
            try
            {
                var path = FrontendData.MaybeWeeklyBackup();
                UpdateBackupStatus();
                if (!string.IsNullOrEmpty(path))
                    SetStatus("Wekelijkse backup: " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                SetStatus("Backup: " + ex.Message);
            }
        }

        void UpdateBackupStatus()
        {
            var nl = CultureInfo.GetCultureInfo("nl-NL");
            var last = FrontendData.LastBackupUtc;
            var when = last.HasValue
                ? last.Value.ToLocalTime().ToString("d MMMM yyyy HH:mm", nl)
                : "nog geen";
            lblBackupStatus.Text = string.Format(nl,
                "Laatste backup: {0}  ·  {1} zip(s) in backups  ·  automatisch eens per week",
                when, FrontendData.BackupCount);
        }

        void MakeFrontendBackup(bool notify)
        {
            try
            {
                var path = FrontendData.CreateBackup();
                UpdateBackupStatus();
                SetStatus("Backup: " + path);
                if (notify)
                    MessageBox.Show(this, "Backup gemaakt:\n" + path, "Backup",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Backup mislukt: " + ex.Message, "Backup",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ExportFrontendZip()
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Zip|*.zip";
                sfd.FileName = "IPBanFrontend-data-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip";
                sfd.Title = "Zip van ProgramData\\IPBanFrontend";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    FrontendData.ExportZip(sfd.FileName);
                    SetStatus("Zip: " + sfd.FileName);
                    MessageBox.Show(this, "Data geëxporteerd:\n" + sfd.FileName, "Zip",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Zip mislukt: " + ex.Message, "Zip",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void ExportCurrentChart()
        {
            if (_trend == null) _trend = AttemptTrendStore.Load();
            IngestTrend(false);
            PaintTrendChart();
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG|*.png";
                sfd.FileName = "pogingen-" + CurrentTrendGrain().ToString().ToLowerInvariant() +
                               "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".png";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    chartTrend.SavePng(sfd.FileName);
                    SetStatus("Grafiek: " + sfd.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export mislukt: " + ex.Message, "Grafiek",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void ExportAllCharts()
        {
            if (_trend == null) _trend = AttemptTrendStore.Load();
            IngestTrend(true);
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Zip|*.zip";
                sfd.FileName = "IPBanFrontend-grafieken-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip";
                sfd.Title = "Grafieken (PNG + CSV)";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    FrontendData.ExportChartsZip(sfd.FileName, _trend);
                    SetStatus("Grafieken: " + sfd.FileName);
                    MessageBox.Show(this,
                        "Geëxporteerd (uur/dag/week/maand + CSV):\n" + sfd.FileName,
                        "Grafieken", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export mislukt: " + ex.Message, "Grafieken",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void ExportBans()
        {
            if (_bansCache.Count == 0) RefreshBans(false);
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV|*.csv";
                sfd.FileName = "ipban-bans-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine("IP,State,FailedLoginCount,LastFailedLoginUtc,BanDateUtc,BanEndDateUtc");
                foreach (var e in _bansCache)
                {
                    sb.Append(Csv(e.Ip)).Append(',')
                      .Append(Csv(e.StateLabel)).Append(',')
                      .Append(e.FailedLoginCount).Append(',')
                      .Append(Csv(Fmt(e.LastFailedLoginUtc))).Append(',')
                      .Append(Csv(Fmt(e.BanDateUtc))).Append(',')
                      .Append(Csv(Fmt(e.BanEndDateUtc))).AppendLine();
                }
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                SetStatus("Geëxporteerd: " + sfd.FileName);
            }
        }

        static string Csv(string s)
        {
            s = s ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        void PromptBan(bool ban)
        {
            using (var dlg = new Form())
            {
                dlg.Text = ban ? "Ban IP" : "Unban IP";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(360, 110);
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                var lbl = new Label
                {
                    Text = ban ? "IP-adres om te bannen:" : "IP-adres om te unbannen:",
                    Left = 12, Top = 12, AutoSize = true
                };
                var box = new TextBox { Left = 12, Top = 36, Width = 330 };
                var ok = new Button { Text = "OK", Left = 166, Top = 72, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Annuleren", Left = 252, Top = 72, Width = 90, DialogResult = DialogResult.Cancel };
                dlg.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.IsNullOrWhiteSpace(box.Text)) return;
                WriteDropFile(ban ? "ban.txt" : "unban.txt", box.Text.Trim());
            }
        }

        async Task WhitelistMyIpAsync()
        {
            try
            {
                SetStatus("Publiek IP ophalen…");
                string url = null;
                if (File.Exists(ConfigPath))
                {
                    var doc = IpBanConfig.Load(ConfigPath);
                    url = IpBanConfig.ReadValue(doc, "ExternalIPAddressUrl");
                }
                if (string.IsNullOrWhiteSpace(url))
                    url = "https://checkip.amazonaws.com/";

                string body;
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(10);
                    body = (await http.GetStringAsync(url)).Trim();
                }

                if (IsDisposed) return;

                var ip = ExtractPublicIp(body);
                if (string.IsNullOrEmpty(ip))
                    throw new InvalidOperationException("Geen geldig IP ontvangen van " + url);

                if (_whiteAll.Any(x => string.Equals(x, ip, StringComparison.OrdinalIgnoreCase)))
                {
                    SetStatus("Staat al op whitelist: " + ip);
                    MessageBox.Show(this, "Dit IP staat al op de whitelist:\n" + ip, "Whitelist",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var r = MessageBox.Show(this,
                    "Publiek IP gedetecteerd: " + ip + "\n\nToevoegen aan whitelist en opslaan?",
                    "Whitelist mijn IP", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;

                _whiteAll.Add(ip);
                _whiteAll.Sort(StringComparer.OrdinalIgnoreCase);
                ApplyListFilter(true);
                RefreshWhitelistUi();
                MarkDirty();
                SaveConfig(true);
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show(this, ex.Message, "Publiek IP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string ExtractPublicIp(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            var line = body.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? body.Trim();
            // IPv4
            var m4 = Regex.Match(line, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
            if (m4.Success)
            {
                var parts = m4.Value.Split('.');
                if (parts.All(p => { int n; return int.TryParse(p, out n) && n >= 0 && n <= 255; }))
                    return m4.Value;
            }
            // IPv6 (ruwe check)
            var m6 = Regex.Match(line, @"\b[0-9a-fA-F:]{2,}\b");
            if (m6.Success && m6.Value.Contains(":")) return m6.Value;
            return null;
        }

        void WriteDropFile(string fileName, string content)
        {
            content = (content ?? "").Trim();
            if (content.Length == 0)
            {
                MessageBox.Show(this, "Vul één of meer IP’s in.", "Ban/Unban", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                if (!Directory.Exists(InstallDir))
                    throw new DirectoryNotFoundException(InstallDir);
                var lines = content.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (lines.Count == 0)
                {
                    MessageBox.Show(this, "Vul één of meer IP’s in.", "Ban/Unban", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var target = Path.Combine(InstallDir, fileName);
                var tmp = target + ".tmp";
                File.WriteAllText(tmp, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.ASCII);
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
                SetStatus(fileName + " geschreven (" + lines.Count + ") — IPBan pakt dit in de volgende cyclus op.");
                ClearCue(txtQuickIp);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Schrijven mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void RefreshLog(bool reset)
        {
            if (chkLogPause.Checked && !reset) return;
            try
            {
                if (!File.Exists(LogPath))
                {
                    if (reset) txtLog.Text = "(geen logfile.txt — is IPBan geïnstalleerd?)";
                    return;
                }
                var info = new FileInfo(LogPath);
                if (reset || _logPointer > info.Length)
                {
                    const int maxBytes = 120 * 1024;
                    _logPointer = Math.Max(0, info.Length - maxBytes);
                }
                using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(_logPointer, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        var chunk = sr.ReadToEnd();
                        _logPointer = fs.Position;
                        if (chunk.Length == 0) return;
                        var filtered = FilterLogChunk(chunk);
                        if (filtered.Length == 0 && !reset) return;
                        if (reset) txtLog.Text = filtered;
                        else
                        {
                            txtLog.AppendText(filtered);
                            if (txtLog.TextLength > 250000)
                                txtLog.Text = txtLog.Text.Substring(txtLog.TextLength - 180000);
                        }
                        if (!chkLogPause.Checked)
                        {
                            txtLog.SelectionStart = txtLog.TextLength;
                            txtLog.ScrollToCaret();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (reset) txtLog.Text = "Log lezen: " + ex.Message;
            }
        }

        string FilterLogChunk(string chunk)
        {
            var mode = cmbLogLevel.SelectedItem as string ?? "Alles";
            var q = txtLogFilter.Text.Trim();
            if (mode == "Alles" && q.Length == 0) return chunk;

            var sb = new StringBuilder();
            foreach (var line in chunk.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (!LineMatchesFilter(line, mode, q)) continue;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        static bool LineMatchesFilter(string line, string mode, string q)
        {
            if (q.Length > 0 && line.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            switch (mode)
            {
                case "Ban/Unban":
                    return ContainsAny(line, "ban", "unban");
                case "Failed":
                    return ContainsAny(line, "failed login", "login attempt failed");
                case "Success":
                    return ContainsAny(line, "success login", "successful login");
                case "ERROR/WARN":
                    return ContainsAny(line, "ERROR", "WARN", "FATAL");
                default:
                    return true;
            }
        }

        static bool ContainsAny(string hay, params string[] needles)
        {
            foreach (var n in needles)
                if (hay.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        void RefreshService()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    var st = sc.Status;
                    lblService.Text = "IPBan: " + ShortStatus(st);
                    lblService.ForeColor = st == ServiceControllerStatus.Running
                        ? Color.FromArgb(21, 128, 61)
                        : Color.FromArgb(185, 28, 28);
                }
            }
            catch
            {
                lblService.Text = "IPBan: niet gevonden";
                lblService.ForeColor = Color.FromArgb(185, 28, 28);
            }
        }

        static string ShortStatus(ServiceControllerStatus st)
        {
            switch (st)
            {
                case ServiceControllerStatus.Running: return "actief";
                case ServiceControllerStatus.Stopped: return "gestopt";
                case ServiceControllerStatus.StartPending: return "start…";
                case ServiceControllerStatus.StopPending: return "stop…";
                default: return st.ToString();
            }
        }

        void OpenFile(string path)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "Bestand niet gevonden: " + path, "Openen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Openen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void SetStatus(string text)
        {
            statusLabel.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        }

        void SetupTray()
        {
            trayIcon.Text = "IPBan Frontend — dubbelklik = openen";
            _trayIconOwned = CreateTrayIcon();
            trayIcon.Icon = _trayIconOwned;
            trayIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Openen", null, (s, e) => ShowFromTray());
            menu.Items.Add("Als Administrator herstarten", null, (s, e) =>
            {
                if (Elevation.RestartElevated())
                {
                    _dirty = false;
                    _reallyExit = true;
                    try { trayIcon.Visible = false; } catch { }
                    Close();
                }
            });
            menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("Afsluiten");
            exit.Click += (s, e) => ExitApp();
            menu.Items.Add(exit);
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += (s, e) => ShowFromTray();
            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    ShowFromTray();
            };
        }

        static Icon CreateTrayIcon()
        {
            try { return (Icon)SystemIcons.Shield.Clone(); }
            catch
            {
                try { return (Icon)SystemIcons.Application.Clone(); }
                catch { return SystemIcons.Application; }
            }
        }

        void HideToTray()
        {
            try
            {
                if (trayIcon.Icon == null)
                {
                    _trayIconOwned = CreateTrayIcon();
                    trayIcon.Icon = _trayIconOwned;
                }
                trayIcon.Visible = true;
                trayIcon.Text = "IPBan Frontend — rechtsklik voor Afsluiten";

                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
                Hide();

                if (!_balloonShown)
                {
                    _balloonShown = true;
                    trayIcon.ShowBalloonTip(4000, "IPBan Frontend",
                        "Draait in het systeemvak (bij de ^ pijl).\nDubbelklik = openen · Rechtsklik = Afsluiten",
                        ToolTipIcon.Info);
                }
                SetStatus("Geminimaliseerd naar systeemvak");
            }
            catch (Exception ex)
            {
                // Fallback: als tray faalt, venster terugzetten i.p.v. “verdwijnen”
                try
                {
                    ShowInTaskbar = true;
                    Show();
                    WindowState = FormWindowState.Normal;
                }
                catch { }
                MessageBox.Show(this,
                    "Systeemvak-icoon lukte niet:\n" + ex.Message +
                    "\n\nKijk bij de verborgen iconen (^) naast de klok.",
                    "Tray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ShowFromTray()
        {
            ForceShowWindow();
        }

        void ForceShowWindow()
        {
            try
            {
                if (!IsHandleCreated) CreateHandle();
                Show();
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;
                ShowInTaskbar = true;
                trayIcon.Visible = true;
                BringToFront();
                Activate();
                Focus();
            }
            catch { /* ignore */ }
        }

        /// <summary>Aangeroepen door 2e instantie via WM_SHOWME — venster terug naar voren.</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WmShowMe)
            {
                ForceShowWindow();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        void ExitApp()
        {
            if (_dirty)
            {
                var r = MessageBox.Show(this,
                    "Er zijn niet-opgeslagen configwijzigingen. Toch afsluiten?",
                    "Niet opgeslagen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
                _dirty = false;
            }
            _reallyExit = true;
            try { trayIcon.Visible = false; } catch { }
            Close();
        }

        void OpenFolder(string path)
        {
            if (!Directory.Exists(path))
            {
                MessageBox.Show(this, "Map bestaat niet: " + path, "Openen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + path + "\"",
                UseShellExecute = true
            });
        }

        void RestartService()
        {
            if (!ControlService(false)) return;
            ControlService(true);
        }

        bool ControlService(bool start)
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    sc.Refresh();
                    if (start)
                    {
                        if (sc.Status == ServiceControllerStatus.Running) { RefreshService(); return true; }
                        if (sc.Status == ServiceControllerStatus.StartPending)
                        {
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
                            RefreshService();
                            return true;
                        }
                        if (sc.Status == ServiceControllerStatus.StopPending)
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                        sc.Refresh();
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
                    }
                    else
                    {
                        if (sc.Status == ServiceControllerStatus.Stopped) { RefreshService(); return true; }
                        if (sc.Status == ServiceControllerStatus.StopPending)
                        {
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                            RefreshService();
                            return true;
                        }
                        if (sc.Status == ServiceControllerStatus.StartPending)
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
                        sc.Refresh();
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                }
                RefreshService();
                SetStatus(start ? "Service gestart" : "Service gestopt");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message + "\n\nStart deze tool als Administrator.",
                    "Service", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void UpdateElevationBanner()
        {
            if (Elevation.IsAdministrator())
            {
                lblElevBanner.Visible = false;
                Text = "IPBan Frontend";
                return;
            }
            lblElevBanner.Visible = true;
            lblElevBanner.BackColor = Color.FromArgb(254, 243, 199);
            lblElevBanner.ForeColor = Color.FromArgb(120, 53, 15);
            lblElevBanner.Text = "  Beperkte modus (geen Administrator) — klik hier om met UAC als admin te herstarten. Service/config-wijzigingen kunnen mislukken.";
            Text = "IPBan Frontend (beperkt)";
        }

        void LoadAppSettingsUi()
        {
            var s = AppSettings.Load();
            _loadingAppSettings = true;
            try
            {
                chkAutoStart.Checked = s.Enabled || AutoStartManager.IsServiceInstalled();
                chkGuiAtLogon.Checked = s.StartGuiAtLogon;
                _autoBlacklistAfter = s.AutoBlacklistAfterBans;
                chkAutoBlacklist.Checked = _autoBlacklistAfter > 0;
            }
            finally
            {
                _loadingAppSettings = false;
            }
            RefreshAutoStartStatus();
            UpdateBackupStatus();
        }

        void RefreshAutoStartStatus()
        {
            try
            {
                var installed = AutoStartManager.IsServiceInstalled();
                var st = AutoStartManager.ServiceStatusText();
                var task = AutoStartManager.IsLogonTaskPresent() ? "ja" : "nee";
                lblAutoStartStatus.Text = "Service: " + st +
                    "  ·  logon-GUI taak: " + task +
                    (installed ? "" : "  — zet enabled aan + Toepassen (admin)") +
                    "  ·  " + AppSettings.FilePath;
            }
            catch
            {
                lblAutoStartStatus.Text = "Autostart-status onbekend";
            }
        }

        void ApplyAutoStartSetting()
        {
            var enable = chkAutoStart.Checked;
            var guiLogon = chkGuiAtLogon.Checked;
            _autoBlacklistAfter = chkAutoBlacklist.Checked ? 3 : 0;

            try
            {
                var prefs = AppSettings.Load();
                prefs.StartGuiAtLogon = guiLogon;
                prefs.AutoBlacklistAfterBans = _autoBlacklistAfter;
                prefs.InstallDir = InstallDir;
                prefs.Save();
            }
            catch { /* ignore */ }

            if (!Elevation.IsAdministrator())
            {
                var r = MessageBox.Show(this,
                    "Autostart installeren/verwijderen vereist Administrator.\n\n" +
                    "Auto-blacklist-instelling is al opgeslagen.\n\nNu elevaten voor autostart?",
                    "Administrator", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes)
                {
                    SetStatus("Auto-blacklist: " + (_autoBlacklistAfter > 0 ? "aan (na 3 bans)" : "uit"));
                    return;
                }

                var args = enable
                    ? (guiLogon
                        ? new[] { "--install-autostart" }
                        : new[] { "--install-autostart", "--no-logon-gui" })
                    : new[] { "--uninstall-autostart" };

                if (Elevation.RunElevatedArgs(args))
                {
                    System.Threading.Thread.Sleep(500);
                    LoadAppSettingsUi();
                    var ok = enable
                        ? AutoStartManager.IsServiceInstalled()
                        : !AutoStartManager.IsServiceInstalled();
                    SetStatus(ok
                        ? (enable ? "Autostart ingeschakeld" : "Autostart uitgeschakeld")
                        : "Autostart-commando klaar — controleer service-status");
                    if (!ok)
                        MessageBox.Show(this,
                            "Commando uitgevoerd, maar status komt niet overeen.\nBekijk service.log in ProgramData.",
                            "Autostart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(this, "Elevatie mislukt of geannuleerd. Auto-blacklist-instelling is wel bewaard.",
                        "Autostart", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            try
            {
                if (enable) AutoStartManager.Install(guiLogon);
                else AutoStartManager.Uninstall();

                var s = AppSettings.Load();
                s.InstallDir = InstallDir;
                s.StartGuiAtLogon = guiLogon;
                s.AutoBlacklistAfterBans = _autoBlacklistAfter;
                s.Save();

                LoadAppSettingsUi();
                MessageBox.Show(this,
                    enable
                        ? "Windows-service 'IPBanFrontend' staat op Automatisch (start bij boot).\n" +
                          (guiLogon ? "Na login start ook de GUI in het systeemvak.\n" : "") +
                          "Auto-blacklist: " + (_autoBlacklistAfter > 0 ? "aan (na 3 bans).\n" : "uit.\n") +
                          "\nLet op: een GUI kan niet zichtbaar draaien vóór login (Session 0).\n" +
                          "De service wél — zie service.log in ProgramData."
                        : "Autostart-service en logon-taak verwijderd.\nAuto-blacklist: " +
                          (_autoBlacklistAfter > 0 ? "aan (na 3 bans)." : "uit."),
                    "Autostart", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus(enable ? "Autostart enabled" : "Autostart disabled");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Autostart mislukt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // cue banner for .NET 4.8 TextBox
        static readonly Dictionary<TextBox, string> Cues = new Dictionary<TextBox, string>();

        static void SetCue(TextBox tb, string cue)
        {
            Cues[tb] = cue;
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                tb.ForeColor = Color.Gray;
                tb.Text = cue;
            }
            tb.GotFocus -= CueGotFocus;
            tb.LostFocus -= CueLostFocus;
            tb.GotFocus += CueGotFocus;
            tb.LostFocus += CueLostFocus;
        }

        static void CueGotFocus(object sender, EventArgs e)
        {
            var tb = (TextBox)sender;
            string cue;
            if (!Cues.TryGetValue(tb, out cue)) return;
            if (tb.Text == cue) { tb.Text = ""; tb.ForeColor = SystemColors.WindowText; }
        }

        static void CueLostFocus(object sender, EventArgs e)
        {
            var tb = (TextBox)sender;
            string cue;
            if (!Cues.TryGetValue(tb, out cue)) return;
            if (string.IsNullOrWhiteSpace(tb.Text)) { tb.ForeColor = Color.Gray; tb.Text = cue; }
        }

        static string GetCueText(TextBox tb)
        {
            string cue;
            if (Cues.TryGetValue(tb, out cue) && tb.Text == cue) return "";
            return tb.Text ?? "";
        }

        static void ClearCue(TextBox tb)
        {
            tb.Clear();
            string cue;
            if (Cues.TryGetValue(tb, out cue))
            {
                tb.ForeColor = Color.Gray;
                tb.Text = cue;
            }
        }
    }

    sealed class HiddenHeaderTabControl : TabControl
    {
        public HiddenHeaderTabControl()
        {
            Appearance = TabAppearance.FlatButtons;
            ItemSize = new Size(0, 1);
            SizeMode = TabSizeMode.Fixed;
            Multiline = false;
        }

        protected override void WndProc(ref Message m)
        {
            // TCM_ADJUSTRECT: tabkoppen verbergen. Alleen bij wParam==1 (display rect), anders base.
            if (m.Msg == 0x1328 && !DesignMode && m.WParam != IntPtr.Zero)
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
