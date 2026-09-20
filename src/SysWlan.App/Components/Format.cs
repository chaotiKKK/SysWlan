namespace SysWlan.App.Components;
public static class Format
{
    public static string Value(object? value) => value?.ToString() is { Length: > 0 } text ? text : "—";
    public static string Rate(double? rate) => rate?.ToString("N2") ?? "—";
    public static string Bytes(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):N2} GiB" : bytes >= 1 << 20 ? $"{bytes / (double)(1 << 20):N1} MiB" : $"{bytes / 1024d:N1} KiB";
    public static string Time(DateTimeOffset? time) => time?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
    public static string Date(DateTimeOffset time) => time.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public static string Tone(string severity) => severity switch { "OK" => "good", "Warnung" => "warn", "Fehler" => "bad", _ => "muted" };
}
