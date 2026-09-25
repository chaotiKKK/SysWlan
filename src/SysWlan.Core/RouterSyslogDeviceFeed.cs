namespace SysWlan.Core;

/// <summary>
/// Hält die zuletzt per Router-Syslog gemeldeten Geräte. Unter Android ist das die einzige
/// Gerätequelle, weil die Nachbartabelle dort nicht mehr lesbar ist. Die Liste ist bewusst
/// sitzungsbezogen und läuft nach zwei Stunden ab, damit keine veralteten Geräte behauptet werden.
/// </summary>
public sealed class RouterSyslogDeviceFeed : IDisposable
{
    private readonly SyslogReceiver receiver;
    private readonly Dictionary<string, Entry> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private readonly TimeSpan lifetime;

    public RouterSyslogDeviceFeed(SyslogReceiver receiver, TimeSpan? lifetime = null)
    {
        this.receiver = receiver;
        this.lifetime = lifetime ?? TimeSpan.FromHours(2);
        receiver.Received += OnReceived;
    }

    private void OnReceived(LogEntry entry) => Observe(entry.Message, entry.Timestamp);

    public void Observe(string message, DateTimeOffset timestamp)
    {
        var device = RouterSyslogDeviceParser.Parse(message);
        if (device is null) return;
        lock (gate)
        {
            var merged = device;
            if (devices.TryGetValue(device.Mac, out var existing))
            {
                merged = new DeviceInfo(
                    device.Address.Length > 0 ? device.Address : existing.Device.Address,
                    device.Mac,
                    device.State,
                    device.Name.Length > 0 ? device.Name : existing.Device.Name);
            }
            devices[device.Mac] = new Entry(merged, timestamp);
        }
    }

    public DeviceInfo[] RecentDevices(DateTimeOffset now)
    {
        lock (gate)
        {
            foreach (var expired in devices.Where(pair => now - pair.Value.Seen > lifetime).Select(pair => pair.Key).ToArray()) devices.Remove(expired);
            return devices.Values.Select(entry => entry.Device).ToArray();
        }
    }

    public void Clear()
    {
        lock (gate) devices.Clear();
    }

    public void Dispose() => receiver.Received -= OnReceived;

    private sealed record Entry(DeviceInfo Device, DateTimeOffset Seen);
}
