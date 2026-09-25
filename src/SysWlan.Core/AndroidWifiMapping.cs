namespace SysWlan.Core;

/// <summary>
/// Bildet Android-Netzwerkmesswerte auf das gemeinsame Modell ab. Bewusst frei von Android-Typen,
/// damit jede Zuordnung ohne Gerät geprüft werden kann. Unbekanntes bleibt unbekannt.
/// </summary>
public static class AndroidWifiMapping
{
    public const string DevicesUnavailable = "Android liefert seit Android 10 keine Nachbartabelle (ARP) mehr. Geräte erscheinen nur, wenn der Router sie per Syslog meldet.";
    public const string ConnectionsUnavailable = "Android lässt keine Einsicht in die Verbindungen anderer Apps zu. Eigene Prozess- und Verbindungslisten sind hier nicht verfügbar.";
    public const string EventsNote = "Statt des Windows-Ereignisprotokolls protokolliert die App Android-Netzwerkereignisse (Verbindung verfügbar, verloren, geändert).";

    public static WlanInfo Map(string? ssid, string? bssid, int? frequencyMhz, int? rssiDbm, int? rxLinkSpeedMbps, int? txLinkSpeedMbps, int? wifiStandard, int? securityType) => new()
    {
        Ssid = CleanText(ssid),
        Bssid = CleanText(bssid),
        Band = Band(frequencyMhz),
        Channel = Channel(frequencyMhz),
        Authentication = Security(securityType),
        Cipher = null,
        Radio = Radio(wifiStandard),
        SignalPercent = SignalPercent(rssiDbm),
        Rssi = rssiDbm,
        ReceiveLinkMbps = rxLinkSpeedMbps,
        SendLinkMbps = txLinkSpeedMbps
    };

    /// <summary>Bereinigt Android-Platzhalter wie "0x" oder "&lt;unknown ssid&gt;" zu null.</summary>
    public static string? CleanText(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Equals("0x", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.Contains("<unknown", StringComparison.OrdinalIgnoreCase)) return null;
        return trimmed;
    }

    public static string? Band(int? frequencyMhz) => frequencyMhz switch
    {
        null or <= 0 => null,
        >= 2400 and <= 2500 => "2,4 GHz",
        >= 4900 and <= 5900 => "5 GHz",
        >= 5925 and <= 7125 => "6 GHz",
        _ => null
    };

    public static string? Channel(int? frequencyMhz)
    {
        if (frequencyMhz is null or <= 0) return null;
        var mhz = frequencyMhz.Value;
        int? channel = mhz switch
        {
            2484 => 14,
            >= 2412 and <= 2472 => (mhz - 2412) / 5 + 1,
            >= 5160 and <= 5885 => (mhz - 5000) / 5,
            // 6 GHz: nur echte 20-MHz-Mitten (1, 5, 9 …) gelten als Kanal, sonst bleibt es unbekannt.
            >= 5955 and <= 7115 when (mhz - 5955) % 20 == 0 => (mhz - 5950) / 5,
            _ => null
        };
        return channel is null or <= 0 ? null : channel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Schätzung aus dem RSSI (−100 dBm ⇒ 0 %, −50 dBm ⇒ 100 %). Kein gemessener Prozentwert.</summary>
    public static int? SignalPercent(int? rssiDbm) => rssiDbm is null ? null : Math.Clamp((rssiDbm.Value + 100) * 2, 0, 100);

    /// <summary>WifiInfo.getWifiStandard(): 0 unbekannt, 1 Legacy, 4 11n, 5 11ac, 6 11ax, 7 11ad, 8 11be.</summary>
    public static string? Radio(int? wifiStandard) => wifiStandard switch
    {
        1 => "802.11a/b/g (Legacy)",
        4 => "802.11n",
        5 => "802.11ac",
        6 => "802.11ax",
        7 => "802.11ad",
        8 => "802.11be",
        _ => null
    };

    /// <summary>WifiInfo.getCurrentSecurityType(): 0 offen, 1 WEP, 2 PSK, 3 EAP, 4 SAE, 5 OWE, 6/7 WAPI.</summary>
    public static string? Security(int? securityType) => securityType switch
    {
        0 => "Offen (unverschlüsselt)",
        1 => "WEP (veraltet)",
        2 => "WPA2-Personal (PSK)",
        3 => "WPA2/WPA3-Enterprise (EAP)",
        4 => "WPA3-Personal (SAE)",
        5 => "OWE (offen, verschlüsselt)",
        6 or 7 => "WAPI",
        _ => null
    };

    public static string? WifiDetailsWarning(bool detailsAllowed) => detailsAllowed
        ? null
        : "WLAN-Details wie SSID, BSSID und Kanal sind gesperrt: ab Android 13 braucht die App „In der Nähe befindliche Geräte“, bis Android 12 den Standortzugriff.";

    /// <summary>
    /// Standortschalter: Android verbirgt SSID und BSSID, solange er aus ist. Der Zustand wird vom
    /// Aufrufer gemeldet. Eine fehlende SSID allein beweist ihn nicht — sie fehlt auch, wenn das Gerät
    /// gar nicht als WLAN-Client verbunden ist (Gerätetest am 2026-09-25).
    /// </summary>
    public static string? LocationDisabledWarning(bool detailsAllowed, bool locationEnabled) =>
        detailsAllowed && !locationEnabled
            ? "Der Standortschalter des Geräts ist aus. Android verbirgt dann SSID und BSSID; mit aktivem Standort gibt Android die WLAN-Daten vollständig heraus."
            : null;

    public static string? UsageAccessWarning(bool usageAccessGranted) => usageAccessGranted
        ? null
        : "Geräteweiter Datenverkehr benötigt den Systemzugriff „Nutzungszugriff“. Ohne ihn zeigt die App keinen Verbrauch, statt einen falschen Wert zu schätzen.";

    /// <summary>Netzwerktopologie-Hinweis: die BSSID ist kein bewiesener Router, sondern die beobachtete Gegenstelle.</summary>
    public static string GatewaySourceWarning(string? source) => source == GatewayIdentityPolicy.ApBssidSource
        ? "Als Routerkennung dient hier die BSSID des WLAN-Accesspoints, weil Android keine Gateway-MAC herausgibt. Das ist eine Zuordnungshilfe, kein Nachweis der Routeridentität."
        : "";
}
