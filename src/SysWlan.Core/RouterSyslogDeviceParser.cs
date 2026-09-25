using System.Text.RegularExpressions;

namespace SysWlan.Core;

/// <summary>
/// Zieht Gerätehinweise aus Router-Syslog-Zeilen (DHCP-Vergabe, WLAN-Anmeldung).
/// Unter Android ist das die einzige Gerätequelle, weil die Nachbartabelle dort nicht lesbar ist.
/// Es werden nur MAC-Adressen gemeldet, die der Router selbst nennt.
/// </summary>
public static class RouterSyslogDeviceParser
{
    private static readonly Regex MacPattern = new(@"(?<![0-9a-fA-F])(?<mac>(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2})(?![0-9a-fA-F])", RegexOptions.Compiled);
    private static readonly Regex Ipv4Pattern = new(@"(?<![0-9.])(?<ip>\d{1,3}(?:\.\d{1,3}){3})(?![0-9.])", RegexOptions.Compiled);
    private static readonly Regex NamePattern = new(@"\((?<name>[^()]{1,63})\)", RegexOptions.Compiled);

    public static DeviceInfo? Parse(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var macMatch = MacPattern.Match(message);
        if (!macMatch.Success) return null;
        var mac = ProfileIdentity.NormalizeMac(macMatch.Value);
        if (!DeviceIdentity.IsValidMac(mac)) return null;

        var ip = Ipv4Pattern.Match(message);
        var address = ip.Success && IsPrivateOrLocal(ip.Groups["ip"].Value) ? ip.Groups["ip"].Value : "";

        var name = "";
        foreach (Match candidate in NamePattern.Matches(message))
        {
            var value = candidate.Groups["name"].Value.Trim();
            if (value.Length == 0 || MacPattern.IsMatch(value) || Ipv4Pattern.IsMatch(value)) continue;
            name = value;
            break;
        }
        return new DeviceInfo(address, mac, "Router-Syslog", name);
    }

    private static bool IsPrivateOrLocal(string address) =>
        System.Net.IPAddress.TryParse(address, out var parsed) && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
}
