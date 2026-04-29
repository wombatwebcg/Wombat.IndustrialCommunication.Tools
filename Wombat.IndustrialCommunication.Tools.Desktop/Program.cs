using System;
using Avalonia;
using Wombat.IndustrialCommunication.Tools.Desktop.Platform;
using Wombat.IndustrialCommunication.Tools.Infrastructure;

namespace Wombat.IndustrialCommunication.Tools.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppServices.Configure(new DesktopBluetoothPlatformService());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
