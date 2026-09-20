using SysWlan.Core;

namespace SysWlan.App.Services;

public sealed class CalendarSyncRegistration(CalDavConfigurationProvider configuration)
{
    public string ConfigurationPath => Path.Combine(FileSystem.AppDataDirectory, "caldav.json");
    public CalDavOptions Load() => configuration.Load(ConfigurationPath);
    public string SafeStatus() => Load().ToSafeString();
}
