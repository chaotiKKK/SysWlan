using System.Security.Cryptography;
using System.Text;

namespace SysWlan.Core;

public static class DeviceIdentity
{
    private static readonly string[] Palette = ["#73E3DC", "#87C8FF", "#FFD27A", "#C6A7FF", "#FF9F96", "#A8E6A3", "#F3A7D5", "#B7C7FF"];

    public static string SourceKey(DeviceInfo device, NetworkSnapshot snapshot)
    {
        var mac = NormalizeMac(device.Mac);
        if (IsValidMac(mac)) return "mac:" + mac;
        var address = device.Address.Trim();
        return $"provisional:{snapshot.InterfaceId}|{address}";
    }

    public static string LocalSourceKey(string? mac, string interfaceId)
    {
        var normalized = NormalizeMac(mac);
        return IsValidMac(normalized) ? "local:" + normalized : "local:" + interfaceId.Trim();
    }

    public static string NormalizeMac(string? mac) => new((mac ?? "").Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValidMac(string mac) => mac.Length == 12 && mac != "000000000000" && mac != "FFFFFFFFFFFF";

    public static string AutomaticColor(string sourceKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey ?? ""));
        return Palette[BitConverter.ToUInt16(hash, 0) % Palette.Length];
    }

    public static bool IsValidColor(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#') return false;
        return value.Skip(1).All(Uri.IsHexDigit);
    }

    public static string ResolveColor(DeviceProfile profile, string sourceKey) =>
        IsValidColor(profile.ManualColor) ? profile.ManualColor!.ToUpperInvariant() : AutomaticColor(sourceKey);
}
