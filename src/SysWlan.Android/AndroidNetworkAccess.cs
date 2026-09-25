using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace SysWlan.Android;

/// <summary>
/// Liest den Berechtigungszustand, ohne Dialoge zu öffnen: das Erbitten gehört in die Oberfläche,
/// damit der Nutzer die Entscheidung sieht. Fehlende Berechtigungen werden nie umgangen.
/// </summary>
public static class AndroidNetworkAccess
{
    public const string UsageAccessOp = "android:get_usage_stats";

    public static bool HasWifiDetails() => HasWifiDetails(Application.Context);

    public static bool HasWifiDetails(Context context)
    {
        var permission = OperatingSystem.IsAndroidVersionAtLeast(33) ? Manifest.Permission.NearbyWifiDevices : Manifest.Permission.AccessFineLocation;
        return context.CheckSelfPermission(permission) == Permission.Granted;
    }

    public static bool HasUsageAccess()
    {
        try
        {
            var context = Application.Context;
            var appOps = context.GetSystemService(Context.AppOpsService) as AppOpsManager;
            if (appOps is null) return false;
            var mode = appOps.CheckOpNoThrow(UsageAccessOp, Process.MyUid(), context.PackageName!);
            return mode == AppOpsManagerMode.Allowed;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Standortschalter des Geräts: ohne ihn verbirgt Android SSID und BSSID.</summary>
    public static bool LocationEnabled()
    {
        try
        {
            var context = Application.Context;
            var manager = context.GetSystemService(Context.LocationService) as global::Android.Locations.LocationManager;
            if (manager is null) return false;
            if (OperatingSystem.IsAndroidVersionAtLeast(28)) return manager.IsLocationEnabled;
            return global::Android.Provider.Settings.Secure.GetInt(context.ContentResolver, global::Android.Provider.Settings.Secure.LocationMode, 0) != 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Name der Berechtigung, die WLAN-Details freigibt.</summary>
    public static string WifiPermissionName() => OperatingSystem.IsAndroidVersionAtLeast(33) ? "In der Nähe befindliche Geräte" : "Standort";
}
