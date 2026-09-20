namespace SysWlan.Core;
public sealed class RateTracker
{
    private NetworkSnapshot? previous;
    public TrafficSample Sample(NetworkSnapshot snapshot, double maxGapSeconds = 45)
    {
        var old = previous;
        previous = snapshot;
        var gap = new TrafficSample(snapshot.Timestamp, null, null, 0, 0);
        if (old is null || !snapshot.Connected || old.InterfaceId != snapshot.InterfaceId || ProfileIdentity.Key(old) != ProfileIdentity.Key(snapshot)) return gap;
        var seconds = (snapshot.Timestamp - old.Timestamp).TotalSeconds;
        var rx = snapshot.ReceivedBytes - old.ReceivedBytes;
        var tx = snapshot.SentBytes - old.SentBytes;
        if (seconds <= 0 || seconds > maxGapSeconds || rx < 0 || tx < 0) return gap;
        return new(snapshot.Timestamp, rx * 8d / seconds / 1_000_000, tx * 8d / seconds / 1_000_000, rx, tx);
    }
    public void Reset() => previous = null;
}
