namespace SysWlan.Core;

public sealed record MonitorState
{
    public NetworkSnapshot? Snapshot { get; init; }
    public RouterStatus Router { get; init; } = new();
    public TrafficSample? Traffic { get; init; }
    public TrafficSample[] History { get; init; } = [];
    public RouterProfile[] Profiles { get; init; } = [];
    public LogEntry[] Logs { get; init; } = [];
    public SecurityFinding[] Findings { get; init; } = [];
    public bool Paused { get; init; }
    /// <summary>Die App ist nicht sichtbar und ein Vordergrunddienst ist nicht aktiv; es wird bewusst nicht gemessen.</summary>
    public bool Backgrounded { get; init; }
    public string? Error { get; init; }
    public string? ProfileId => Snapshot?.Connected == true ? ProfileIdentity.Key(Snapshot) : null;
}

public sealed class MonitorService(INetworkCollector collector, Store store, RouterProbe probe, DeviceTimelineService timeline) : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly RateTracker rates = new();
    private readonly HashSet<string> eventKeys = [];
    private Task? worker;
    private volatile bool paused;
    private volatile bool backgrounded;
    private volatile MonitorState state = new();
    public MonitorState State => state;
    public event Action? Changed;
    /// <summary>Mindestabstand im Hintergrund (Android-Vordergrunddienst), damit Akku und Datenvolumen nicht überzogen werden.</summary>
    public int BackgroundIntervalSeconds { get; set; }
    public void Start() => worker ??= Task.Run(LoopAsync);
    public void TogglePause()
    {
        paused = !paused; state = state with { Paused = paused }; Changed?.Invoke();
    }
    /// <summary>
    /// Meldet, dass die App in den Hintergrund gegangen ist. Ohne Vordergrunddienst wird die Erfassung
    /// ausgesetzt und die Zeit ausdrücklich nicht als Messwert gewertet; der Nutzer-Pausenzustand bleibt unberührt.
    /// </summary>
    public void SetBackgrounded(bool value)
    {
        if (backgrounded == value) return;
        backgrounded = value;
        rates.Reset();
        if (value) store.AddLog(new(DateTimeOffset.UtcNow, "App", "Info", "App im Hintergrund: Erfassung ausgesetzt. Die Zeit erscheint nicht als Messwert, Anwesenheitsintervalle werden geschlossen."));
        state = state with { Backgrounded = backgrounded, Paused = paused };
        Changed?.Invoke();
    }
    public void RefreshStored()
    {
        state = state with { Profiles = store.Profiles(), Logs = store.Logs() }; Changed?.Invoke();
    }
    private async Task LoopAsync()
    {
        var token = cancellation.Token;
        string? lastId = null;
        DateTimeOffset nextProbe = DateTimeOffset.MinValue, nextPrune = DateTimeOffset.MinValue;
        string? lastConfig = null;
        var router = new RouterStatus();
        try
        {
            store.AddLog(new(DateTimeOffset.UtcNow, "App", "Info", "Monitoring gestartet. Datenmengen zählen nur beobachtete Zeiträume dieses Geräts."));
            while (!token.IsCancellationRequested)
            {
                if (paused || backgrounded) { rates.Reset(); await Task.Delay(500, token); continue; }
                try
                {
                    var snapshot = await collector.CollectAsync(token);
                    var id = snapshot.Connected ? ProfileIdentity.Key(snapshot) : null;
                    var changed = id != lastId;
                    if (changed)
                    {
                        store.AddLog(new(snapshot.Timestamp, "Verbindung", "Info", id is null ? "Kein aktives Gateway." : $"Netzwerk erkannt: {snapshot.Ssid ?? snapshot.InterfaceName}, Gateway {snapshot.Gateway}."));
                        router = new(); lastConfig = null; lastId = id;
                    }
                    var configuredInterval = int.TryParse(store.GetSetting("intervalSeconds"), out var configured) ? Math.Clamp(configured, 3, 60) : 5;
                    var traffic = rates.Sample(snapshot, configuredInterval + 25);
                    timeline.ObserveSnapshot(snapshot, traffic, configuredInterval);
                    if (id is not null && (changed || DateTimeOffset.UtcNow >= nextProbe))
                    {
                        router = await probe.ReadAsync(snapshot.Gateway, token); nextProbe = DateTimeOffset.UtcNow.AddSeconds(60);
                    }
                    if (id is null) router = new();
                    var config = $"{snapshot.Gateway}|{string.Join(',', snapshot.DnsServers)}|{snapshot.Wlan.Authentication}|{router.Firmware}";
                    if (lastConfig is not null && config != lastConfig) store.AddLog(new(snapshot.Timestamp, "Konfiguration", "Info", "Beobachtete Gateway-, DNS-, WLAN- oder Firmware-Daten haben sich geändert."));
                    lastConfig = config;
                    foreach (var entry in snapshot.Events)
                    {
                        var key = $"{entry.Timestamp:O}|{entry.Message}";
                        if (eventKeys.Add(key)) store.AddLog(entry);
                    }
                    if (eventKeys.Count > 2000) eventKeys.Clear();
                    store.Observe(snapshot, traffic, router);
                    if (DateTimeOffset.UtcNow >= nextPrune)
                    {
                        store.Prune(int.TryParse(store.GetSetting("retentionDays"), out var days) ? Math.Clamp(days, 1, 365) : 30);
                        nextPrune = DateTimeOffset.UtcNow.AddMinutes(5);
                    }
                    state = new() { Snapshot = snapshot, Router = router, Traffic = traffic, History = id is null ? [] : store.History(id), Profiles = store.Profiles(), Logs = store.Logs(), Findings = SecurityAnalyzer.Analyze(snapshot, router), Paused = paused, Backgrounded = backgrounded };
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    rates.Reset();
                    timeline.RecordCollectionError(DateTimeOffset.UtcNow, ex.Message);
                    state = state with { Error = "Erfassung fehlgeschlagen: " + SyslogParser.Redact(ex.Message), Paused = paused, Backgrounded = backgrounded };
                }
                Changed?.Invoke();
                var interval = int.TryParse(store.GetSetting("intervalSeconds"), out var seconds) ? Math.Clamp(seconds, 3, 60) : 5;
                if (backgrounded && BackgroundIntervalSeconds > interval) interval = BackgroundIntervalSeconds;
                await Task.Delay(TimeSpan.FromSeconds(state.Error is null ? interval : Math.Max(interval, 15)), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public void Dispose() { cancellation.Cancel(); }
}
