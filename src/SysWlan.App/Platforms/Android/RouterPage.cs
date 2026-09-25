using SysWlan.Core;
using AndroidCookieManager = global::Android.Webkit.CookieManager;
using AndroidDownloadListener = global::Android.Webkit.IDownloadListener;
using AndroidMixedContent = global::Android.Webkit.MixedContentHandling;
using AndroidResourceRequest = global::Android.Webkit.IWebResourceRequest;
using AndroidSslError = global::Android.Net.Http.SslError;
using AndroidSslErrorHandler = global::Android.Webkit.SslErrorHandler;
using AndroidWebStorage = global::Android.Webkit.WebStorage;
using AndroidWebView = global::Android.Webkit.WebView;
using AndroidWebViewClient = global::Android.Webkit.WebViewClient;

namespace SysWlan.App;

/// <summary>
/// Routeroberfläche unter Android. Es ist immer genau eine Sitzung aktiv: Android hat einen globalen
/// Cookie-Speicher, deshalb werden Cookies und WebStorage beim Öffnen und Schließen geleert, statt sie über
/// Profile hinweg zu vermischen. Navigation außerhalb der Gateway-Origin wird blockiert, Downloads bleiben
/// gesperrt und Zertifikatfehler werden nicht umgangen.
/// </summary>
public sealed class RouterPage : ContentPage
{
    private readonly Uri origin;
    private readonly Func<NetworkSnapshot> ensureCurrent;
    private readonly Label status = new() { LineBreakMode = LineBreakMode.WordWrap, Margin = new Thickness(16, 12) };
    private readonly WebView view = new();
    private readonly MonitorService monitor;
    private IDispatcherTimer? timer;
    private bool closed;

    public RouterPage(string profileId, Uri origin, MonitorService monitor, Func<NetworkSnapshot> ensureCurrent)
    {
        this.origin = origin;
        this.monitor = monitor;
        this.ensureCurrent = ensureCurrent;
        Title = "Routerverwaltung";
        status.Text = $"{origin}  ·  Originale Routeroberfläche  ·  Anmeldung und Änderungen erfolgen am Router. HTTP im lokalen Netz ist unverschlüsselt.";
        view.Navigating += OnNavigating;
        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        grid.Add(status, 0, 0);
        grid.Add(view, 0, 1);
        Content = grid;
        view.Source = origin.ToString();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ConfigurePlatformView();
        monitor.Changed += OnMonitorChanged;
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => Check();
        timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Close();
    }

    private void ConfigurePlatformView()
    {
        if (view.Handler?.PlatformView is not AndroidWebView platform) return;
        var settings = platform.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.AllowFileAccess = false;
        settings.AllowContentAccess = false;
        settings.MixedContentMode = AndroidMixedContent.NeverAllow;
        platform.SetWebViewClient(new RouterWebViewClient(this));
        platform.SetDownloadListener(new BlockedDownloadListener(this));
        PurgeSession();
    }

    /// <summary>Android teilt Cookies und WebStorage über alle WebViews eines Prozesses.</summary>
    private void PurgeSession()
    {
        try
        {
            AndroidCookieManager.Instance?.RemoveAllCookies(null);
            AndroidCookieManager.Instance?.Flush();
            AndroidWebStorage.Instance?.DeleteAllData();
        }
        catch (Exception) { }
    }

    internal bool BlockIfForeign(string? target)
    {
        if (target is null) return true;
        try { ensureCurrent(); } catch { Close(); return true; }
        if (RouterAccessPolicy.IsSameOrigin(origin, target)) return false;
        status.Text = "Navigation außerhalb der gewählten Routeradresse wurde blockiert.";
        return true;
    }

    internal void ReportSslProblem(string detail)
    {
        status.Text = $"Zertifikat nicht akzeptiert ({detail}). Die Routeroberfläche bleibt gesperrt, statt den Fehler zu umgehen.";
        Close();
    }

    internal void ReportBlockedDownload() => status.Text = "Downloads aus der Routeroberfläche sind gesperrt. Für Router-Konfigurationsdateien die Herstelleroberfläche im eigenen Browser verwenden.";

    private void OnNavigating(object? sender, WebNavigatingEventArgs args) => args.Cancel = BlockIfForeign(args.Url);

    private void OnMonitorChanged() => MainThread.BeginInvokeOnMainThread(Check);

    private void Check()
    {
        if (closed) return;
        try { ensureCurrent(); }
        catch
        {
            // Pause, Fehler, veraltete Erfassung oder Profilwechsel beenden die Sitzung — wie unter Windows.
            Close();
        }
    }

    private void Close()
    {
        if (closed) return;
        closed = true;
        timer?.Stop();
        monitor.Changed -= OnMonitorChanged;
        try { (view.Handler?.PlatformView as AndroidWebView)?.StopLoading(); }
        catch (Exception) { }
        PurgeSession();
        if (Navigation.ModalStack.Contains(this)) _ = Navigation.PopModalAsync();
    }

    private sealed class RouterWebViewClient(RouterPage page) : AndroidWebViewClient
    {
        public override bool ShouldOverrideUrlLoading(AndroidWebView? view, AndroidResourceRequest? request)
            => page.BlockIfForeign(request?.Url?.ToString());

        public override void OnReceivedSslError(AndroidWebView? view, AndroidSslErrorHandler? handler, AndroidSslError? error)
        {
            handler?.Cancel();
            page.ReportSslProblem(error?.PrimaryError.ToString() ?? "unbekannter Zertifikatsfehler");
        }
    }

    private sealed class BlockedDownloadListener(RouterPage page) : Java.Lang.Object, AndroidDownloadListener
    {
        public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition, string? mimetype, long contentLength) => page.ReportBlockedDownload();
    }
}
