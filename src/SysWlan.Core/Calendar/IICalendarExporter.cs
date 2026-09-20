namespace SysWlan.Core;

public interface IICalendarExporter
{
    string Export(CalendarSnapshot snapshot);
}
