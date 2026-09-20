namespace SysWlan.Core;

public sealed class CalendarQueryService(Store store)
{
    public CalendarSnapshot GetWeek(DateOnly anchor, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var daysSinceMonday = ((int)anchor.DayOfWeek + 6) % 7;
        var monday = anchor.AddDays(-daysSinceMonday);
        return Query(monday, monday.AddDays(7), CalendarViewMode.Week, zone);
    }

    public CalendarSnapshot GetDay(DateOnly anchor, TimeZoneInfo? zone = null) => Query(anchor, anchor.AddDays(1), CalendarViewMode.Day, zone ?? TimeZoneInfo.Local);

    public CalendarSnapshot GetMonth(DateOnly anchor, TimeZoneInfo? zone = null) => Query(new DateOnly(anchor.Year, anchor.Month, 1), new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1), CalendarViewMode.Month, zone ?? TimeZoneInfo.Local);

    public CalendarSnapshot Query(DateOnly fromLocal, DateOnly toLocal, CalendarViewMode mode, TimeZoneInfo zone)
    {
        var from = TimeZoneInfo.ConvertTimeToUtc(fromLocal.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        var to = TimeZoneInfo.ConvertTimeToUtc(toLocal.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        return store.QueryCalendar(new DateTimeOffset(from), new DateTimeOffset(to), true);
    }
}
