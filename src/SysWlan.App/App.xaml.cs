namespace SysWlan.App;
public partial class App : Application
{
    public App() { InitializeComponent(); }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "SysWLANInfo · Netzwerkzentrale", Width = 1360, Height = 880, MinimumWidth = 760, MinimumHeight = 600 };
        window.Destroying += (_, _) =>
        {
            var services = IPlatformApplication.Current?.Services;
            services?.GetService<SysWlan.Core.MonitorService>()?.Dispose();
            services?.GetService<SysWlan.Core.SyslogReceiver>()?.Dispose();
        };
        return window;
    }
}
