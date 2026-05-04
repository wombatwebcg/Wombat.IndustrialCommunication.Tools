using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Extensions.Bluetooth;
using Wombat.IndustrialCommunication.Extensions.Bluetooth.Models;
using Wombat.IndustrialCommunication.Tools.Models;
using Wombat.IndustrialCommunication.Tools.Services.Platform;

namespace Wombat.IndustrialCommunication.Tools.Desktop.Platform;

public sealed class DesktopBluetoothPlatformService : IBluetoothPlatformService
{
    private readonly UnsupportedBluetoothPlatformService _inner =
        new("当前桌面宿主仅在 Windows 目标框架下支持 BLE/GATT。");

    public bool IsSupported => _inner.IsSupported;

    public string AvailabilityMessage => _inner.AvailabilityMessage;

    public Task<OperationResult<IReadOnlyList<BluetoothDeviceOption>>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        return _inner.GetDevicesAsync(cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<BluetoothServiceOption>>> GetServicesAsync(string deviceId, CancellationToken cancellationToken)
    {
        return _inner.GetServicesAsync(deviceId, cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<BluetoothCharacteristicOption>>> GetCharacteristicsAsync(string deviceId, string serviceId, CancellationToken cancellationToken)
    {
        return _inner.GetCharacteristicsAsync(deviceId, serviceId, cancellationToken);
    }

    public OperationResult<IBluetoothChannel> CreateChannel(BluetoothConnectionOptions options)
    {
        return _inner.CreateChannel(options);
    }

    public OperationResult<IBluetoothChannel> CreateLocalServerChannel(BluetoothServerOptions options)
    {
        return _inner.CreateLocalServerChannel(options);
    }
}
