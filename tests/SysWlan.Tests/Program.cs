using System.Net;
using SysWlan.Core;

var failed = 0;
void Check(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Require(bool ok, string reason) { if (!ok) throw new Exception(reason); }
var start = DateTimeOffset.Parse("2026-09-20T12:00:00Z");
NetworkSnapshot Sample(long rx, long tx, int sec, string mac = "AA-BB-CC-DD-EE-01") => new()
{
    Timestamp = start.AddSeconds(sec), InterfaceId = "nic-1", InterfaceName = "WLAN", Gateway = "192.168.0.1",
    GatewayMac = mac, Ssid = "Test WLAN", ReceivedBytes = rx, SentBytes = tx
};
Check("first counter reading is not measured throughput", () =>
{
    Require(new RateTracker().Sample(Sample(1_000_000, 200, 0)).ReceiveMbps is null, "first sample must be unknown");
});
Check("rates use elapsed time and bytes to decimal megabits", () =>
{
    var tracker = new RateTracker(); tracker.Sample(Sample(10, 20, 0));
    var rate = tracker.Sample(Sample(1_000_010, 250_020, 2));
    Require(rate.ReceiveMbps == 4 && rate.SendMbps == 1, "expected 4/1 Mbps over two seconds");
});
Check("counter reset creates gap instead of negative traffic", () =>
{
    var tracker = new RateTracker(); tracker.Sample(Sample(5000, 4000, 0));
    Require(tracker.Sample(Sample(10, 20, 2)).ReceiveMbps is null, "reset must be gap");
});
Check("network switch does not charge previous network traffic", () =>
{
    var tracker = new RateTracker(); tracker.Sample(Sample(10, 20, 0));
    Require(tracker.Sample(Sample(1000, 2000, 2, "AA-BB-CC-DD-EE-02")).ReceiveMbps is null, "new router needs baseline");
});
Check("standby gap is excluded from live rate", () =>
{
    var tracker = new RateTracker(); tracker.Sample(Sample(10, 20, 0));
    Require(tracker.Sample(Sample(1000, 2000, 120)).ReceiveMbps is null, "long gap must be unknown");
});
Check("gateway MAC identity is case and delimiter independent", () =>
{
    Require(ProfileIdentity.Key(Sample(0, 0, 0, "aa:bb:cc:dd:ee:01")) == ProfileIdentity.Key(Sample(0, 0, 0)), "MAC normalization");
});
Check("different gateways behind same IP and SSID are not merged", () =>
{
    Require(ProfileIdentity.Key(Sample(0, 0, 0)) != ProfileIdentity.Key(Sample(0, 0, 0, "AA-BB-CC-DD-EE-02")), "router collision");
});
Check("router parser only exposes whitelisted public fields", () =>
{
    var router = RouterParser.Parse("<!-- ARRIS --> _ga.swVersion = '1.2'; _ga.modemConnectionStatus = 'DOCSIS Online'; var currentSessionId = 'secret'; _ga.isModel6442 = 'true';");
    Require(router.Vendor == "ARRIS / Vodafone" && router.Firmware == "1.2", "missing public status");
    Require(!System.Text.Json.JsonSerializer.Serialize(router).Contains("secret"), "session leaked");
});
Check("HTML is never treated as authenticated router access", () =>
{
    var router = RouterParser.Parse("<html>Login</html>");
    Require(!router.CanConfigure && router.Firmware is null, "unknown router claimed supported");
});
Check("German WLAN values preserve colons in BSSID", () =>
{
    var wlan = WlanParser.Parse("    SSID : Test\n    AP BSSID : aa:bb:cc:dd:ee:ff\n    Signal : 65%\n    Empfangsrate (MBit/s) : 130\n    Authentifizierung : WPA3-Personal\n    RSSI : -71\n");
    Require(wlan.Bssid == "aa:bb:cc:dd:ee:ff" && wlan.SignalPercent == 65 && wlan.ReceiveLinkMbps == 130, "WLAN parsing failed");
});
Check("syslog parses severity and removes control characters", () =>
{
    var log = SyslogParser.Parse("<134>hello\u001b[31m\0", "192.168.0.1", start);
    Require(log.Severity == "Info" && !log.Message.Contains('\u001b') && !log.Message.Contains('\0'), "invalid log sanitization");
});
Check("storage reconnect preserves user name and notes", () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"syswlan-tests-{Guid.NewGuid():N}.db");
    try
    {
        using (var store = new Store(path))
        {
            store.Observe(Sample(10, 20, 0), new TrafficSample(start, null, null, 0, 0), new RouterStatus());
            var first = store.Profiles().Single();
            store.UpdateProfile(first.Id, "Home Office", "Custom note");
            store.Observe(Sample(30, 40, 10), new TrafficSample(start.AddSeconds(10), 1, 2, 20, 20), new RouterStatus());
        }
        using (var reopened = new Store(path))
        {
            var profile = reopened.Profiles().Single();
            Require(profile.Name == "Home Office" && profile.Notes == "Custom note", "user metadata lost");
            Require(profile.ReceivedBytes == 20 && profile.LastSeen == start.AddSeconds(10), "reconnect or totals failed");
        }
    }
    finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
});
Check("export redacts common credentials from received logs", () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"syswlan-tests-{Guid.NewGuid():N}.db");
    try
    {
        using var store = new Store(path);
        store.AddLog(new(start, "Router", "Info", "password=secret123 token=token456"));
        var export = store.Export();
        Require(!export.Contains("secret123") && !export.Contains("token456"), "credentials in export");
    }
    finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
});
Check("SQL metacharacters in profile names remain plain data", () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"syswlan-tests-{Guid.NewGuid():N}.db");
    try
    {
        using var store = new Store(path);
        store.Observe(Sample(0, 0, 0), new(start, null, null, 0, 0), new());
        store.UpdateProfile(store.Profiles()[0].Id, "Home'; DROP TABLE profiles;--", "notes");
        Require(store.Profiles().Length == 1 && store.Profiles()[0].Name.StartsWith("Home'"), "SQL injection");
    }
    finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
});
Check("long configured polling intervals still produce rates", () =>
{
    var tracker = new RateTracker(); tracker.Sample(Sample(0, 0, 0), 90);
    Require(tracker.Sample(Sample(61_000_000, 0, 61), 90).ReceiveMbps == 8, "60 second setting loses all rates");
});
Check("authorization headers and JSON secrets are fully redacted", () =>
{
    foreach (var raw in new[] { "Authorization: Bearer abc123", "{\"password\":\"abc123\"}", "Cookie: a=public; session=abc123", "password='abc123 secret'", "passwd: abc123" })
        Require(!SyslogParser.Redact(raw).Contains("abc123"), "secret survived: " + raw);
});
Check("router body reads have a deadline after successful headers", () =>
{
    using var parent = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    using var probe = new RouterProbe(new StalledHandler(), TimeSpan.FromMilliseconds(80));
    var result = probe.ReadAsync("127.0.0.1", parent.Token).GetAwaiter().GetResult();
    Require(result.Error is not null && !parent.IsCancellationRequested, "body exceeded probe deadline");
});
Check("router access stops on pause, errors, stale data and profile change", () =>
{
    var snapshot = Sample(0, 0, 0); var id = ProfileIdentity.Key(snapshot); var state = new MonitorState { Snapshot = snapshot };
    Require(RouterAccessPolicy.CanAccess(state, id, start, 5), "fresh connected profile rejected");
    Require(!RouterAccessPolicy.CanAccess(state with { Paused = true }, id, start, 5), "paused access allowed");
    Require(!RouterAccessPolicy.CanAccess(state with { Error = "error" }, id, start, 5), "failed collection allowed");
    Require(!RouterAccessPolicy.CanAccess(state, id, start.AddSeconds(31), 5), "stale access allowed");
    Require(!RouterAccessPolicy.CanAccess(state, "another", start, 5), "wrong profile allowed");
});
Check("router credentials never cross scheme, host or port", () =>
{
    var origin = new Uri("https://192.168.0.1/");
    Require(RouterAccessPolicy.IsSameOrigin(origin, "https://192.168.0.1/login"), "valid origin rejected");
    foreach(var target in new[] { "http://192.168.0.1/", "https://192.168.0.2/", "https://192.168.0.1:8443/", "https://user@192.168.0.1/" })
        Require(!RouterAccessPolicy.IsSameOrigin(origin, target), "unsafe origin accepted");
});
Check("device identity and colors survive address changes", () =>
{
    var snapshot = Sample(0, 0, 0);
    var a = DeviceIdentity.SourceKey(new DeviceInfo("192.168.0.20", "AA-BB-CC-DD-EE-20", "Reachable", "Phone"), snapshot);
    var b = DeviceIdentity.SourceKey(new DeviceInfo("192.168.0.88", "aa:bb:cc:dd:ee:20", "Reachable", "Renamed"), snapshot);
    Require(a == b && DeviceIdentity.IsValidColor(DeviceIdentity.AutomaticColor(a)), "identity/color changed");
});
Check("presence intervals tolerate one missed poll and close after a longer gap", () =>
{
    var builder = new PresenceIntervalBuilder();
    var first = builder.Observe("mac:AABBCCDDEE20", start, TimeSpan.FromSeconds(5), "observed");
    var second = builder.Observe("mac:AABBCCDDEE20", start.AddSeconds(10), TimeSpan.FromSeconds(5), "observed");
    var closed = builder.CloseExpired(start.AddSeconds(26), TimeSpan.FromSeconds(5));
    Require(first.Opened.Length == 1 && second.Updated.Length == 1 && closed.Closed.Length == 1 && closed.Closed[0].End == start.AddSeconds(10), "interval aggregation failed");
});
Check("iCalendar export is deterministic and excludes secrets", () =>
{
    var device = new CalendarDevice("d1", "Phone", "#73E3DC", null, true, ["mac:A"], start, start.AddMinutes(5));
    var interval = new PresenceInterval("p1", "mac:A", start, start.AddMinutes(5), "observed", false);
    var output = new IcsExporter().Export(new([device], [interval], [], []));
    Require(output == new IcsExporter().Export(new([device], [interval], [], [])) && output.Contains("UID:presence:mac:A:p1@syswlaninfo.local") && !output.Contains("password"), "invalid ICS output");
});
Check("password generator creates strong local passwords", () =>
{
    var password = PasswordGenerator.Generate();
    Require(password.Length == 24 && password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit), "weak generated password");
});
Check("router credential test sends exactly one explicit request", () =>
{
    using var handler = new CredentialHandler();
    using var tester = new RouterConnectionTester(handler, TimeSpan.FromSeconds(1));
    var result = tester.TestAsync("192.168.0.1", "admin", "one-secret", false, CancellationToken.None).GetAwaiter().GetResult();
    Require(result.Succeeded && handler.Requests == 1 && handler.Authorization == "admin:one-secret", "credential test was not one explicit request");
});
Check("router credential test cancellation stops without retry", () =>
{
    using var handler = new DelayedCredentialHandler();
    using var tester = new RouterConnectionTester(handler, TimeSpan.FromSeconds(5));
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
    var canceled = false;
    try { _ = tester.TestAsync("192.168.0.1", "admin", "secret", false, cancellation.Token).GetAwaiter().GetResult(); }
    catch (OperationCanceledException) { canceled = true; }
    Require(canceled && handler.Requests == 1, "cancellation retried credential test");
});
Check("CalDAV environment override is secret-safe", () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"caldav-{Guid.NewGuid():N}.json");
    try
    {
        File.WriteAllText(path, "{\"Url\":\"https://example.test/cal\",\"Username\":\"user\",\"Password\":\"file-secret\",\"Calendar\":\"Netzwerkgeräte\"}");
        var options = new CalDavConfigurationProvider().Load(path, new Dictionary<string, string?> { ["SYSWLAN_CALDAV_PASSWORD"] = "env-secret" });
        Require(options.Password == "env-secret" && !options.ToSafeString().Contains("env-secret"), "CalDAV secret leaked or override failed");
    }
    finally { File.Delete(path); }
});
Check("CalDAV discovers, creates, updates and deletes collection objects", () =>
{
    using var handler = new RecordingCalDavHandler();
    using var http = new HttpClient(handler);
    var client = new CalDavClient(http, new CalDavOptions("https://calendar.test/dav", "user", "secret"));
    var items = new[] { new CalendarSyncItem("calendar", "one", "one", "BEGIN:VCALENDAR", null), new CalendarSyncItem("calendar", "gone", "gone", "", "etag-old") };
    var results = client.PushAsync(items, CancellationToken.None).GetAwaiter().GetResult();
    Require(results.All(r => r.Status == SyncStatus.Synced), "CalDAV object flow failed");
    Require(handler.Methods.SequenceEqual(["PROPFIND", "MKCALENDAR", "PUT", "DELETE"]), "unexpected CalDAV capability/object flow: " + string.Join(',', handler.Methods));
    Require(handler.AuthorizationSeen && handler.UpdateConditionSeen, "CalDAV auth/conditional update missing");
});
if (args.Contains("--live"))
{
    var snapshot = await new SysWlan.Windows.WindowsCollector().CollectAsync(CancellationToken.None);
    using var probe = new RouterProbe();
    var router = await probe.ReadAsync(snapshot.Gateway, CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { snapshot.Timestamp, snapshot.InterfaceName, snapshot.Ssid, snapshot.Gateway, snapshot.Wlan, devices = snapshot.Devices.Length, connections = snapshot.Connections.Length, events = snapshot.Events.Length, snapshot.Errors, router }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Require(snapshot.Connected && snapshot.ReceivedBytes > 0, "live collector missing active adapter");
}
Console.WriteLine($"\n{failed} failed");
return failed == 0 ? 0 : 1;

sealed class RecordingCalDavHandler : HttpMessageHandler
{
    public List<string> Methods { get; } = [];
    public bool AuthorizationSeen { get; private set; }
    public bool UpdateConditionSeen { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Methods.Add(request.Method.Method);
        AuthorizationSeen |= request.Headers.Authorization is not null;
        UpdateConditionSeen |= request.Method == HttpMethod.Put && request.Headers.Contains("If-None-Match");
        var status = request.Method.Method switch { "PROPFIND" => HttpStatusCode.NotFound, "MKCALENDAR" => HttpStatusCode.Created, "PUT" => HttpStatusCode.Created, "DELETE" => HttpStatusCode.NoContent, _ => HttpStatusCode.BadRequest };
        return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
    }
}
sealed class CredentialHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    public string? Authorization { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++;
        Authorization = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization?.Parameter ?? ""));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
    }
}
sealed class DelayedCredentialHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++; await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
sealed class StalledHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
}
sealed class StalledStream : Stream
{
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => 0; public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(Timeout.Infinite, token); return 0; }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
