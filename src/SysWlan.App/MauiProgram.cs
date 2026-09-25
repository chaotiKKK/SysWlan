using Microsoft.Extensions.Logging;
using SysWlan.Core;
using SysWlan.App.Services;
#if WINDOWS
using SysWlan.Windows;
#elif ANDROID
using SysWlan.Android;
#endif
namespace SysWlan.App;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton(_ => new Store(Path.Combine(FileSystem.AppDataDirectory, "syswlaninfo.db")));
        builder.Services.AddSingleton<RouterProbe>();
        builder.Services.AddSingleton<RouterConnectionTester>();
        builder.Services.AddSingleton<DeviceTimelineService>();
        builder.Services.AddSingleton<CalendarQueryService>();
        builder.Services.AddSingleton<IICalendarExporter, IcsExporter>();
        builder.Services.AddSingleton<CalDavConfigurationProvider>();
        builder.Services.AddSingleton<CalendarSyncRegistration>();
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        builder.Services.AddSingleton<ConfiguredCalDavAdapter>();
        builder.Services.AddSingleton<ICalendarSyncAdapter>(services => services.GetRequiredService<ConfiguredCalDavAdapter>());
        builder.Services.AddSingleton<CalendarSyncService>();
        builder.Services.AddSingleton<MonitorService>();
        builder.Services.AddSingleton<SyslogReceiver>();
#if WINDOWS
        builder.Services.AddSingleton<INetworkCollector, WindowsCollector>();
        builder.Services.AddSingleton<IPlatformActions, WindowsPlatformActions>();
        builder.Services.AddSingleton<IPlatformInfo, WindowsPlatformInfo>();
        builder.Services.AddSingleton<IPlatformPermissions, WindowsPlatformPermissions>();
        builder.Services.AddSingleton<IBackgroundMonitoring, WindowsBackgroundMonitoring>();
#elif ANDROID
        builder.Services.AddSingleton<NetworkEventsRecorder>();
        builder.Services.AddSingleton<RouterSyslogDeviceFeed>();
        builder.Services.AddSingleton<INetworkCollector, AndroidCollector>();
        builder.Services.AddSingleton<IPlatformActions, AndroidPlatformActions>();
        builder.Services.AddSingleton<IPlatformInfo, AndroidPlatformInfo>();
        builder.Services.AddSingleton<IPlatformPermissions, AndroidPlatformPermissions>();
        builder.Services.AddSingleton<IBackgroundMonitoring, AndroidBackgroundMonitoring>();
#endif
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
