using Android.App;
using Android.App.Usage;
using Android.Content;
using Android.Net;
using Android.OS;
using SysWlan.Core;

namespace SysWlan.Android;

/// <summary>Ergebnis einer Traffic-Abfrage. Fehlt die Berechtigung, bleiben die Werte null.</summary>
public sealed record DeviceTraffic(long? ReceivedBytes, long? SentBytes, string? Error);

/// <summary>
/// Geräteweiter Datenverkehr über NetworkStatsManager. Android bucht die Werte in Intervallen; es ist
/// deshalb eine Systemzählung des Geräts und kein Momentanwert. Ohne den Systemzugriff „Nutzungszugriff“
/// liefert die App bewusst keinen Zahlenwert statt einer Schätzung.
/// </summary>
public static class DeviceTrafficReader
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(2);

    public static DeviceTraffic Read(AndroidTransport transport, DateTimeOffset now)
    {
        if (transport == AndroidTransport.Unknown) return new(null, null, "Transportmittel unbekannt; geräteweiter Datenverkehr nicht abfragbar.");
        if (!AndroidNetworkAccess.HasUsageAccess()) return new(null, null, AndroidWifiMapping.UsageAccessWarning(false));
        var manager = Application.Context.GetSystemService(Context.NetworkStatsService) as NetworkStatsManager;
        if (manager is null) return new(null, null, "Android liefert keinen NetworkStatsManager.");
        var end = now.ToUnixTimeMilliseconds();
        var start = end - (long)Window.TotalMilliseconds;
        try
        {
            var bucket = manager.QuerySummaryForDevice((ConnectivityType)AndroidTransportMapping.NetworkType(transport), null, start, end);
            if (bucket is null) return new(null, null, "NetworkStatsManager lieferte keine Zusammenfassung für dieses Gerät.");
            return new(bucket.RxBytes, bucket.TxBytes, null);
        }
        catch (Exception ex)
        {
            return new(null, null, "Geräteweiter Datenverkehr nicht lesbar: " + SyslogParser.Redact(ex.Message));
        }
    }

    /// <summary>Eigene App-Zähler als klar gekennzeichneter Nebenwert, nie als Gerätesumme.</summary>
    public static (long Received, long Sent)? OwnAppTraffic()
    {
        try
        {
            var uid = Process.MyUid();
            var rx = TrafficStats.GetUidRxBytes(uid);
            var tx = TrafficStats.GetUidTxBytes(uid);
            if (rx == TrafficStats.Unsupported || tx == TrafficStats.Unsupported) return null;
            return (rx, tx);
        }
        catch (Exception) { return null; }
    }
}
