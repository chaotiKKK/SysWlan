using System.Net;
using System.Text.Json;
using SysWlan.Core;

namespace SysWlan.App.Services;

/// <summary>
/// Android-Umsetzung: Routerbereich als eigene Seite mit Sitzungsregeln, Export über das Android-Teilen-Menü
/// und Zugangsspeicher im verschlüsselten Speicher von MAUI.
/// </summary>
public sealed class AndroidPlatformActions(Store store, MonitorService monitor, CalendarQueryService calendar, IICalendarExporter ics, RouterConnectionTester connectionTester) : IPlatformActions
{
    private readonly SemaphoreSlim connectionTestGate = new(1, 1);
    private DateTimeOffset lastConnectionTest = DateTimeOffset.MinValue;
    private static string CredentialKey(string profileId) => "router-credential-" + profileId;

    public async Task SaveCredentialAsync(string profileId, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length > 512) throw new ArgumentException("Bitte ein Routerpasswort mit maximal 512 Zeichen eingeben.");
        EnsureCurrent(profileId);
        await SecureStorage.Default.SetAsync(CredentialKey(profileId), JsonSerializer.Serialize(new RouterCredential(username.Trim(), password)));
    }

    public async Task<bool> HasCredentialAsync(string profileId) => await SecureStorage.Default.GetAsync(CredentialKey(profileId)) is not null;

    public void ForgetCredential(string profileId) => SecureStorage.Default.Remove(CredentialKey(profileId));

    private NetworkSnapshot EnsureCurrent(string profileId)
    {
        var state = monitor.State;
        var interval = int.TryParse(store.GetSetting("intervalSeconds"), out var value) ? value : 5;
        if (!RouterAccessPolicy.CanAccess(state, profileId, DateTimeOffset.UtcNow, interval))
            throw new InvalidOperationException("Dieses Profil ist nicht aktuell verbunden. Erfassung fortsetzen und erneut versuchen.");
        return state.Snapshot!;
    }

    public async Task<RouterConnectionTestResult> TestRouterConnectionAsync(string profileId, string username, string password, bool https, CancellationToken cancellationToken)
    {
        EnsureCurrent(profileId);
        if (!await connectionTestGate.WaitAsync(0, cancellationToken))
            return new(false, "Es läuft bereits ein Verbindungstest. Bitte warten oder abbrechen.");
        try
        {
            if (DateTimeOffset.UtcNow - lastConnectionTest < TimeSpan.FromSeconds(3))
                return new(false, "Bitte mindestens drei Sekunden bis zum nächsten manuellen Test warten.");
            lastConnectionTest = DateTimeOffset.UtcNow;
            return await connectionTester.TestAsync(monitor.State.Snapshot!.Gateway, username, password, https, cancellationToken);
        }
        finally { connectionTestGate.Release(); }
    }

    public async Task<string> GeneratePasswordAsync()
    {
        await Task.Yield();
        return PasswordGenerator.Generate();
    }

    public Task CopyToClipboardAsync(string value) => Clipboard.Default.SetTextAsync(value);

    public async Task OpenRouterAsync(string profileId, bool https)
    {
        var snapshot = EnsureCurrent(profileId);
        var uri = new UriBuilder(https ? "https" : "http", IPAddress.Parse(snapshot.Gateway!).ToString()).Uri;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var host = Application.Current?.Windows.FirstOrDefault()?.Page ?? throw new InvalidOperationException("Keine aktive Seite gefunden.");
            await host.Navigation.PushModalAsync(new NavigationPage(new RouterPage(profileId, uri, monitor, () => EnsureCurrent(profileId))));
        });
    }

    public async Task<string> ExportAsync()
    {
        var path = await WriteAsync($"netzwerk-export-{DateTime.Now:yyyyMMdd-HHmmss}.json", store.Export());
        return await ShareAsync(path, "JSON-Export");
    }

    public async Task<string> ExportCalendarAsync(DateOnly anchor, CalendarViewMode mode)
    {
        var snapshot = mode switch
        {
            CalendarViewMode.Day => calendar.GetDay(anchor),
            CalendarViewMode.Month => calendar.GetMonth(anchor),
            _ => calendar.GetWeek(anchor)
        };
        var path = await WriteAsync($"netzwerk-kalender-{DateTime.Now:yyyyMMdd-HHmmss}.ics", ics.Export(snapshot));
        return await ShareAsync(path, "Kalender-Export");
    }

    public async Task<string> ResolveAsync(string host)
    {
        if (host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown) throw new ArgumentException("Bitte einen gültigen Hostnamen ohne URL-Pfad eingeben.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var started = System.Diagnostics.Stopwatch.StartNew();
        var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
        return $"{host} → {string.Join(", ", addresses.Select(a => a.ToString()))} ({started.ElapsedMilliseconds} ms, Android-DNS)";
    }

    // Ohne BOM: "Encoding.UTF8" schreibt eines, das strenge JSON-Leser (jq, Python, viele Editoren)
    // ablehnen. Windows schreibt ebenfalls ohne BOM; der Gerätetest hat den Unterschied sichtbar gemacht.
    private static async Task<string> WriteAsync(string fileName, string content, System.Text.Encoding? encoding = null)
    {
        var folder = Path.Combine(FileSystem.CacheDirectory, "SysWLANInfo");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        await File.WriteAllTextAsync(path, content, encoding ?? new System.Text.UTF8Encoding(false));
        return path;
    }

    private static async Task<string> ShareAsync(string path, string label)
    {
        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest { Title = $"SysWLANInfo {label}", File = new ShareFile(path) });
            return $"{label} erstellt und an das Teilen-Menü übergeben: {path}";
        }
        catch (Exception ex)
        {
            return $"{label} erstellt: {path} · Teilen nicht möglich ({SyslogParser.Redact(ex.Message)}).";
        }
    }

    private sealed record RouterCredential(string Username, string Password);
}
