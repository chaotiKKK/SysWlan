using System.Text.Json;

namespace SysWlan.Core;

public sealed class CalDavConfigurationProvider
{
    public CalDavOptions Load(string filePath, IReadOnlyDictionary<string, string?>? environment = null)
    {
        CalDavFile file = new();
        if (File.Exists(filePath))
        {
            try { file = JsonSerializer.Deserialize<CalDavFile>(File.ReadAllText(filePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
            catch (JsonException ex) { throw new InvalidDataException("CalDAV-Konfiguration ist kein gültiges JSON.", ex); }
        }
        environment ??= Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().Where(e => e.Key is string).ToDictionary(e => (string)e.Key, e => e.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        return new(
            Override(file.Url, environment, "SYSWLAN_CALDAV_URL"),
            Override(file.Username, environment, "SYSWLAN_CALDAV_USERNAME"),
            Override(file.Password, environment, "SYSWLAN_CALDAV_PASSWORD"),
            Override(file.Calendar, environment, "SYSWLAN_CALDAV_CALENDAR") ?? "Netzwerkgeräte");
    }

    public void Save(string filePath, CalDavOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(new CalDavFile(options.Url, options.Username, options.Password, options.CalendarName), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? Override(string? fileValue, IReadOnlyDictionary<string, string?> environment, string key) => environment.TryGetValue(key, out var value) && value is not null ? value : fileValue;
    private sealed record CalDavFile(string? Url = null, string? Username = null, string? Password = null, string? Calendar = null);
}
