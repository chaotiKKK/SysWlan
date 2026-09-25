namespace SysWlan.Core;

/// <summary>Ergebnis der Routerkennung: MAC plus die Quelle, aus der sie stammt.</summary>
public sealed record GatewayIdentity(string? Mac, string? Source);

/// <summary>
/// Legt fest, welche Hardwarekennung ein Netzwerk als Routerprofil identifiziert.
/// Windows liest die echte Gateway-MAC; Android liefert keine und nutzt die WLAN-BSSID.
/// </summary>
public static class GatewayIdentityPolicy
{
    public const string GatewayMacSource = "gateway-mac";
    public const string ApBssidSource = "ap-bssid";

    public static GatewayIdentity Resolve(string? gatewayMac, string? apBssid)
    {
        if (IsUsable(gatewayMac)) return new(gatewayMac, GatewayMacSource);
        if (IsUsable(apBssid)) return new(apBssid, ApBssidSource);
        return new(null, null);
    }

    public static bool IsUsable(string? mac)
    {
        var normalized = ProfileIdentity.NormalizeMac(mac);
        return normalized.Length == 12 && normalized is not ("000000000000" or "FFFFFFFFFFFF");
    }

    public static string Describe(string? source) => source switch
    {
        GatewayMacSource => "Gateway-MAC, direkt an der Netzwerkschnittstelle gelesen.",
        ApBssidSource => "BSSID des WLAN-Accesspoints als Ersatz für die Gateway-MAC; das ist eine Zuordnungshilfe, kein Nachweis der Routeridentität.",
        _ => "Keine belastbare Routerkennung, deshalb entsteht ein vorläufiges Profil und der Routerbereich bleibt gesperrt."
    };
}
