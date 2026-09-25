using System.Net;
using System.Net.NetworkInformation;
using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using SysWlan.Core;

namespace SysWlan.Android;

/// <summary>WLAN-Werte, wie Android sie herausgibt. Fehlende Berechtigungen führen zu null-Feldern.</summary>
public sealed record AndroidWifiReading(string? Ssid, string? Bssid, int? FrequencyMhz, int? RssiDbm, int? RxLinkSpeedMbps, int? TxLinkSpeedMbps, int? WifiStandard, int? SecurityType, string? LocalMac);

/// <summary>
/// Erfassung unter Android: Verbindungszustand, Adressen, Gateway, WLAN-Details, geräteweiter
/// Datenverkehr und Netzwerkereignisse. Was Android nicht meldet, bleibt leer und wird benannt;
/// es wird nichts geschätzt und keine Nachbartabelle erfunden.
/// </summary>
public sealed class AndroidCollector(NetworkEventsRecorder events, RouterSyslogDeviceFeed syslogDevices) : INetworkCollector
{
    /// <summary>Android meldet diesen Wert, wenn die MAC aus Datenschutzgründen verborgen bleibt.</summary>
    public const string AnonymizedMac = "02:00:00:00:00:00";
    public const int UnknownRssi = -127;

    public async Task<NetworkSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        // Die Rückrufe liefern die Netzwerkereignisse; die Registrierung passiert bei der ersten Erfassung.
        events.Start();
        var pending = events.Drain();
        var errors = new List<string>();
        var context = Application.Context;
        var connectivity = context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        if (connectivity is null) return new NetworkSnapshot { Timestamp = timestamp, Events = pending, Errors = ["Android liefert keinen ConnectivityManager."] };
        var network = connectivity.ActiveNetwork;
        var capabilities = network is null ? null : connectivity.GetNetworkCapabilities(network);
        var link = network is null ? null : connectivity.GetLinkProperties(network);
        if (link is null)
            return new NetworkSnapshot { Timestamp = timestamp, Events = pending, Errors = ["Kein aktives Netzwerk. Android meldet erst nach einer Verbindung Messwerte."] };

        var transport = AndroidTransportMapping.From(capabilities);
        var gateway = DefaultGateway(link);
        var wifi = ReadWifi(context, capabilities, errors);
        var wlan = wifi is null
            ? new WlanInfo()
            : AndroidWifiMapping.Map(wifi.Ssid, wifi.Bssid, wifi.FrequencyMhz, wifi.RssiDbm, wifi.RxLinkSpeedMbps, wifi.TxLinkSpeedMbps, wifi.WifiStandard, wifi.SecurityType);

        if (!AndroidNetworkAccess.HasWifiDetails()) errors.Add(AndroidWifiMapping.WifiDetailsWarning(false)!);
        else if (AndroidWifiMapping.LocationDisabledWarning(true, AndroidNetworkAccess.LocationEnabled()) is { } location) errors.Add(location);

        var traffic = DeviceTrafficReader.Read(transport, timestamp);
        if (traffic.Error is not null) errors.Add(traffic.Error);
        var own = DeviceTrafficReader.OwnAppTraffic();
        var identity = GatewayIdentityPolicy.Resolve(null, wifi?.Bssid);
        if (identity.Source == GatewayIdentityPolicy.ApBssidSource) errors.Add(AndroidWifiMapping.GatewaySourceWarning(identity.Source));

        return new NetworkSnapshot
        {
            Timestamp = timestamp,
            InterfaceId = link.InterfaceName ?? transport.ToString(),
            InterfaceName = link.InterfaceName ?? "",
            Description = $"{AndroidTransportMapping.Describe(transport)} · {link.InterfaceName}",
            Gateway = gateway,
            GatewayMac = identity.Mac,
            GatewayMacSource = identity.Source,
            LocalMac = wifi?.LocalMac,
            Ssid = wlan.Ssid,
            Addresses = Addresses(link),
            DnsServers = DnsServers(link),
            ReceivedBytes = traffic.ReceivedBytes ?? 0,
            SentBytes = traffic.SentBytes ?? 0,
            AppReceivedBytes = own?.Received,
            AppSentBytes = own?.Sent,
            LinkSpeed = (long)(wlan.ReceiveLinkMbps ?? 0) * 1_000_000,
            GatewayLatencyMs = await GatewayLatencyAsync(gateway, cancellationToken),
            Wlan = wlan,
            Devices = syslogDevices.RecentDevices(timestamp),
            Connections = [],
            Events = pending,
            Errors = [.. errors]
        };
    }

    private static AndroidWifiReading? ReadWifi(Context context, NetworkCapabilities? capabilities, List<string> errors)
    {
        if (capabilities is null || !capabilities.HasTransport(TransportType.Wifi)) return null;
        WifiInfo? info = null;
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) info = capabilities.TransportInfo as WifiInfo;
        info ??= LegacyConnectionInfo(context, errors);
        if (info is null) return null;
        var ssid = Attempt(() => info.SSID, errors, "SSID");
        var bssid = Attempt(() => info.BSSID, errors, "BSSID");
        return new(
            AndroidWifiMapping.CleanText(ssid),
            AndroidWifiMapping.CleanText(bssid),
            FrequencyOf(info, errors),
            RssiOf(info, errors),
            RxLinkSpeedOf(info, errors),
            TxLinkSpeedOf(info, errors),
            WifiStandardOf(info, errors),
            SecurityTypeOf(info, errors),
            LocalMacOf(info, errors));
    }

    /// <summary>Nur für Android 8 bis 10 nötig: ab API 29 liefert TransportInfo dieselben Werte.</summary>
    private static WifiInfo? LegacyConnectionInfo(Context context, List<string> errors)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
#pragma warning disable CA1422 // WifiManager.ConnectionInfo ist nur ab API 31 veraltet und wird hier nur unterhalb von API 29 gelesen.
        try { return (context.GetSystemService(Context.WifiService) as WifiManager)?.ConnectionInfo; }
        catch (Exception ex) { errors.Add("WLAN-Verbindungsinformation nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
#pragma warning restore CA1422
    }

    private static int? FrequencyOf(WifiInfo info, List<string> errors)
    {
        try { return info.Frequency <= 0 ? null : info.Frequency; }
        catch (Exception ex) { errors.Add("Frequenz nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
    }

    private static int? RssiOf(WifiInfo info, List<string> errors)
    {
        try { return info.Rssi <= UnknownRssi ? null : info.Rssi; }
        catch (Exception ex) { errors.Add("Signalstärke nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
    }

    private static int? RxLinkSpeedOf(WifiInfo info, List<string> errors)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
        try { return info.RxLinkSpeedMbps <= 0 ? null : info.RxLinkSpeedMbps; }
        catch (Exception ex) { errors.Add("Empfangsrate nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
    }

    private static int? TxLinkSpeedOf(WifiInfo info, List<string> errors)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return null;
        try { return info.TxLinkSpeedMbps <= 0 ? null : info.TxLinkSpeedMbps; }
        catch (Exception ex) { errors.Add("Senderate nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
    }

    private static int? WifiStandardOf(WifiInfo info, List<string> errors)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return null;
        try { return info.WifiStandard; }
        catch (Exception ex) { errors.Add("Funkstandard nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
    }

    private static int? SecurityTypeOf(WifiInfo info, List<string> errors)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return null;
        try { return info.CurrentSecurityType; }
        catch (Exception ex) { errors.Add("Sicherheitstyp nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
    }

    private static string? LocalMacOf(WifiInfo info, List<string> errors)
    {
        try
        {
            var mac = info.MacAddress;
            if (mac is null || mac == AnonymizedMac) return null;
            return DeviceIdentity.IsValidMac(DeviceIdentity.NormalizeMac(mac)) ? mac : null;
        }
        catch (Exception ex) { errors.Add("Geräte-MAC nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
    }

    private static string? Attempt(Func<string?> read, List<string> errors, string what)
    {
        try { return read(); }
        catch (Exception ex) { errors.Add($"{what} nicht lesbar: " + SyslogParser.Redact(ex.Message)); return null; }
    }

    private static string[] Addresses(LinkProperties link)
    {
        var values = new List<string>();
        foreach (var address in link.LinkAddresses ?? []) if (address.Address?.HostAddress is { Length: > 0 } host) values.Add(host);
        return [.. values];
    }

    private static string[] DnsServers(LinkProperties link)
    {
        var values = new List<string>();
        foreach (var server in link.DnsServers ?? []) if (server.HostAddress is { Length: > 0 } host) values.Add(host);
        return [.. values];
    }

    /// <summary>Default-Route aus den LinkProperties. Android gibt keine Gateway-MAC heraus, nur die Adresse.</summary>
    public static string? DefaultGateway(LinkProperties link)
    {
        foreach (var route in link.Routes ?? [])
        {
            if (route.Gateway?.HostAddress is not { Length: > 0 } address) continue;
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                if (route.IsDefaultRoute) return address;
            }
            if (route.Destination is null || route.Destination.PrefixLength == 0) return address;
        }
        return null;
    }

    private static async Task<long?> GatewayLatencyAsync(string? gateway, CancellationToken cancellationToken)
    {
        if (gateway is null || !IPAddress.TryParse(gateway, out var address)) return null;
        try
        {
            using var ping = new Ping();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(1), cancellationToken: timeout.Token);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception) { return null; }
    }
}
