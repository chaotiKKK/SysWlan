using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using SysWlan.Core;
using Microsoft.Web.WebView2.Core;

namespace SysWlan.App.Services;

public sealed class DesktopActions(Store store, MonitorService monitor, CalendarQueryService calendar, IICalendarExporter ics)
{
    private readonly List<Microsoft.UI.Xaml.Window> routerWindows = [];
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
    public async Task OpenRouterAsync(string profileId, bool https)
    {
        var snapshot = EnsureCurrent(profileId);
        var uri = new UriBuilder(https ? "https" : "http", IPAddress.Parse(snapshot.Gateway!).ToString()).Uri;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var window = new Microsoft.UI.Xaml.Window { Title = $"Router · {snapshot.Ssid ?? snapshot.Gateway} · SysWLANInfo" };
            var grid = new Microsoft.UI.Xaml.Controls.Grid();
            grid.RowDefinitions.Add(new() { Height = Microsoft.UI.Xaml.GridLength.Auto });
            grid.RowDefinitions.Add(new() { Height = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
            var status = new Microsoft.UI.Xaml.Controls.TextBlock { Text = $"{uri}  ·  Originale Routeroberfläche · Anmeldung und Änderungen erfolgen am Router.", Margin = new Microsoft.UI.Xaml.Thickness(16, 10, 16, 10), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap };
            var fill = new Microsoft.UI.Xaml.Controls.Button { Content = "Gespeicherten Zugang auf Anmeldeseite einsetzen", Margin = new Microsoft.UI.Xaml.Thickness(16, 0, 16, 10), IsEnabled = false };
            var toolbar = new Microsoft.UI.Xaml.Controls.StackPanel(); toolbar.Children.Add(status); toolbar.Children.Add(fill);
            var view = new Microsoft.UI.Xaml.Controls.WebView2();
            Microsoft.UI.Xaml.Controls.Grid.SetRow(view, 1); grid.Children.Add(toolbar); grid.Children.Add(view);
            window.Content = grid; routerWindows.Add(window);
            var closed = false;
            void CloseWindow() { if (!closed) { closed = true; window.Close(); } }
            void NetworkChanged()
            {
                try { EnsureCurrent(profileId); } catch { MainThread.BeginInvokeOnMainThread(CloseWindow); }
            }
            void AddressChanged(object? sender, EventArgs args) => MainThread.BeginInvokeOnMainThread(CloseWindow);
            NetworkChange.NetworkAddressChanged += AddressChanged;
            var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => NetworkChanged(); timer.Start();
            monitor.Changed += NetworkChanged;
            window.Closed += (_, _) => { closed = true; timer.Stop(); NetworkChange.NetworkAddressChanged -= AddressChanged; monitor.Changed -= NetworkChanged; routerWindows.Remove(window); view.Close(); };
            window.Activate();
            try
            {
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(FileSystem.AppDataDirectory, "router-sessions", profileId), null);
                await view.EnsureCoreWebView2Async(environment);
                view.CoreWebView2.Settings.AreDevToolsEnabled = false;
                view.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                view.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                view.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
                view.CoreWebView2.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
                view.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
                view.CoreWebView2.NavigationStarting += (_, args) =>
                {
                    try { EnsureCurrent(profileId); } catch { args.Cancel = true; return; }
                    if (!RouterAccessPolicy.IsSameOrigin(uri, args.Uri)) { args.Cancel = true; status.Text = "Navigation außerhalb der gewählten Routeradresse wurde blockiert."; }
                };
                fill.IsEnabled = true;
                fill.Click += async (_, args) =>
                {
                    try
                    {
                        EnsureCurrent(profileId);
                        if (!RouterAccessPolicy.IsSameOrigin(uri, view.Source?.ToString()) || monitor.State.Router.Vendor != "ARRIS / Vodafone") { status.Text = "Automatisches Einsetzen ist für diese Routeroberfläche nicht validiert."; return; }
                        var saved = await SecureStorage.Default.GetAsync(CredentialKey(profileId));
                        if (saved is null) { status.Text = "Kein Zugang gespeichert. Anmeldung direkt hier oder Zugang im Dashboard speichern."; return; }
                        var credential = JsonSerializer.Deserialize<RouterCredential>(saved)!;
                        var userJson = JsonSerializer.Serialize(credential.Username); var passwordJson = JsonSerializer.Serialize(credential.Password);
                        EnsureCurrent(profileId);
                        var script = "(() => { if(location.origin!==" + JsonSerializer.Serialize(uri.GetLeftPart(UriPartial.Authority)) + " || !['/','/index.php'].includes(location.pathname) || !document.body.classList.contains('login_background')) return false; const p = ['Password','Password_m'].map(id=>document.getElementById(id)).find(e=>e && e.type==='password' && e.getClientRects().length); if(!p) return false; const suffix=p.id.endsWith('_m')?'_m':''; const b=document.getElementById('LoginBtn'+suffix); const u=document.getElementById('UserName'+suffix); if(!b || !u || !b.getClientRects().length) return false; if(!u.disabled){u.value=" + userJson + ";u.dispatchEvent(new Event('input',{bubbles:true}));} p.value=" + passwordJson + ";p.dispatchEvent(new Event('input',{bubbles:true}));p.dispatchEvent(new Event('change',{bubbles:true}));return true; })()";
                        var result = await view.ExecuteScriptAsync(script);
                        status.Text = result == "true" ? "Zugang eingesetzt. Anmeldung mit Einloggen im Router bestätigen." : "Keine validierte ARRIS-Anmeldeseite erkannt. Es wurden keine Zugangsdaten eingesetzt.";
                    }
                    catch { status.Text = "Zugang konnte nicht eingesetzt werden. Du kannst dich direkt in der Routeroberfläche anmelden."; }
                };
                EnsureCurrent(profileId); view.Source = uri;
            }
            catch { window.Close(); throw; }
        });
    }
    public async Task<string> ExportAsync()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SysWLANInfo"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"netzwerk-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, store.Export()); return path;
    }
    public async Task<string> ExportCalendarAsync(DateOnly anchor, CalendarViewMode mode)
    {
        var snapshot = mode switch
        {
            CalendarViewMode.Day => calendar.GetDay(anchor),
            CalendarViewMode.Month => calendar.GetMonth(anchor),
            _ => calendar.GetWeek(anchor)
        };
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SysWLANInfo");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"netzwerk-kalender-{DateTime.Now:yyyyMMdd-HHmmss}.ics");
        await File.WriteAllTextAsync(path, ics.Export(snapshot), new System.Text.UTF8Encoding(false));
        return path;
    }
    public async Task<string> ResolveAsync(string host)
    {
        if (host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown) throw new ArgumentException("Bitte einen gültigen Hostnamen ohne URL-Pfad eingeben.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var started = System.Diagnostics.Stopwatch.StartNew(); var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
        return $"{host} → {string.Join(", ", addresses.Select(a => a.ToString()))} ({started.ElapsedMilliseconds} ms, Windows-DNS)";
    }
    private sealed record RouterCredential(string Username, string Password);
}
