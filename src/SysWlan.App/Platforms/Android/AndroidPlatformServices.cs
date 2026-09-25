using Android.App;
using Android.Content;
using SysWlan.App.Services;
using SysWlan.Core;

namespace SysWlan.App;

/// <summary>Merkt sich, ob die App sichtbar ist: nur so kann die Erfassung ehrlich pausieren.</summary>
public static class AndroidAppLifecycle
{
    public static bool IsForeground { get; private set; }

    public static void SetForeground(bool value)
    {
        IsForeground = value;
        var monitor = IPlatformApplication.Current?.Services.GetService<MonitorService>();
        var background = IPlatformApplication.Current?.Services.GetService<IBackgroundMonitoring>();
        if (monitor is null) return;
        // Im Vordergrund wird immer erfasst; im Hintergrund nur, wenn der Vordergrunddienst läuft.
        monitor.SetBackgrounded(!value && background?.IsActive != true);
    }
}

public sealed class AndroidPlatformInfo : IPlatformInfo
{
    public string PlatformLabel => "Android";
    public string DeviceName => (global::Android.OS.Build.Manufacturer + " " + global::Android.OS.Build.Model).Trim();
    public string DeviceLabel => "Dieses Android-Gerät";
    public string DeviceShortLabel => "dieses Gerät";
    public string FooterLabel => "Android-Erfassung · lokale Speicherung";
    public string VersionLabel => $"Android · Version {AppInfo.Current.VersionString}";
    public string TrafficNote => "Gerätesumme von Android (NetworkStatsManager) · in Intervallen gebucht";
    public string DnsNote => "Eine gezielte Anfrage über den Namensauflöser von Android.";
    public CapabilityRow[] Capabilities =>
    [
        new("WLAN, IP, DNS, Linkraten", "ConnectivityManager, WifiManager", "Berechtigung „In der Nähe befindliche Geräte“ bzw. Standort"),
        new("Latenz zum Gateway", "ICMP-Ping", "Ohne Routeranmeldung"),
        new("Geräteweiter Datenverkehr", "NetworkStatsManager", "Systemzugriff „Nutzungszugriff“ erforderlich"),
        new("Netzwerkereignisse", "ConnectivityManager-Rückrufe", "Ersetzt das Windows-Ereignisprotokoll"),
        new("Geräte im Netz", "Router-Syslog", "Nur was der Router per Syslog meldet; Nachbartabelle ist gesperrt"),
        new("Verbindungen anderer Apps", "—", "Android erlaubt diese Einsicht nicht"),
        new("Firmware & DOCSIS-Status", "Öffentliche Routerseite", "Firmwareabhängig verfügbar"),
        new("Gastnetz, WLAN-Konfiguration, vollständige Clients", "Integrierte Routeroberfläche (WebView)", "Routeranmeldung erforderlich; Klartext-HTTP nur zum eigenen Gateway"),
        new("Automatisierte Routeränderungen", "Router-API", "Noch kein validierter Schreibadapter"),
        new("Router-Syslog", "UDP-Empfänger", "Versand im Router erforderlich; im Hintergrund nur mit Vordergrunddienst"),
        new("Dauermonitoring", "Vordergrunddienst (dataSync)", "Android 15 beendet den Dienst nach etwa sechs Stunden")
    ];
    public string DevicesNote => SysWlan.Core.AndroidWifiMapping.DevicesUnavailable + " Geräte erscheinen hier nur, wenn der Router sie per Syslog gemeldet hat — mit unbekanntem Namen, falls der Router keinen nennt.";
    public string ConnectionsNote => SysWlan.Core.AndroidWifiMapping.ConnectionsUnavailable + " Diese Ansicht bleibt deshalb bewusst leer.";
    public string LogsNote => SysWlan.Core.AndroidWifiMapping.EventsNote + " Router-Syslog erscheint nur, solange der Empfänger läuft; im Hintergrund gilt die Grenze des Vordergrunddienstes.";
}

public sealed class AndroidPlatformPermissions : IPlatformPermissions
{
    public bool IsRelevant => true;
    public bool CanRequestWifiDetails => !SysWlan.Android.AndroidNetworkAccess.HasWifiDetails();
    public bool NeedsTrafficAccess => !SysWlan.Android.AndroidNetworkAccess.HasUsageAccess();

    public string[] Rows()
    {
        var rows = new List<string>
        {
            SysWlan.Android.AndroidNetworkAccess.HasWifiDetails()
                ? "WLAN-Details (SSID, BSSID, Kanal, Signal): erlaubt"
                : $"WLAN-Details: gesperrt (Berechtigung „{SysWlan.Android.AndroidNetworkAccess.WifiPermissionName()}“ fehlt)",
            SysWlan.Android.AndroidNetworkAccess.HasUsageAccess()
                ? "Geräteweiter Datenverkehr: Nutzungszugriff erlaubt"
                : "Geräteweiter Datenverkehr: Nutzungszugriff fehlt",
            "Benachrichtigungen: " + (MonitoringForegroundService.NotificationsAllowed() ? "erlaubt" : "gesperrt oder nicht erteilt") + " (nur für den Vordergrunddienst nötig)"
        };
        if (SysWlan.Android.AndroidNetworkAccess.HasWifiDetails() && !SysWlan.Android.AndroidNetworkAccess.LocationEnabled())
            rows.Add("Standortschalter: aus — Android verbirgt dann SSID und BSSID");
        return [.. rows];
    }

    public async Task<string> RequestWifiDetailsAsync()
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var status = await Permissions.CheckStatusAsync<Permissions.NearbyWifiDevices>();
                if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.NearbyWifiDevices>();
                return status == PermissionStatus.Granted
                    ? "Berechtigung „In der Nähe befindliche Geräte“ erteilt."
                    : "Berechtigung nicht erteilt. SSID, BSSID, Kanal und Signalstärke bleiben unbekannt.";
            }
            var location = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (location != PermissionStatus.Granted) location = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            return location == PermissionStatus.Granted
                ? "Standortberechtigung erteilt; Android gibt jetzt WLAN-Details heraus."
                : "Standortberechtigung nicht erteilt. WLAN-Details bleiben unbekannt.";
        }
        catch (Exception ex) { return "Berechtigung konnte nicht angefragt werden: " + SyslogParser.Redact(ex.Message); }
    }

    public Task<string> OpenTrafficAccessAsync()
    {
        try
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionUsageAccessSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            global::Android.App.Application.Context.StartActivity(intent);
            return Task.FromResult("Android-Einstellungen geöffnet: Dort „Nutzungszugriff“ für SysWLANInfo erlauben und zurückkehren.");
        }
        catch (Exception ex) { return Task.FromResult("Einstellungen konnten nicht geöffnet werden: " + SyslogParser.Redact(ex.Message)); }
    }
}

public sealed class AndroidBackgroundMonitoring : IBackgroundMonitoring
{
    private readonly MonitorService monitor;

    /// <summary>Im Hintergrund wird höchstens alle 15 Sekunden gemessen statt im eingestellten 3–60-Sekunden-Takt.</summary>
    public AndroidBackgroundMonitoring(MonitorService monitor)
    {
        this.monitor = monitor;
        monitor.BackgroundIntervalSeconds = 15;
    }

    public bool IsSupported => true;
    public bool IsActive => MonitoringForegroundService.Running;
    public string Description => "Ohne Vordergrunddienst pausiert die Erfassung, sobald die App nicht sichtbar ist — Hintergrundzeit zählt dann nicht als Messwert. Der Dienst erlaubt Dauermonitoring mit dauerhafter Benachrichtigung und hält den Prozess wach. Android 15 beendet dataSync-Dienste nach etwa sechs Stunden; die Beendigung wird protokolliert und lässt sich hier neu starten.";

    public async Task<string> StartAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted) return "Ohne Benachrichtigungs-Berechtigung lässt Android keinen Vordergrunddienst zu. Es wurde nichts gestartet.";
        }
        MonitoringForegroundService.Start();
        monitor.SetBackgrounded(!AndroidAppLifecycle.IsForeground);
        return "Vordergrunddienst gestartet: Die Erfassung läuft auch ohne sichtbare App weiter.";
    }

    public Task<string> StopAsync()
    {
        MonitoringForegroundService.Stop();
        monitor.SetBackgrounded(!AndroidAppLifecycle.IsForeground);
        return Task.FromResult("Vordergrunddienst gestoppt. Im Hintergrund pausiert die Erfassung wieder.");
    }
}
