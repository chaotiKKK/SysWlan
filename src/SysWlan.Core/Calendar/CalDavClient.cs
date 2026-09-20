using System.Net;
using System.Net.Http.Headers;
using System.Security;

namespace SysWlan.Core;

public sealed class CalDavClient(HttpClient http, CalDavOptions options) : ICalendarSyncAdapter
{
    private const int MaxResponseBytes = 64 * 1024;
    public async Task<CalendarSyncResult[]> PushAsync(IReadOnlyList<CalendarSyncItem> items, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return items.Select(i => new CalendarSyncResult(i.ObjectId, SyncStatus.Failed, null, "CalDAV-Konfiguration unvollständig.")).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Options, CollectionUri());
        ApplyAuth(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return items.Select(i => new CalendarSyncResult(i.ObjectId, SyncStatus.Failed, null, "CalDAV-Anmeldung abgelehnt.")).ToArray();
        if ((int)response.StatusCode >= 500)
            return items.Select(i => new CalendarSyncResult(i.ObjectId, SyncStatus.Failed, null, "CalDAV-Serverfehler.")).ToArray();
        var results = new List<CalendarSyncResult>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await PutAsync(item, cancellationToken));
        }
        return results.ToArray();
    }

    private async Task<CalendarSyncResult> PutAsync(CalendarSyncItem item, CancellationToken cancellationToken)
    {
        var uri = new Uri(new Uri(CollectionUri()), item.Uid + ".ics");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri);
        ApplyAuth(request);
        request.Headers.TryAddWithoutValidation("If-Match", item.ETag ?? "*");
        request.Content = new StringContent(item.Content, System.Text.Encoding.UTF8, "text/calendar");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var etag = response.Headers.ETag?.Tag;
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.NoContent)
            return new(item.ObjectId, SyncStatus.Synced, etag, null);
        var error = response.StatusCode switch { HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "CalDAV-Anmeldung abgelehnt.", HttpStatusCode.NotFound => "CalDAV-Kalender nicht gefunden.", _ when (int)response.StatusCode >= 500 => "CalDAV-Serverfehler.", _ => "CalDAV-Übertragung fehlgeschlagen." };
        return new(item.ObjectId, SyncStatus.Failed, etag, error);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private string CollectionUri() => options.Url!.TrimEnd('/') + "/";
}
