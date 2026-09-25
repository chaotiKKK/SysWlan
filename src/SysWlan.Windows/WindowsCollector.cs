using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using SysWlan.Core;

namespace SysWlan.Windows;

public sealed class WindowsCollector : INetworkCollector
{
    private static readonly string Script = ReadScript();
    private static string ReadScript()
    {
        using var stream = typeof(WindowsCollector).Assembly.GetManifestResourceStream("SysWlan.Windows.Scripts.collect.ps1")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public async Task<NetworkSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Windows-Erfassung konnte nicht gestartet werden.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        if (process.ExitCode != 0) throw new IOException("Windows-Erfassung fehlgeschlagen: " + SyslogParser.Redact(await error));
        var data = JsonSerializer.Deserialize<CollectionData>(await output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("Windows lieferte keine Netzwerkdaten.");
        var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id.Trim('{', '}').Equals(data.InterfaceId.Trim('{', '}'), StringComparison.OrdinalIgnoreCase));
        if (adapter is null) return new() { Errors = data.Errors, Connections = data.Connections, Events = data.Events };
        var properties = adapter.GetIPProperties();
        var counters = adapter.GetIPStatistics();
        long? latency = null;
        if (IPAddress.TryParse(data.Gateway, out var gateway))
        {
            try { using var ping = new Ping(); var reply = await ping.SendPingAsync(gateway, TimeSpan.FromSeconds(1), cancellationToken: cancellationToken); if (reply.Status == IPStatus.Success) latency = reply.RoundtripTime; }
            catch (PingException) { }
        }
        var wlan = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? WlanParser.Parse(data.WlanText) : new WlanInfo();
        var identity = GatewayIdentityPolicy.Resolve(data.GatewayMac, null);
        return new()
        {
            Timestamp = DateTimeOffset.UtcNow, InterfaceId = adapter.Id, InterfaceName = adapter.Name, Description = adapter.Description,
            Gateway = string.IsNullOrEmpty(data.Gateway) ? null : data.Gateway, GatewayMac = identity.Mac, GatewayMacSource = identity.Source, LocalMac = adapter.GetPhysicalAddress().ToString(),
            Ssid = wlan.Ssid, Wlan = wlan, LinkSpeed = adapter.Speed, ReceivedBytes = counters.BytesReceived, SentBytes = counters.BytesSent,
            Addresses = properties.UnicastAddresses.Select(a => a.Address.ToString()).ToArray(), DnsServers = properties.DnsAddresses.Select(a => a.ToString()).ToArray(),
            GatewayLatencyMs = latency, Devices = data.Devices, Connections = data.Connections, Events = data.Events, Errors = data.Errors
        };
    }
    private sealed record CollectionData
    {
        public string InterfaceId { get; init; } = "";
        public string Gateway { get; init; } = "";
        public string GatewayMac { get; init; } = "";
        public string WlanText { get; init; } = "";
        public DeviceInfo[] Devices { get; init; } = [];
        public ConnectionInfo[] Connections { get; init; } = [];
        public LogEntry[] Events { get; init; } = [];
        public string[] Errors { get; init; } = [];
    }
}
