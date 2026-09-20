using SysWlan.Core;

namespace SysWlan.App.Services;

public sealed class ConfiguredCalDavAdapter(CalendarSyncRegistration registration, HttpClient http) : ICalendarSyncAdapter
{
    public async Task<CalendarSyncResult[]> PushAsync(IReadOnlyList<CalendarSyncItem> items, CancellationToken cancellationToken)
    {
        CalDavOptions options;
        try { options = registration.Load(); }
        catch (Exception ex) { return items.Select(item => new CalendarSyncResult(item.ObjectId, SyncStatus.Failed, null, SyslogParser.Redact(ex.Message))).ToArray(); }
        return await new CalDavClient(http, options).PushAsync(items, cancellationToken);
    }
}
