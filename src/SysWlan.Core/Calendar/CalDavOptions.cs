namespace SysWlan.Core;

public sealed record CalDavOptions(string? Url, string? Username, string? Password, string CalendarName = "Netzwerkgeräte")
{
    public bool Enabled => Uri.TryCreate(Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(CalendarName);
    public string ToSafeString() => $"URL={Url ?? "nicht gesetzt"}; Kalender={CalendarName}; Benutzer={(string.IsNullOrWhiteSpace(Username) ? "nicht gesetzt" : "gesetzt")}; Passwort={(string.IsNullOrWhiteSpace(Password) ? "nicht gesetzt" : "gesetzt")}";
}
