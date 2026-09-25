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
Check("android wifi mapping keeps unknown values unknown", () =>
{
    var unknown = AndroidWifiMapping.Map(null, null, null, null, null, null, null, null);
    Require(unknown.Ssid is null && unknown.Band is null && unknown.Channel is null && unknown.Authentication is null && unknown.Radio is null && unknown.SignalPercent is null, "unknown android values were invented");
    Require(AndroidWifiMapping.CleanText("0x") is null && AndroidWifiMapping.CleanText("<unknown ssid>") is null && AndroidWifiMapping.CleanText(" ") is null, "android placeholders leaked as SSID");
});
Check("android band and channel mapping covers 2.4/5/6 GHz", () =>
{
    Require(AndroidWifiMapping.Band(2412) == "2,4 GHz" && AndroidWifiMapping.Channel(2412) == "1", "2.4 GHz channel 1");
    Require(AndroidWifiMapping.Channel(2437) == "6" && AndroidWifiMapping.Channel(2484) == "14", "2.4 GHz channels 6/14");
    Require(AndroidWifiMapping.Band(5180) == "5 GHz" && AndroidWifiMapping.Channel(5180) == "36", "5 GHz channel 36");
    Require(AndroidWifiMapping.Band(5955) == "6 GHz" && AndroidWifiMapping.Channel(5955) == "1" && AndroidWifiMapping.Channel(5975) == "5", "6 GHz channels");
    Require(AndroidWifiMapping.Channel(6000) is null && AndroidWifiMapping.Channel(0) is null && AndroidWifiMapping.Channel(null) is null && AndroidWifiMapping.Band(1000) is null, "invalid frequencies must stay unknown");
});
Check("android signal, radio and security mapping do not guess", () =>
{
    Require(AndroidWifiMapping.SignalPercent(-50) == 100 && AndroidWifiMapping.SignalPercent(-71) == 58 && AndroidWifiMapping.SignalPercent(-100) == 0 && AndroidWifiMapping.SignalPercent(-140) == 0, "rssi to percent");
    Require(AndroidWifiMapping.SignalPercent(null) is null, "missing rssi must stay unknown");
    Require(AndroidWifiMapping.Radio(6) == "802.11ax" && AndroidWifiMapping.Radio(5) == "802.11ac" && AndroidWifiMapping.Radio(0) is null && AndroidWifiMapping.Radio(null) is null, "radio standard mapping");
    Require(AndroidWifiMapping.Security(4) == "WPA3-Personal (SAE)" && AndroidWifiMapping.Security(0) == "Offen (unverschlüsselt)" && AndroidWifiMapping.Security(99) is null && AndroidWifiMapping.Security(null) is null, "security mapping");
});
Check("android access warnings are explicit instead of silent", () =>
{
    Require(AndroidWifiMapping.WifiDetailsWarning(false) is not null && AndroidWifiMapping.WifiDetailsWarning(true) is null, "wifi detail warning");
    Require(AndroidWifiMapping.UsageAccessWarning(false)?.Contains("Nutzungszugriff") == true && AndroidWifiMapping.UsageAccessWarning(true) is null, "usage access warning");
    // Gerätetest 2026-09-25: eine fehlende SSID beweist keinen ausgeschalteten Standortschalter.
    Require(AndroidWifiMapping.LocationDisabledWarning(true, false) is not null, "disabled location switch must be reported");
    Require(AndroidWifiMapping.LocationDisabledWarning(true, true) is null, "missing ssid must not be blamed on the location switch");
    Require(AndroidWifiMapping.LocationDisabledWarning(false, false) is null, "location warning is redundant without wifi detail permission");
    Require(AndroidWifiMapping.DevicesUnavailable.Contains("Nachbartabelle") && AndroidWifiMapping.ConnectionsUnavailable.Contains("Verbindungen"), "unsupported android facts must be named");
});
Check("gateway identity prefers the real gateway MAC over the accesspoint BSSID", () =>
{
    var direct = GatewayIdentityPolicy.Resolve("aa:bb:cc:dd:ee:01", "aa:bb:cc:dd:ee:99");
    Require(direct.Source == GatewayIdentityPolicy.GatewayMacSource && direct.Mac == "aa:bb:cc:dd:ee:01", "gateway mac not preferred");
    var android = GatewayIdentityPolicy.Resolve(null, "AA:BB:CC:DD:EE:99");
    Require(android.Source == GatewayIdentityPolicy.ApBssidSource && android.Mac == "AA:BB:CC:DD:EE:99", "bssid fallback missing");
    foreach (var empty in new[] { (string?)null, "", "00:00:00:00:00:00", "FF-FF-FF-FF-FF-FF", "nicht-mac" })
        Require(GatewayIdentityPolicy.Resolve(empty, empty).Source is null, "unusable hardware identity accepted: " + empty);
    Require(GatewayIdentityPolicy.Describe(GatewayIdentityPolicy.ApBssidSource).Contains("Zuordnungshilfe"), "bssid source must be described as an inference");
});
Check("bssid router identity passes the session policy", () =>
{
    var identity = GatewayIdentityPolicy.Resolve(null, "aa:bb:cc:dd:ee:99");
    NetworkSnapshot Android(string? mac) => new() { Timestamp = start, InterfaceId = "android-wifi", InterfaceName = "wlan0", Gateway = "192.168.0.1", GatewayMac = mac, GatewayMacSource = identity.Source, Ssid = "Test WLAN", Wlan = new WlanInfo { Ssid = "Test WLAN", Bssid = "aa:bb:cc:dd:ee:99" } };
    var snapshot = Android(identity.Mac);
    Require(RouterAccessPolicy.CanAccess(new MonitorState { Snapshot = snapshot }, ProfileIdentity.Key(snapshot), start, 5), "android profile cannot reach the router");
    Require(ProfileIdentity.Key(snapshot) == ProfileIdentity.Key(Android("AA-BB-CC-DD-EE-99")), "bssid identity is delimiter dependent");
});
Check("router syslog device parser extracts mac, address and name", () =>
{
    var device = RouterSyslogDeviceParser.Parse("dhcpd: DHCPACK on 192.168.0.20 to aa:bb:cc:dd:ee:20 (iPhone-Anna) via br0");
    Require(device is not null && device.Mac == "AABBCCDDEE20" && device.Address == "192.168.0.20" && device.Name == "iPhone-Anna", "dhcp device not parsed");
    var associated = RouterSyslogDeviceParser.Parse("hostapd: wlan0: STA aa:bb:cc:dd:ee:21 IEEE 802.11: associated");
    Require(associated is not null && associated.Name.Length == 0 && associated.Address.Length == 0, "association must not invent a name");
    Require(RouterSyslogDeviceParser.Parse("kernel: link up, no hardware address here") is null, "line without mac must be ignored");
    Require(RouterSyslogDeviceParser.Parse("dhcpd: DHCPACK to 00:00:00:00:00:00 (Bogus)") is null, "all zero mac must be ignored");
});
Check("syslog device feed merges names and expires stale devices", () =>
{
    using var store = new Store(Path.Combine(Path.GetTempPath(), $"syswlan-feed-{Guid.NewGuid():N}.db"));
    using var receiver = new SyslogReceiver(store);
    using var feed = new RouterSyslogDeviceFeed(receiver, TimeSpan.FromMinutes(10));
    feed.Observe("dhcpd: DHCPACK on 192.168.0.20 to aa:bb:cc:dd:ee:20 via br0", start);
    feed.Observe("dhcpd: DHCPACK on 192.168.0.20 to aa:bb:cc:dd:ee:20 (iPhone-Anna) via br0", start.AddMinutes(1));
    feed.Observe("hostapd: STA aa:bb:cc:dd:ee:21 IEEE 802.11: associated", start.AddMinutes(2));
    var devices = feed.RecentDevices(start.AddMinutes(3));
    Require(devices.Length == 2, "feed did not group devices by mac: " + devices.Length);
    Require(devices.Single(d => d.Mac == "AABBCCDDEE20").Name == "iPhone-Anna", "later name was not merged");
    Require(feed.RecentDevices(start.AddMinutes(30)).Length == 0, "stale syslog devices were still claimed");
});
Check("background pause stops collection without touching the user pause", () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"syswlan-tests-{Guid.NewGuid():N}.db");
    try
    {
        using var store = new Store(path);
        store.SetSetting("intervalSeconds", "3");
        var collector = new CountingCollector();
        using var calendarSync = new CalendarSyncService(store, new NullSyncAdapter());
        var timeline = new DeviceTimelineService(store, new IcsExporter(), calendarSync);
        using var monitor = new MonitorService(collector, store, new RouterProbe(new OfflineHandler(), TimeSpan.FromMilliseconds(50)), timeline);
        monitor.Start();
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (collector.Count == 0 && DateTime.UtcNow < deadline) Thread.Sleep(50);
        Require(collector.Count > 0, "monitor never collected");
        monitor.SetBackgrounded(true);
        Require(monitor.State.Backgrounded && !monitor.State.Paused, "background must not read as a user pause");
        // Eine bereits laufende Erfassung darf noch zu Ende kommen, bevor die Baseline gilt.
        Thread.Sleep(700);
        var stopped = collector.Count;
        Thread.Sleep(4200);
        Require(collector.Count == stopped, "collection continued while the app was in the background");
        monitor.TogglePause();
        monitor.SetBackgrounded(false);
        Require(monitor.State.Paused && !monitor.State.Backgrounded, "user pause was overwritten by the lifecycle");
        monitor.TogglePause();
        deadline = DateTime.UtcNow.AddSeconds(6);
        while (collector.Count == stopped && DateTime.UtcNow < deadline) Thread.Sleep(50);
        Require(collector.Count > stopped, "collection did not resume in the foreground");
    }
    finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
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
sealed class CountingCollector : INetworkCollector
{
    private int count;
    public int Count => Volatile.Read(ref count);
    public Task<NetworkSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref count);
        return Task.FromResult(new NetworkSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow, InterfaceId = "test-nic", InterfaceName = "WLAN", Gateway = "192.168.0.1",
            GatewayMac = "AA-BB-CC-DD-EE-01", GatewayMacSource = GatewayIdentityPolicy.GatewayMacSource, Ssid = "Test WLAN",
            ReceivedBytes = Count * 1000L, SentBytes = Count * 500L
        });
    }
}
sealed class NullSyncAdapter : ICalendarSyncAdapter
{
    public Task<CalendarSyncResult[]> PushAsync(IReadOnlyList<CalendarSyncItem> items, CancellationToken cancellationToken) =>
        Task.FromResult(items.Select(i => new CalendarSyncResult(i.ObjectId, SyncStatus.Synced, "etag", null)).ToArray());
}
sealed class OfflineHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request });
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
