using Android.Net;

namespace SysWlan.Android;

public enum AndroidTransport { Unknown, Wifi, Cellular, Ethernet, Vpn }

public static class AndroidTransportMapping
{
    public static AndroidTransport From(NetworkCapabilities? capabilities)
    {
        if (capabilities is null) return AndroidTransport.Unknown;
        if (capabilities.HasTransport(TransportType.Vpn)) return AndroidTransport.Vpn;
        if (capabilities.HasTransport(TransportType.Wifi)) return AndroidTransport.Wifi;
        if (capabilities.HasTransport(TransportType.Ethernet)) return AndroidTransport.Ethernet;
        if (capabilities.HasTransport(TransportType.Cellular)) return AndroidTransport.Cellular;
        return AndroidTransport.Unknown;
    }

    public static string Describe(AndroidTransport transport) => transport switch
    {
        AndroidTransport.Wifi => "WLAN",
        AndroidTransport.Cellular => "Mobilfunk",
        AndroidTransport.Ethernet => "Ethernet",
        AndroidTransport.Vpn => "VPN über der aktiven Verbindung",
        _ => "unbekanntes Transportmittel"
    };

    /// <summary>ConnectivityManager-Typnummer für NetworkStatsManager-Abfragen.</summary>
    public static int NetworkType(AndroidTransport transport) => transport switch
    {
        AndroidTransport.Wifi => (int)ConnectivityType.Wifi,
        AndroidTransport.Cellular => (int)ConnectivityType.Mobile,
        AndroidTransport.Ethernet => 9,
        AndroidTransport.Vpn => (int)ConnectivityType.Wifi,
        _ => -1
    };
}
