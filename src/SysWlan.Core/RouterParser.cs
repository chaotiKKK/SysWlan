namespace SysWlan.Core;
public static class RouterParser
{
    public static RouterStatus Parse(string html)
    {
        string? Read(string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(html, @"_ga\." + key + @"\s*=\s*'([^'\r\n]{0,160})'", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return match.Success ? match.Groups[1].Value : null;
        }
        bool? Flag(string key) => Read(key) is { } value ? value == "true" : null;
        return new()
        {
            Vendor = html.Contains("ARRIS", StringComparison.OrdinalIgnoreCase) ? "ARRIS / Vodafone" : html.Contains("FRITZ!", StringComparison.OrdinalIgnoreCase) ? "AVM FRITZ!Box" : "Unbekannt",
            Firmware = Read("swVersion"), ConnectionStatus = Read("modemConnectionStatus"), WanMode = Read("gwMode"),
            ModelHint = Flag("isModel6442") == true ? "6442 (Oberflächenkennung)" : null,
            WifiEnabled = Flag("isWifiEnabled"), BandSteering = Flag("isBandSteeringEnabled"), CheckedAt = DateTimeOffset.UtcNow
        };
    }
}
