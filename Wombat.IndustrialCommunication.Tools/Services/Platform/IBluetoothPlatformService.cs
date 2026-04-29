using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Extensions.Bluetooth;
using Wombat.IndustrialCommunication.Extensions.Bluetooth.Models;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.Services.Platform;

public interface IBluetoothPlatformService
{
    bool IsSupported { get; }

    string AvailabilityMessage { get; }

    Task<OperationResult<IReadOnlyList<BluetoothDeviceOption>>> GetDevicesAsync(CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<BluetoothServiceOption>>> GetServicesAsync(string deviceId, CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<BluetoothCharacteristicOption>>> GetCharacteristicsAsync(string deviceId, string serviceId, CancellationToken cancellationToken);

    OperationResult<IBluetoothChannel> CreateChannel(BluetoothConnectionOptions options);

    OperationResult<IBluetoothChannel> CreateLocalServerChannel(BluetoothServerOptions options);
}
