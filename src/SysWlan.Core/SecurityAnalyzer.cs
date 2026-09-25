namespace SysWlan.Core;

public static class SecurityAnalyzer
{
    public static SecurityFinding[] Analyze(NetworkSnapshot snapshot, RouterStatus router)
    {
        var items = new List<SecurityFinding>();
        if (!snapshot.Connected) return [new("Info", "Kein aktives Gateway", "Verbinde dieses Gerät mit einem Netzwerk, um Befunde zu erfassen.", "Netzwerk")];
        var auth = snapshot.Wlan.Authentication;
        if (auth is not null)
            items.Add(auth.Contains("WPA3", StringComparison.OrdinalIgnoreCase)
                ? new("OK", "WPA3 ausgehandelt", $"Die aktuelle WLAN-Verbindung verwendet {auth}. Das bewertet nicht die gesamte Routerkonfiguration.", "WLAN-Verbindung")
                : auth.Contains("WPA2", StringComparison.OrdinalIgnoreCase)
                    ? new("Info", "WPA2 ausgehandelt", "Falls alle Geräte es unterstützen, WPA3 in der Routeroberfläche prüfen.", "WLAN-Verbindung")
                    : new("Warnung", "WLAN-Sicherheit prüfen", $"Gemeldete Authentifizierung: {auth}. Moderne Verschlüsselung in der Routeroberfläche prüfen.", "WLAN-Verbindung"));
        if (router.CheckedAt is not null && router.Error is null)
            items.Add(new("Warnung", "Verwaltungsseite über HTTP erreichbar", "Die öffentliche Statusseite antwortet unverschlüsselt. Für eine Anmeldung nach Möglichkeit HTTPS am Router verwenden.", "Gateway HTTP"));
        if (snapshot.Wlan.Rssi is < -70)
            items.Add(new("Info", "Schwaches WLAN-Signal", $"Gemessen: {snapshot.Wlan.Rssi} dBm. Standort, Entfernung und Kanalbelegung prüfen.", "WLAN-Verbindung"));
        items.Add(new("Info", "Routerprüfung unvollständig", "Firewallregeln, Freigaben, Gastnetz-Isolation und Firmware-Sicherheitsstand sind ohne passende authentifizierte Routerquelle nicht bewertet.", "Funktionsumfang"));
        return items.ToArray();
    }
}
