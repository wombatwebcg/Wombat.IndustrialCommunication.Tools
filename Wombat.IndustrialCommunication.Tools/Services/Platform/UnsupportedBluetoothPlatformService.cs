using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Extensions.Bluetooth;
using Wombat.IndustrialCommunication.Extensions.Bluetooth.Models;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.Services.Platform;

public sealed class UnsupportedBluetoothPlatformService(string? message = null) : IBluetoothPlatformService
{
    private readonly string _message = string.IsNullOrWhiteSpace(message) ? "当前宿主暂不支持蓝牙透传 Modbus RTU。" : message;

    public bool IsSupported => false;

    public string AvailabilityMessage => _message;

    public Task<OperationResult<IReadOnlyList<BluetoothDeviceOption>>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(OperationResult.CreateFailedResult<IReadOnlyList<BluetoothDeviceOption>>(_message));
    }

    public Task<OperationResult<IReadOnlyList<BluetoothServiceOption>>> GetServicesAsync(string deviceId, CancellationToken cancellationToken)
    {
        return Task.FromResult(OperationResult.CreateFailedResult<IReadOnlyList<BluetoothServiceOption>>(_message));
    }

    public Task<OperationResult<IReadOnlyList<BluetoothCharacteristicOption>>> GetCharacteristicsAsync(string deviceId, string serviceId, CancellationToken cancellationToken)
    {
        return Task.FromResult(OperationResult.CreateFailedResult<IReadOnlyList<BluetoothCharacteristicOption>>(_message));
    }

    public OperationResult<IBluetoothChannel> CreateChannel(BluetoothConnectionOptions options)
    {
        return OperationResult.CreateFailedResult<IBluetoothChannel>(_message);
    }

    public OperationResult<IBluetoothChannel> CreateLocalServerChannel(BluetoothServerOptions options)
    {
        return OperationResult.CreateFailedResult<IBluetoothChannel>(_message);
    }
}
