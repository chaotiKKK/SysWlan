namespace SysWlan.Core;

public enum CalendarViewMode { Week, Day, Month }
public sealed record CalendarRange(DateTimeOffset FromUtc, DateTimeOffset ToUtc, CalendarViewMode Mode, DateOnly LocalAnchor);
