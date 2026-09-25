using SysWlan.Core;

namespace SysWlan.App.Services;

/// <summary>
/// Alles, was die Ansichten auslösen und was plattformspezifisch ist: Routerbereich, Export,
/// Zwischenablage, Zugangsspeicher und Namensauflösung. Windows und Android setzen dieselben
/// Regeln um, nur mit anderen Mitteln.
/// </summary>
public interface IPlatformActions
{
    Task SaveCredentialAsync(string profileId, string username, string password);
    Task<bool> HasCredentialAsync(string profileId);
    void ForgetCredential(string profileId);
    Task<RouterConnectionTestResult> TestRouterConnectionAsync(string profileId, string username, string password, bool https, CancellationToken cancellationToken);
    Task<string> GeneratePasswordAsync();
    Task CopyToClipboardAsync(string value);
    Task OpenRouterAsync(string profileId, bool https);
    /// <summary>Exportiert die gespeicherten Daten und liefert die fertige Statusmeldung für die Oberfläche.</summary>
    Task<string> ExportAsync();
    Task<string> ExportCalendarAsync(DateOnly anchor, CalendarViewMode mode);
    Task<string> ResolveAsync(string host);
}

/// <summary>Woher die Messwerte kommen und was diese Plattform grundsätzlich nicht liefern kann.</summary>
public interface IPlatformInfo
{
    string PlatformLabel { get; }
    string DeviceName { get; }
    string DeviceLabel { get; }
    string DeviceShortLabel { get; }
    string FooterLabel { get; }
    string VersionLabel { get; }
    string TrafficNote { get; }
    string DnsNote { get; }
    CapabilityRow[] Capabilities { get; }
    string DevicesNote { get; }
    string ConnectionsNote { get; }
    string LogsNote { get; }
}

public sealed record CapabilityRow(string Feature, string Source, string Access);

/// <summary>Plattformberechtigungen, die die App wirklich braucht — und ihr Zustand.</summary>
public interface IPlatformPermissions
{
    bool IsRelevant { get; }
    string[] Rows();
    bool CanRequestWifiDetails { get; }
    bool NeedsTrafficAccess { get; }
    Task<string> RequestWifiDetailsAsync();
    Task<string> OpenTrafficAccessAsync();
}

/// <summary>Dauermonitoring im Hintergrund. Windows kennt diese Einschränkung nicht.</summary>
public interface IBackgroundMonitoring
{
    bool IsSupported { get; }
    bool IsActive { get; }
    string Description { get; }
    Task<string> StartAsync();
    Task<string> StopAsync();
}
