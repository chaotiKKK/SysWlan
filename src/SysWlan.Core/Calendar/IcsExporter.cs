using System.Globalization;
using System.Text;

namespace SysWlan.Core;

public sealed class IcsExporter : IICalendarExporter
{
    public string Export(CalendarSnapshot snapshot)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//SysWLANInfo//Gerätekalender//DE",
            "CALSCALE:GREGORIAN",
            "X-WR-CALNAME:Netzwerkgeräte"
        };
        var devices = snapshot.Devices.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var sources = snapshot.Devices.SelectMany(d => d.SourceKeys.Select(source => (source, device: d))).ToDictionary(x => x.source, x => x.device, StringComparer.Ordinal);
        foreach (var interval in snapshot.Presence.Where(p => !p.IsOpen && p.End is not null).OrderBy(p => p.Start).ThenBy(p => p.Id))
        {
            var device = sources.GetValueOrDefault(interval.SourceKey);
            var name = device?.Name ?? "Unbekanntes Gerät";
            var color = device?.Color ?? DeviceIdentity.AutomaticColor(interval.SourceKey);
            var activity = snapshot.Activity.Where(a => a.SourceKey == interval.SourceKey && a.Start < interval.End && a.End > interval.Start).OrderBy(a => a.Start).FirstOrDefault();
            var description = activity is null || activity.Availability == ActivityAvailability.Unavailable
                ? "Aktivität: nicht verfügbar"
                : $"Aktivität: Empfang {activity.ReceiveMbps?.ToString("N2", CultureInfo.InvariantCulture) ?? "–"} MBit/s, Senden {activity.SendMbps?.ToString("N2", CultureInfo.InvariantCulture) ?? "–"} MBit/s ({activity.Source})";
            var uid = $"presence:{interval.SourceKey}:{interval.Id}@syswlaninfo.local";
            lines.AddRange(EventLines(uid, $"{name} · Anwesenheit", description, interval.Start, interval.End!.Value, ["Anwesenheit", name], color));
        }
        foreach (var entry in snapshot.Events.OrderBy(e => e.Timestamp).ThenBy(e => e.Id))
        {
            var device = sources.GetValueOrDefault(entry.SourceKey);
            var name = device?.Name ?? "Unbekanntes Gerät";
            var uid = $"event:{entry.SourceKey}:{entry.Id}@syswlaninfo.local";
            var end = entry.Timestamp.AddMinutes(1);
            lines.AddRange(EventLines(uid, $"{name} · {EventLabel(entry.Type)}", $"{entry.Message} (Quelle: {entry.Source})", entry.Timestamp, end, ["Ereignis", entry.Type.ToString(), name], device?.Color ?? DeviceIdentity.AutomaticColor(entry.SourceKey)));
        }
        lines.Add("END:VCALENDAR");
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static IEnumerable<string> EventLines(string uid, string summary, string description, DateTimeOffset start, DateTimeOffset end, string[] categories, string color)
    {
        yield return "BEGIN:VEVENT";
        yield return "UID:" + Escape(uid);
        yield return "DTSTAMP:" + Date(start);
        yield return "DTSTART:" + Date(start);
        yield return "DTEND:" + Date(end);
        yield return "SUMMARY:" + Escape(summary);
        yield return "DESCRIPTION:" + Escape(description);
        yield return "CATEGORIES:" + string.Join(',', categories.Select(Escape));
        yield return "COLOR:" + Escape(color);
        yield return "X-APPLE-CALENDAR-COLOR:" + Escape(color);
        yield return "END:VEVENT";
    }

    private static string Date(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    private static string Escape(string value) => (value ?? "").Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\n", StringComparison.Ordinal);
    private static string EventLabel(DeviceEventType type) => type switch { DeviceEventType.FirstObserved => "Neu erkannt", DeviceEventType.ProfileChanged => "Profilwechsel", DeviceEventType.Absence => "Längere Abwesenheit", DeviceEventType.CollectionError => "Erfassungsfehler", _ => "Netzwerkereignis" };
}
