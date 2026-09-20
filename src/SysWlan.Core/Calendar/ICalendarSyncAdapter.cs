namespace SysWlan.Core;

public interface ICalendarSyncAdapter
{
    Task<CalendarSyncResult[]> PushAsync(IReadOnlyList<CalendarSyncItem> items, CancellationToken cancellationToken);
}
