IPBan Frontend
==============

WinForms-GUI voor de gratis IPBan-service op Windows — Pro-achtig lokaal beheer
zonder multi-server / country-block / IPBan Shield (dat blijft Pro).

Kan
---
- Dashboard: bans, mislukte logins, white/blacklist, service, recente log-events
- Actieve bans uit ipban.sqlite (unban, whitelist+unban, CSV-export)
- Mislukte logins (nog onder de ban-drempel) bekijken en forceren te bannen
- Whitelist / blacklist bewerken (+ regex, bulk plakken)
- Instellingen uit ipban.config (BanTime, drempels, FirewallRules, UriRules, …)
- Windows Firewall-regels met IPBan_-prefix tonen
- Live logfile met filter / pauze
- Service start/stop/herstart
- Direct ban/unban via ban.txt / unban.txt
- Publiek IP ophalen en whitelisten

Vereisten
---------
- Windows Server 2016/2019/2022 of Windows 10/11
- .NET Framework 4.8
- Optioneel Administrator (bij start kun je kiezen: UAC of beperkt)
- IPBan geïnstalleerd in C:\Program Files\IPBan (of andere map instellen)
- Bij bouwen: .NET SDK (voor NuGet Microsoft.Data.Sqlite)

Autostart (enabled)
-------------------
Instellingen-tab → "enabled — Windows-service starten bij systeemstart".
Dat registreert Windows-service "IPBanFrontend" (start=auto), ook zonder login.
De GUI zelf kan niet vóór login zichtbaar zijn (Session 0); optioneel start die
na login in het systeemvak (--tray). Settings: %ProgramData%\IPBanFrontend\settings.json

  { "enabled": true, "startGuiAtLogon": true, ... }

CLI: IPBanFrontend.exe --install-autostart | --uninstall-autostart | --service | --tray

Bouwen
------
  dotnet build IPBanFrontend.csproj -c Release

  Output: bin\Release\  (hele map kopiëren)

Gebruik
-------
1. IPBan geïnstalleerd.
2. IPBanFrontend.exe — kies Ja voor admin, of Nee voor beperkt.
3. Controleer de IPBan-map.
4. Whitelist jouw IP → Opslaan → herstart IPBan-service.
5. Optioneel: autostart enabled + Toepassen.

Let op
------
- Whitelist in deze tool = config-sleutel Whitelist.
- ban.txt / unban.txt worden door IPBan zelf opgepakt (één cyclus wachten).
- Dit is geen officieel IPBan-product.
