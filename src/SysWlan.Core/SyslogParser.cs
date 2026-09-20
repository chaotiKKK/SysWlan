namespace SysWlan.Core;
public static class SyslogParser
{
    public static LogEntry Parse(string text, string sender, DateTimeOffset timestamp)
    {
        var level = "Info";
        if (text.StartsWith('<') && text.IndexOf('>') is > 1 and < 5)
        {
            var end = text.IndexOf('>');
            if (int.TryParse(text.AsSpan(1, end - 1), out var priority) && priority is >= 0 and <= 191)
            {
                level = (priority % 8) switch { <= 3 => "Fehler", 4 => "Warnung", 7 => "Debug", _ => "Info" };
                text = text[(end + 1)..];
            }
        }
        return new(timestamp, $"Syslog {sender}", level, Redact(text));
    }
    public static string Redact(string text)
    {
        var clean = new string(text.Take(8192).Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
        return System.Text.RegularExpressions.Regex.Replace(clean,
            "(?im)([\"']?\\b(?:password|passwort|passwd|pwd|token|authorization|cookie|session[_-]?id|currentsessionid|csrfnonce)\\b[\"']?\\s*[:=])[^\\r\\n]*", "$1[entfernt]",
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
    }
}
