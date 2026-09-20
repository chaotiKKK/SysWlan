namespace SysWlan.Core;
public static class ProfileIdentity
{
    public static string Key(NetworkSnapshot snapshot)
    {
        var mac = NormalizeMac(snapshot.GatewayMac);
        var identity = mac.Length == 12 && mac != "000000000000" && mac != "FFFFFFFFFFFF"
            ? $"router:{mac}"
            : $"provisional:{snapshot.Gateway}|{snapshot.Ssid}|{snapshot.InterfaceId}|{snapshot.Wlan.Bssid}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..24];
    }
    public static string NormalizeMac(string? mac) => new((mac ?? "").Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
