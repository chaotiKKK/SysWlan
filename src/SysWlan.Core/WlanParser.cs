namespace SysWlan.Core;
public static class WlanParser
{
    public static WlanInfo Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) values.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
        }
        string? Get(params string[] names) => names.Select(n => values.GetValueOrDefault(n)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        double? Number(string? value) => double.TryParse(value?.TrimEnd('%').Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
        return new()
        {
            Ssid = Get("SSID"), Bssid = Get("AP BSSID", "BSSID"), Band = Get("Band", "Bereich"), Channel = Get("Channel", "Kanal"),
            Authentication = Get("Authentication", "Authentifizierung"), Cipher = Get("Cipher", "Verschlüsselungsverfahren", "Verschlüsselung"),
            Radio = Get("Radio type", "Funktyp"), SignalPercent = (int?)Number(Get("Signal")), Rssi = (int?)Number(Get("RSSI")),
            ReceiveLinkMbps = Number(Get("Receive rate (Mbps)", "Empfangsrate (MBit/s)", "Empfangsrate (Mbit/s)")),
            SendLinkMbps = Number(Get("Transmit rate (Mbps)", "Übertragungsrate (MBit/s)", "Übertragungsrate (Mbit/s)"))
        };
    }
}
