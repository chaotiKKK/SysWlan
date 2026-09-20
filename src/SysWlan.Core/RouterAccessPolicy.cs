namespace SysWlan.Core;
public static class RouterAccessPolicy
{
    public static bool CanAccess(MonitorState state, string profileId, DateTimeOffset now, int intervalSeconds)
    {
        if (state.Paused || state.Error is not null || state.ProfileId != profileId || state.Snapshot is not { Connected: true } snapshot) return false;
        var age = (now - snapshot.Timestamp).TotalSeconds;
        var mac = ProfileIdentity.NormalizeMac(snapshot.GatewayMac);
        return age >= 0 && age <= Math.Clamp(intervalSeconds, 3, 60) + 25 && mac.Length == 12 && mac is not ("000000000000" or "FFFFFFFFFFFF");
    }
    public static bool IsSameOrigin(Uri expected, string? actual) => Uri.TryCreate(actual, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.Scheme == expected.Scheme && uri.Host == expected.Host && uri.Port == expected.Port && uri.UserInfo.Length == 0;
}
