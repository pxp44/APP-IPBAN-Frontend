IPBan Frontend
==============

WinForms GUI for the free IPBan service on Windows — Pro-like local management
without multi-server / country-block / IPBan Shield (those remain Pro features).

Features
--------
- Home dashboard: bans, failed logins, white/blacklist, service status, recent log events
- Active bans from ipban.sqlite (unban, whitelist+unban, CSV export)
- Failed logins still under the ban threshold — view and force-ban
- Attack trend charts (attempts vs previous period)
- Whitelist / blacklist editing (+ regex, bulk paste)
- Auto-blacklist after repeated bans (default: after 3 ban episodes)
- Settings from ipban.config (BanTime, thresholds, FirewallRules, UriRules, …)
- Community blocklists via FirewallUriRules (sync e.g. every 8h); log shows [community: ListName]
- Windows Firewall rules with the IPBan_ prefix
- Live logfile with filter / pause
- Start / stop / restart the IPBan service
- Direct ban / unban via ban.txt / unban.txt
- Fetch and whitelist your public IP
- Weekly backups, zip export, and chart export
  (%ProgramData%\IPBanFrontend)

Requirements
------------
- Windows Server 2016/2019/2022 or Windows 10/11
- .NET Framework 4.8
- Optional Administrator (at startup you can choose: UAC or limited)
- IPBan installed in C:\Program Files\IPBan (or set another folder)
- When building: .NET SDK (for the Microsoft.Data.Sqlite NuGet package)

Autostart (enabled)
-------------------
Settings tab → "enabled — start Windows service at system startup".
This registers the Windows service "IPBanFrontend" (start=auto), even without a login.
The GUI itself cannot appear before login (Session 0); optionally it starts in the
system tray after logon (--tray). Settings: %ProgramData%\IPBanFrontend\settings.json

  {
    "enabled": true,
    "startGuiAtLogon": true,
    "autoBlacklistAfterBans": 3,
    "installDir": "C:\\Program Files\\IPBan"
  }

CLI: IPBanFrontend.exe --install-autostart | --uninstall-autostart | --service | --tray

Build
-----
  dotnet build IPBanFrontend.csproj -c Release

  Output: bin\Release\  (copy the entire folder)

Usage
-----
1. Install IPBan.
2. Run IPBanFrontend.exe — choose Yes for admin, or No for limited mode.
3. Confirm the IPBan folder.
4. Whitelist your IP → Save → restart the IPBan service.
5. Optional: enable autostart + Apply.

Notes
-----
- Whitelist in this tool = the config key Whitelist.
- ban.txt / unban.txt are picked up by IPBan itself (wait one cycle).
- This is not an official IPBan product.
