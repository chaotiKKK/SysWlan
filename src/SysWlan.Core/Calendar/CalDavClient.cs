using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SysWlan.Core;

public sealed class CalDavClient(HttpClient http, CalDavOptions options) : ICalendarSyncAdapter
{
    public async Task<CalendarSyncResult[]> PushAsync(IReadOnlyList<CalendarSyncItem> items, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return items.Select(i => Failed(i, "CalDAV-Konfiguration unvollständig.")).ToArray();
        var capability = await EnsureCollectionAsync(cancellationToken);
        if (capability is not null) return items.Select(i => Failed(i, capability)).ToArray();
        var results = new List<CalendarSyncResult>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(item.Content.Length == 0 ? await DeleteAsync(item, cancellationToken) : await PutAsync(item, cancellationToken));
        }
        return results.ToArray();
    }

    private async Task<string?> EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), CollectionUri());
        request.Headers.Add("Depth", "0");
        ApplyAuth(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return "CalDAV-Anmeldung abgelehnt.";
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            using var create = new HttpRequestMessage(new HttpMethod("MKCALENDAR"), CollectionUri());
            ApplyAuth(create);
            create.Content = new StringContent($"<?xml version=\"1.0\" encoding=\"utf-8\"?><mkcalendar xmlns=\"urn:ietf:params:xml:ns:caldav\"><set><prop><displayname xmlns=\"DAV:\">{EscapeXml(options.CalendarName)}</displayname></prop></set></mkcalendar>", Encoding.UTF8, "application/xml");
            using var created = await http.SendAsync(create, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (created.IsSuccessStatusCode || created.StatusCode == HttpStatusCode.MethodNotAllowed) return null;
            return ErrorFor(created.StatusCode, "CalDAV-Kalender konnte nicht angelegt werden.");
        }
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MultiStatus) return null;
        return ErrorFor(response.StatusCode, "CalDAV-Kalender konnte nicht geprüft werden.");
    }

    private async Task<CalendarSyncResult> PutAsync(CalendarSyncItem item, CancellationToken cancellationToken)
    {
        var uri = new Uri(new Uri(CollectionUri()), Uri.EscapeDataString(item.Uid) + ".ics");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri);
        ApplyAuth(request);
        if (string.IsNullOrWhiteSpace(item.ETag)) request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        else request.Headers.TryAddWithoutValidation("If-Match", item.ETag);
        request.Content = new StringContent(item.Content, Encoding.UTF8, "text/calendar");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode) return new(item.ObjectId, SyncStatus.Synced, response.Headers.ETag?.Tag, null);
        return Failed(item, ErrorFor(response.StatusCode, "CalDAV-Übertragung fehlgeschlagen."), response.Headers.ETag?.Tag);
    }

    private async Task<CalendarSyncResult> DeleteAsync(CalendarSyncItem item, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(new Uri(CollectionUri()), Uri.EscapeDataString(item.Uid) + ".ics"));
        ApplyAuth(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return new(item.ObjectId, SyncStatus.Synced, null, null);
        return Failed(item, ErrorFor(response.StatusCode, "CalDAV-Löschen fehlgeschlagen."));
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private string CollectionUri() => options.Url!.TrimEnd('/') + "/";
    private static CalendarSyncResult Failed(CalendarSyncItem item, string error, string? etag = null) => new(item.ObjectId, SyncStatus.Failed, etag, error);
    private static string ErrorFor(HttpStatusCode status, string fallback) => status switch { HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "CalDAV-Anmeldung abgelehnt.", HttpStatusCode.NotFound => "CalDAV-Kalender nicht gefunden.", _ when (int)status >= 500 => "CalDAV-Serverfehler.", _ => fallback };
    private static string EscapeXml(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal);
}
