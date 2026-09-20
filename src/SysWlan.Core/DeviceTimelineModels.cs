namespace SysWlan.Core;

public enum ActivityAvailability { Unavailable, Available }
public enum DeviceEventType { FirstObserved, ProfileChanged, Absence, CollectionError, AdapterEvent }
public enum SyncStatus { Pending, Synced, Failed }

public sealed record DeviceProfile(string Id, string Name, string? ManualColor, string? Icon, bool IsVisible, DateTimeOffset FirstSeen, DateTimeOffset LastSeen);
public sealed record DeviceIdentityMember(string SourceKey, string DeviceProfileId, string? Mac, string SourceKind, DateTimeOffset FirstSeen, DateTimeOffset LastSeen);
public sealed record PresenceInterval(string Id, string SourceKey, DateTimeOffset Start, DateTimeOffset? End, string Quality, bool IsOpen);
public sealed record ActivityAggregate(string Id, string SourceKey, DateTimeOffset Start, DateTimeOffset End, double? ReceiveMbps, double? SendMbps, ActivityAvailability Availability, string Source);
public sealed record DeviceEvent(string Id, string SourceKey, DateTimeOffset Timestamp, DeviceEventType Type, string Severity, string Message, string Source);
public sealed record CalendarSyncRecord(string ObjectType, string ObjectId, string Uid, string ContentHash, string? ETag, SyncStatus Status, int Attempts, DateTimeOffset? NextAttemptAt, string? LastError, string Content = "");

public sealed record CalendarDevice(string Id, string Name, string? ManualColor, string? Icon, bool IsVisible, string[] SourceKeys, DateTimeOffset FirstSeen, DateTimeOffset LastSeen)
{
    public string Color => DeviceIdentity.ResolveColor(new DeviceProfile(Id, Name, ManualColor, Icon, IsVisible, FirstSeen, LastSeen), SourceKeys.FirstOrDefault() ?? Id);
}

public sealed record CalendarSnapshot(CalendarDevice[] Devices, PresenceInterval[] Presence, ActivityAggregate[] Activity, DeviceEvent[] Events);
public sealed record CalendarSyncItem(string ObjectType, string ObjectId, string Uid, string Content, string? ETag = null);
public sealed record CalendarSyncResult(string ObjectId, SyncStatus Status, string? ETag, string? Error);
public sealed record IntervalTransition(PresenceInterval[] Opened, PresenceInterval[] Updated, PresenceInterval[] Closed, DeviceEvent[] Events)
{
    public static IntervalTransition Empty => new([], [], [], []);
}
