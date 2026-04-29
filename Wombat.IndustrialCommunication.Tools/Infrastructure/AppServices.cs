using Wombat.IndustrialCommunication.Tools.Services;
using Wombat.IndustrialCommunication.Tools.Services.Platform;
using Wombat.IndustrialCommunication.Tools.ViewModels;

namespace Wombat.IndustrialCommunication.Tools.Infrastructure;

public static class AppServices
{
    private static IBluetoothPlatformService _bluetoothPlatformService = new UnsupportedBluetoothPlatformService();
    private static bool _isConfigured;

    public static void Configure(IBluetoothPlatformService? bluetoothPlatformService = null)
    {
        if (_isConfigured && bluetoothPlatformService == null)
        {
            return;
        }

        _bluetoothPlatformService = bluetoothPlatformService ?? new UnsupportedBluetoothPlatformService();
        _isConfigured = true;
    }

    public static MainViewModel CreateMainViewModel()
    {
        return new MainViewModel(new DeviceSessionService(_bluetoothPlatformService), _bluetoothPlatformService);
    }
}
