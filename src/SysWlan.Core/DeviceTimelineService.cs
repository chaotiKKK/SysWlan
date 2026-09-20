namespace SysWlan.Core;

public sealed class DeviceTimelineService(Store store, IICalendarExporter exporter, CalendarSyncService sync)
{
    private readonly Dictionary<string, PresenceIntervalBuilder> builders = new(StringComparer.Ordinal);
    private string? activeNetwork;
    private string? lastError;

    public void ObserveSnapshot(NetworkSnapshot snapshot, TrafficSample traffic, int pollSeconds)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(pollSeconds, 3, 60));
        var network = snapshot.Connected ? ProfileIdentity.Key(snapshot) : null;
        if (network != activeNetwork)
        {
            if (activeNetwork is not null) CloseNetwork(snapshot.Timestamp, "Netzwerkprofil gewechselt.");
            activeNetwork = network;
            lastError = null;
        }
        if (!snapshot.Connected) return;

        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in snapshot.Devices.Where(d => d.State.Equals("Reachable", StringComparison.OrdinalIgnoreCase)))
        {
            var source = DeviceIdentity.SourceKey(device, snapshot);
            if (!observed.Add(source)) continue;
            var builder = Builders(source);
            var transition = builder.Observe(source, snapshot.Timestamp, interval, "observed");
            var current = transition.Opened.Concat(transition.Updated).LastOrDefault();
            if (current is not null) Save(source, device.Mac, string.IsNullOrWhiteSpace(device.Name) ? "Unbekanntes Gerät" : device.Name, snapshot.Timestamp, "neighbor", current, null, transition.Events);
            if (transition.Closed.Length > 0) store.SaveTimelineTransition(transition.Closed, transition.Events);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LocalMac))
        {
            var source = DeviceIdentity.LocalSourceKey(snapshot.LocalMac, snapshot.InterfaceId);
            var builder = Builders(source);
            var transition = builder.Observe(source, snapshot.Timestamp, interval, "local");
            var current = transition.Opened.Concat(transition.Updated).LastOrDefault();
            var activity = new ActivityAggregate($"activity-{source}-{snapshot.Timestamp:yyyyMMddHHmmss}", source, snapshot.Timestamp, snapshot.Timestamp, traffic.ReceiveMbps, traffic.SendMbps, traffic.ReceiveMbps is null && traffic.SendMbps is null ? ActivityAvailability.Unavailable : ActivityAvailability.Available, "local-interface");
            if (current is not null) Save(source, snapshot.LocalMac, "Dieser Windows-PC", snapshot.Timestamp, "local", current, activity, transition.Events);
        }

        foreach (var builder in builders.Values)
        {
            var transition = builder.CloseExpired(snapshot.Timestamp, interval);
            if (transition.Closed.Length > 0 || transition.Events.Length > 0) store.SaveTimelineTransition(transition.Closed, transition.Events);
        }
        store.QueueCalendarExport(exporter, snapshot.Timestamp.AddDays(-365), snapshot.Timestamp.AddMinutes(1));
        sync.Signal();
    }

    public void ResetNetwork(DateTimeOffset timestamp, string message)
    {
        CloseNetwork(timestamp, message);
        activeNetwork = null;
    }

    public void RecordCollectionError(DateTimeOffset timestamp, string message)
    {
        if (message == lastError) return;
        lastError = message;
        var source = activeNetwork ?? "local";
        store.SaveTimelineTransition([], [new DeviceEvent($"error-{source}-{timestamp:yyyyMMddHHmmss}", source, timestamp, DeviceEventType.CollectionError, "Fehler", SyslogParser.Redact(message), "Collector")]);
    }

    private void Save(string source, string? mac, string name, DateTimeOffset observedAt, string kind, PresenceInterval presence, ActivityAggregate? activity, IEnumerable<DeviceEvent> events) =>
        store.SaveDeviceObservation(source, DeviceIdentity.NormalizeMac(mac), name, observedAt, kind, presence, activity, events);

    private PresenceIntervalBuilder Builders(string source) => builders.TryGetValue(source, out var builder) ? builder : builders[source] = new PresenceIntervalBuilder();

    private void CloseNetwork(DateTimeOffset timestamp, string message)
    {
        var closed = new List<PresenceInterval>();
        var events = new List<DeviceEvent>();
        foreach (var pair in builders)
        {
            var transition = pair.Value.Reset(timestamp, message);
            closed.AddRange(transition.Closed);
            events.AddRange(transition.Events);
        }
        if (closed.Count > 0 || events.Count > 0) store.SaveTimelineTransition(closed, events);
    }
}
