using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SysWlan.Core;

public sealed class PresenceIntervalBuilder
{
    private sealed record OpenState(string Id, string SourceKey, DateTimeOffset Start, DateTimeOffset LastSeen, string Quality);
    private readonly Dictionary<string, OpenState> open = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, PresenceInterval> OpenIntervals => open.ToDictionary(
        pair => pair.Key,
        pair => new PresenceInterval(pair.Value.Id, pair.Value.SourceKey, pair.Value.Start, null, pair.Value.Quality, true),
        StringComparer.Ordinal);

    public IntervalTransition Observe(string sourceKey, DateTimeOffset timestamp, TimeSpan pollInterval, string quality)
    {
        if (string.IsNullOrWhiteSpace(sourceKey)) throw new ArgumentException("Source key is required.", nameof(sourceKey));
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        timestamp = timestamp.ToUniversalTime();
        if (!open.TryGetValue(sourceKey, out var current) || timestamp - current.LastSeen > pollInterval + pollInterval)
        {
            var closed = current is null
                ? Array.Empty<PresenceInterval>()
                : [new PresenceInterval(current.Id, current.SourceKey, current.Start, current.LastSeen, current.Quality, false)];
            var closedEvents = current is null
                ? Array.Empty<DeviceEvent>()
                : [new DeviceEvent(EventId(sourceKey, current.LastSeen, DeviceEventType.Absence), sourceKey, current.LastSeen, DeviceEventType.Absence, "Warnung", "Gerät ist länger nicht beobachtet worden.", "Timeline")];
            open.Remove(sourceKey);
            var id = IntervalId(sourceKey, timestamp);
            var state = new OpenState(id, sourceKey, timestamp, timestamp, quality);
            open[sourceKey] = state;
            var interval = new PresenceInterval(id, sourceKey, timestamp, null, quality, true);
            var events = new[] { new DeviceEvent(EventId(sourceKey, timestamp, DeviceEventType.FirstObserved), sourceKey, timestamp, DeviceEventType.FirstObserved, "Info", "Gerät erstmals in der Beobachtung erkannt.", "Timeline") };
            return new([interval], [], closed, closedEvents.Concat(events).ToArray());
        }

        var updated = current with { LastSeen = timestamp, Quality = quality };
        open[sourceKey] = updated;
        return new([], [new PresenceInterval(updated.Id, sourceKey, updated.Start, null, updated.Quality, true)], [], []);
    }

    public IntervalTransition CloseExpired(DateTimeOffset timestamp, TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        timestamp = timestamp.ToUniversalTime();
        var closed = new List<PresenceInterval>();
        var events = new List<DeviceEvent>();
        foreach (var pair in open.ToArray())
        {
            var state = pair.Value;
            if (timestamp - state.LastSeen <= pollInterval + pollInterval) continue;
            open.Remove(pair.Key);
            closed.Add(new PresenceInterval(state.Id, state.SourceKey, state.Start, state.LastSeen, state.Quality, false));
            events.Add(new DeviceEvent(EventId(state.SourceKey, state.LastSeen, DeviceEventType.Absence), state.SourceKey, state.LastSeen, DeviceEventType.Absence, "Warnung", "Gerät ist länger nicht beobachtet worden.", "Timeline"));
        }
        return new([], [], closed.ToArray(), events.ToArray());
    }

    public IntervalTransition Reset(DateTimeOffset timestamp, string message)
    {
        timestamp = timestamp.ToUniversalTime();
        var closed = open.Values.Select(state => new PresenceInterval(state.Id, state.SourceKey, state.Start, state.LastSeen, state.Quality, false)).ToArray();
        var events = open.Values.Select(state => new DeviceEvent(EventId(state.SourceKey, timestamp, DeviceEventType.ProfileChanged), state.SourceKey, timestamp, DeviceEventType.ProfileChanged, "Info", message, "Timeline")).ToArray();
        open.Clear();
        return new([], [], closed, events);
    }

    private static string IntervalId(string sourceKey, DateTimeOffset timestamp) =>
        "presence-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey + "|" + timestamp.ToString("O", CultureInfo.InvariantCulture))))[..24];

    private static string EventId(string sourceKey, DateTimeOffset timestamp, DeviceEventType type) =>
        "event-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey + "|" + type + "|" + timestamp.ToString("O", CultureInfo.InvariantCulture))))[..24];
}
