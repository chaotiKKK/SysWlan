using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using SysWlan.Core;

namespace SysWlan.App;

/// <summary>
/// Optionaler Vordergrunddienst: hält den Prozess wach, damit die Erfassung auch ohne sichtbare App läuft,
/// und zeigt das dauerhaft in einer Benachrichtigung. Der Dienst wird ausschließlich aus einer Nutzeraktion
/// gestartet, weil Android Hintergrundstarts verbietet und weil Dauermonitoring eine bewusste Entscheidung ist.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class MonitoringForegroundService : Service
{
    public const string ChannelId = "syswlaninfo-monitoring";
    public const int NotificationId = 4101;
    private const string WakeLockTag = "SysWLANInfo::Monitoring";
    private PowerManager.WakeLock? wakeLock;
    public static bool Running { get; private set; }

    public static void Start()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(MonitoringForegroundService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
        else context.StartService(intent);
    }

    public static void Stop() => global::Android.App.Application.Context.StopService(new Intent(global::Android.App.Application.Context, typeof(MonitoringForegroundService)));

    public static bool NotificationsAllowed()
    {
        try
        {
            var manager = global::Android.App.Application.Context.GetSystemService(NotificationService) as NotificationManager;
            return manager?.AreNotificationsEnabled() == true;
        }
        catch (Exception) { return false; }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartForeground(NotificationId, BuildNotification());
        AcquireWakeLock();
        Running = true;
        Log("Info", "Vordergrunddienst gestartet: Monitoring läuft auch bei nicht sichtbarer App.");
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        Running = false;
        ReleaseWakeLock();
        Log("Info", "Vordergrunddienst beendet. Im Hintergrund pausiert die Erfassung wieder.");
        base.OnDestroy();
    }

    /// <summary>
    /// Android beendet dataSync-Dienste nach einer Systemgrenze (Android 15: etwa sechs Stunden).
    /// Der Dienst stoppt dann selbst und protokolliert das, damit keine stille Lücke entsteht.
    /// </summary>
    public override void OnTimeout(int startId)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(34)) return;
        Running = false;
        Log("Warnung", "Android hat den Vordergrunddienst wegen der Systemgrenze beendet (dataSync ist zeitlich begrenzt). Monitoring läuft nur bei sichtbarer App weiter; im Hintergrund neu starten, wenn Dauermonitoring gewünscht ist.");
        base.OnTimeout(startId);
        StopSelf(startId);
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        Log("Info", "Die App wurde aus der Übersicht entfernt; der Vordergrunddienst läuft weiter.");
        base.OnTaskRemoved(rootIntent);
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private Notification BuildNotification()
    {
        var manager = GetSystemService(NotificationService) as NotificationManager;
        var channel = new NotificationChannel(ChannelId, "Netzwerküberwachung", NotificationImportance.Low)
        {
            Description = "Zeigt an, dass SysWLANInfo im Hintergrund Netzwerkdaten erfasst."
        };
        manager?.CreateNotificationChannel(channel);
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var builder = new Notification.Builder(this, ChannelId)
            .SetContentTitle("SysWLANInfo erfasst das Netzwerk")
            .SetContentText("Dauermonitoring aktiv · Messwerte bleiben lokal auf diesem Gerät")
            .SetContentIntent(open)
            .SetOngoing(true)
            .SetShowWhen(false);
        var icon = Resources?.GetIdentifier("notification_status", "drawable", PackageName) ?? 0;
        builder.SetSmallIcon(icon != 0 ? icon : global::Android.Resource.Drawable.IcDialogInfo);
        return builder.Build();
    }

    private void AcquireWakeLock()
    {
        try
        {
            var power = GetSystemService(PowerService) as PowerManager;
            wakeLock = power?.NewWakeLock(WakeLockFlags.Partial, WakeLockTag);
            wakeLock?.SetReferenceCounted(false);
            wakeLock?.Acquire();
        }
        catch (Exception) { wakeLock = null; }
    }

    private void ReleaseWakeLock()
    {
        try { if (wakeLock?.IsHeld == true) wakeLock.Release(); }
        catch (Exception) { }
        wakeLock = null;
    }

    private static void Log(string severity, string message)
    {
        try { IPlatformApplication.Current?.Services.GetService<Store>()?.AddLog(new LogEntry(DateTimeOffset.UtcNow, "App", severity, message)); }
        catch (Exception) { }
    }
}
