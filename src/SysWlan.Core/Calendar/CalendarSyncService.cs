namespace SysWlan.Core;

public sealed class CalendarSyncService(Store store, ICalendarSyncAdapter adapter, Func<DateTimeOffset>? clock = null) : IDisposable
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private Task? worker;
    public string Status { get; private set; } = "CalDAV nicht konfiguriert.";
    public event Action? Changed;

    public void Start() => worker ??= Task.Run(LoopAsync);
    public void Signal() { if (signal.CurrentCount == 0) signal.Release(); }
    public async Task SyncNowAsync(CancellationToken cancellationToken = default) => await ProcessAsync(cancellationToken);

    private async Task LoopAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await signal.WaitAsync(cancellation.Token);
                await ProcessAsync(cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var records = store.PendingSync(now(), 100);
        if (records.Length == 0) { Status = "CalDAV synchronisiert."; Changed?.Invoke(); return; }
        var items = records.Select(r => new CalendarSyncItem(r.ObjectType, r.ObjectId, r.Uid, r.Content, r.ETag)).ToArray();
        CalendarSyncResult[] results;
        try { results = await adapter.PushAsync(items, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { results = records.Select(r => new CalendarSyncResult(r.ObjectId, SyncStatus.Failed, r.ETag, "CalDAV-Netzwerk nicht erreichbar.")).ToArray(); }
        foreach (var result in results)
        {
            var record = records.FirstOrDefault(r => r.ObjectId == result.ObjectId);
            if (record is null) continue;
            if (result.Status == SyncStatus.Synced)
            {
                store.SaveSyncRecord(record with { Status = SyncStatus.Synced, ETag = result.ETag, NextAttemptAt = null, LastError = null, Content = "" });
                continue;
            }
            var attempts = record.Attempts + 1;
            var seconds = Math.Min(900, 5 * Math.Pow(2, Math.Min(attempts, 8)));
            store.SaveSyncRecord(record with { Status = SyncStatus.Failed, Attempts = attempts, ETag = result.ETag, NextAttemptAt = now().AddSeconds(seconds), LastError = result.Error });
        }
        Status = results.Any(r => r.Status == SyncStatus.Failed) ? "lokal aktuell, CalDAV ausstehend" : "CalDAV synchronisiert.";
        Changed?.Invoke();
    }

    public void Dispose() { cancellation.Cancel(); signal.Dispose(); }
}
