using System.Collections.Concurrent;
using Android.Content;
using Android.Net;
using SysWlan.Core;

namespace SysWlan.Android;

/// <summary>
/// Ersetzt das Windows-Ereignisprotokoll: Android meldet Netzwerkwechsel über Rückrufe.
/// Die Einträge werden zwischengespeichert und bei der nächsten Erfassung in die Logs übernommen.
/// </summary>
public sealed class NetworkEventsRecorder : ConnectivityManager.NetworkCallback, IDisposable
{
    private readonly ConcurrentQueue<LogEntry> pending = new();
    private ConnectivityManager? connectivity;
    private string? lastSignature;
    public bool Running { get; private set; }
    public string? Error { get; private set; }

    public void Start()
    {
        if (Running) return;
        try
        {
            connectivity = Application.Context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
            if (connectivity is null) { Error = "Android liefert keinen ConnectivityManager."; return; }
            connectivity.RegisterDefaultNetworkCallback(this);
            Running = true; Error = null;
            Enqueue("Info", "Netzwerküberwachung gestartet; Android meldet Wechsel des Standardnetzes.");
        }
        catch (Exception ex) { Error = SyslogParser.Redact(ex.Message); }
    }

    public override void OnAvailable(Network network) => Enqueue("Info", $"Netzwerk verfügbar: {network}");

    public override void OnLost(Network network) => Enqueue("Warnung", $"Netzwerk verloren: {network}");

    public override void OnCapabilitiesChanged(Network network, NetworkCapabilities? capabilities)
    {
        var transport = AndroidTransportMapping.Describe(AndroidTransportMapping.From(capabilities));
        var validated = capabilities?.HasCapability(NetCapability.Validated) == true ? "validiert" : "nicht validiert";
        var signature = $"{transport}|{validated}";
        if (signature == lastSignature) return;
        lastSignature = signature;
        Enqueue("Info", $"Netzwerkeigenschaften geändert: {transport}, {validated}.");
    }

    private void Enqueue(string severity, string message)
    {
        pending.Enqueue(new LogEntry(DateTimeOffset.UtcNow, "Android Netzwerk", severity, message));
        while (pending.Count > 200 && pending.TryDequeue(out _)) { }
    }

    public LogEntry[] Drain() => [.. pending];

    void IDisposable.Dispose() => Stop();

    public void Stop()
    {
        try { if (Running) connectivity?.UnregisterNetworkCallback(this); }
        catch (Exception) { }
        Running = false;
    }
}
