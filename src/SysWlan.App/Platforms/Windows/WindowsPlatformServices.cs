using SysWlan.App.Services;

namespace SysWlan.App;

/// <summary>Windows braucht keine Laufzeitberechtigungen und kennt keine Hintergrundsperre.</summary>
public sealed class WindowsPlatformPermissions : IPlatformPermissions
{
    public bool IsRelevant => false;
    public string[] Rows() => [];
    public bool CanRequestWifiDetails => false;
    public bool NeedsTrafficAccess => false;
    public Task<string> RequestWifiDetailsAsync() => Task.FromResult("Windows benötigt für diese Werte keine Laufzeitberechtigung.");
    public Task<string> OpenTrafficAccessAsync() => Task.FromResult("Windows benötigt für diese Werte keine Laufzeitberechtigung.");
}

public sealed class WindowsBackgroundMonitoring : IBackgroundMonitoring
{
    public bool IsSupported => false;
    public bool IsActive => false;
    public string Description => "Unter Windows läuft die Erfassung im eigenen Prozess weiter; ein Android-Vordergrunddienst ist hier nicht nötig.";
    public Task<string> StartAsync() => Task.FromResult(Description);
    public Task<string> StopAsync() => Task.FromResult(Description);
}

public sealed class WindowsPlatformInfo : IPlatformInfo
{
    public string PlatformLabel => "Windows";
    public string DeviceName => Environment.MachineName;
    public string DeviceLabel => "Dieser Windows-PC";
    public string DeviceShortLabel => "dieser PC";
    public string FooterLabel => "Windows-Erfassung · lokale Speicherung";
    public string VersionLabel => $"Windows · Version {AppInfo.Current.VersionString}";
    public string TrafficNote => "Windows-Schnittstellenzähler dieses PCs · gemessene Zeiträume";
    public string DnsNote => "Eine gezielte Anfrage über den Namensauflöser von Windows.";
    public CapabilityRow[] Capabilities =>
    [
        new("WLAN, IP, DNS, Linkraten", "Windows", "Ohne Routeranmeldung"),
        new("Lokaler Traffic dieses PCs", "Windows-Schnittstellenzähler", "Keine geräteübergreifende Messung"),
        new("Geräte-Nachbartabelle", "Windows", "Unvollständig; „Stale“ ist kein Offline-Nachweis"),
        new("Verbindungen und Prozesse", "Windows", "Momentaufnahme dieses PCs mit PID"),
        new("Firmware & DOCSIS-Status", "Öffentliche Routerseite", "Firmwareabhängig verfügbar"),
        new("Gastnetz, WLAN-Konfiguration, vollständige Clients", "Integrierte Routeroberfläche", "Routeranmeldung erforderlich"),
        new("Automatisierte Routeränderungen", "Router-API", "Noch kein validierter Schreibadapter"),
        new("Router-Syslog", "UDP-Empfänger", "Versand im Router erforderlich")
    ];
    public string DevicesNote => "Windows kennt Geräte, mit denen es im lokalen Netz Kontakt hatte. „Stale“ bedeutet zuletzt beobachtet. Dies ist keine vollständige Router-Clientliste; Traffic anderer Geräte ist nicht verfügbar.";
    public string ConnectionsNote => "Anzeige maximal 300 Treffer; Erfassung maximal 1024 TCP-Verbindungen und 512 UDP-Endpunkte. UDP-Endpunkte liefern keine entfernte Adresse. Ein offener Port ist allein kein Sicherheitsbefund.";
    public string LogsNote => "Zeitangaben bei Syslog zeigen den Empfangszeitpunkt. UDP-Syslog authentifiziert den Absender nicht. Windows-Ereignisse und App-Meldungen sind getrennt gekennzeichnet.";
}
