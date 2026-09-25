using Android.App;
using Android.Content.PM;
using Android.OS;

namespace SysWlan.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Android hält den Prozess nach dem Verlassen der App weiter am Leben. Ohne Vordergrunddienst wird die
    /// Erfassung deshalb ausgesetzt und die Lücke protokolliert, statt im Hintergrund weiterzumessen.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();
        AndroidAppLifecycle.SetForeground(true);
    }

    protected override void OnPause()
    {
        AndroidAppLifecycle.SetForeground(false);
        base.OnPause();
    }
}
