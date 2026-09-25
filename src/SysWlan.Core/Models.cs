namespace SysWlan.Core;

public sealed record NetworkSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string InterfaceId { get; init; } = "";
    public string InterfaceName { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Gateway { get; init; }
    public string? GatewayMac { get; init; }
    /// <summary>Quelle der Routerkennung: "gateway-mac" (Windows) oder "ap-bssid" (Android, ohne Gateway-MAC).</summary>
    public string? GatewayMacSource { get; init; }
    public string? LocalMac { get; init; }
    public string? Ssid { get; init; }
    public string[] Addresses { get; init; } = [];
    public string[] DnsServers { get; init; } = [];
    public long ReceivedBytes { get; init; }
    public long SentBytes { get; init; }
    /// <summary>Eigene App-Zähler, nur dort gefüllt, wo die Gerätesumme nicht verfügbar ist (Android ohne Nutzungszugriff).</summary>
    public long? AppReceivedBytes { get; init; }
    public long? AppSentBytes { get; init; }
    public long LinkSpeed { get; init; }
    public long? GatewayLatencyMs { get; init; }
    public WlanInfo Wlan { get; init; } = new();
    public DeviceInfo[] Devices { get; init; } = [];
    public ConnectionInfo[] Connections { get; init; } = [];
    public LogEntry[] Events { get; init; } = [];
    public string[] Errors { get; init; } = [];
    public bool Connected => !string.IsNullOrWhiteSpace(Gateway);
}
public sealed record WlanInfo
{
    public string? Ssid { get; init; }
    public string? Bssid { get; init; }
    public string? Band { get; init; }
    public string? Channel { get; init; }
    public string? Authentication { get; init; }
    public string? Cipher { get; init; }
    public string? Radio { get; init; }
    public int? SignalPercent { get; init; }
    public int? Rssi { get; init; }
    public double? ReceiveLinkMbps { get; init; }
    public double? SendLinkMbps { get; init; }
}
public sealed record DeviceInfo(string Address, string Mac, string State, string Name);
public sealed record ConnectionInfo(string Protocol, string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, string State, int ProcessId, string ProcessName);
public sealed record LogEntry(DateTimeOffset Timestamp, string Source, string Severity, string Message);
public sealed record TrafficSample(DateTimeOffset Timestamp, double? ReceiveMbps, double? SendMbps, long ReceivedDelta, long SentDelta);
public sealed record RouterStatus
{
    public string Vendor { get; init; } = "Unbekannt";
    public string? Firmware { get; init; }
    public string? ConnectionStatus { get; init; }
    public string? WanMode { get; init; }
    public string? ModelHint { get; init; }
    public bool? WifiEnabled { get; init; }
    public bool? BandSteering { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
    public string? Error { get; init; }
    public bool CanConfigure => false;
}
public sealed record RouterProfile(string Id, string Name, string Notes, string Gateway, string Mac, string Ssid, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, long ReceivedBytes, long SentBytes, string Vendor, string? Firmware);
public sealed record SecurityFinding(string Severity, string Title, string Detail, string Source);
public interface INetworkCollector { Task<NetworkSnapshot> CollectAsync(CancellationToken cancellationToken); }
