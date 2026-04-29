using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Extensions.Bluetooth;
using Wombat.IndustrialCommunication.Extensions.Bluetooth.Models;
using Wombat.IndustrialCommunication.Tools.Models;
using Wombat.IndustrialCommunication.Tools.Services.Platform;

namespace Wombat.IndustrialCommunication.Tools.Desktop.Platform;

public sealed class DesktopBluetoothPlatformService : IBluetoothPlatformService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public string AvailabilityMessage =>
        IsSupported
            ? "当前桌面宿主支持 Windows BLE/GATT。"
            : "当前桌面宿主仅支持 Windows BLE/GATT。";

    public async Task<OperationResult<IReadOnlyList<BluetoothDeviceOption>>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pairedSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var unpairedSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(false);
            var pairedDevices = await DeviceInformation.FindAllAsync(pairedSelector);
            var unpairedDevices = await DeviceInformation.FindAllAsync(unpairedSelector);

            var devices = pairedDevices
                .Concat(unpairedDevices)
                .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var device = group.First();
                    return new BluetoothDeviceOption
                    {
                        DeviceId = device.Id,
                        DisplayName = string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                        IsPaired = device.Pairing?.IsPaired == true,
                        Description = device.Pairing?.IsPaired == true ? "已配对 BLE 设备" : "已发现 BLE 设备"
                    };
                })
                .OrderByDescending(device => device.IsPaired)
                .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return OperationResult.CreateSuccessResult<IReadOnlyList<BluetoothDeviceOption>>(devices);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothDeviceOption>>("BLE 设备加载已取消。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothDeviceOption>>(ex);
        }
    }

    public async Task<OperationResult<IReadOnlyList<BluetoothServiceOption>>> GetServicesAsync(string deviceId, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothServiceOption>>("BLE 设备标识不能为空。");
            }

            await using var context = await BleDeviceContext.CreateAsync(deviceId, cancellationToken).ConfigureAwait(false);
            var result = await context.Device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (result.Status != GattCommunicationStatus.Success || result.Services == null)
            {
                return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothServiceOption>>($"BLE 服务发现失败: {result.Status}");
            }

            var services = result.Services
                .Select(service => new BluetoothServiceOption
                {
                    ServiceId = service.Uuid.ToString(),
                    DisplayName = service.Uuid.ToString(),
                    IsPrimary = true,
                    Description = $"AttributeHandle: 0x{service.AttributeHandle:X4}"
                })
                .GroupBy(service => service.ServiceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return OperationResult.CreateSuccessResult<IReadOnlyList<BluetoothServiceOption>>(services);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothServiceOption>>("BLE 服务加载已取消。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothServiceOption>>(ex);
        }
    }

    public async Task<OperationResult<IReadOnlyList<BluetoothCharacteristicOption>>> GetCharacteristicsAsync(string deviceId, string serviceId, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothCharacteristicOption>>("BLE 设备标识不能为空。");
            }

            if (!Guid.TryParse(serviceId, out var serviceGuid))
            {
                return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothCharacteristicOption>>("BLE 服务 UUID 格式无效。");
            }

            await using var context = await BleDeviceContext.CreateAsync(deviceId, cancellationToken).ConfigureAwait(false);
            var serviceResult = await context.Device.GetGattServicesForUuidAsync(serviceGuid, BluetoothCacheMode.Uncached);
            if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services == null || serviceResult.Services.Count == 0)
            {
                return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothCharacteristicOption>>($"BLE 服务访问失败: {serviceResult.Status}");
            }

            using var service = serviceResult.Services[0];
            var characteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            if (characteristicsResult.Status != GattCommunicationStatus.Success || characteristicsResult.Characteristics == null)
            {
                return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothCharacteristicOption>>($"BLE 特征发现失败: {characteristicsResult.Status}");
            }

            var characteristics = characteristicsResult.Characteristics
                .Select(characteristic =>
                {
                    var properties = characteristic.CharacteristicProperties;
                    var canRead = properties.HasFlag(GattCharacteristicProperties.Read);
                    var canWrite = properties.HasFlag(GattCharacteristicProperties.Write) ||
                                   properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse);
                    var canNotify = properties.HasFlag(GattCharacteristicProperties.Notify) ||
                                    properties.HasFlag(GattCharacteristicProperties.Indicate);

                    return new BluetoothCharacteristicOption
                    {
                        CharacteristicId = characteristic.Uuid.ToString(),
                        DisplayName = characteristic.Uuid.ToString(),
                        CanRead = canRead,
                        CanWrite = canWrite,
                        CanNotify = canNotify,
                        Description = $"Read={canRead}, Write={canWrite}, Notify={canNotify}, Handle=0x{characteristic.AttributeHandle:X4}"
                    };
                })
                .GroupBy(characteristic => characteristic.CharacteristicId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(characteristic => characteristic.CanNotify)
                .ThenByDescending(characteristic => characteristic.CanWrite)
                .ThenBy(characteristic => characteristic.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return OperationResult.CreateSuccessResult<IReadOnlyList<BluetoothCharacteristicOption>>(characteristics);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothCharacteristicOption>>("BLE 特征加载已取消。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<IReadOnlyList<BluetoothCharacteristicOption>>(ex);
        }
    }

    public OperationResult<IBluetoothChannel> CreateChannel(BluetoothConnectionOptions options)
    {
        try
        {
            var validation = options.Validate();
            if (!validation.IsSuccess)
            {
                return OperationResult.CreateFailedResult<IBluetoothChannel>(validation);
            }

            return OperationResult.CreateSuccessResult<IBluetoothChannel>(
                new DesktopBluetoothChannel(
                    options.DeviceId,
                    options.ServiceId,
                    options.WriteCharacteristicId,
                    options.NotifyCharacteristicId,
                    options.ConnectTimeout,
                    options.ReceiveTimeout,
                    options.SendTimeout));
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<IBluetoothChannel>(ex);
        }
    }

    public OperationResult<IBluetoothChannel> CreateLocalServerChannel(BluetoothServerOptions options)
    {
        try
        {
            var validation = options.Validate();
            if (!validation.IsSuccess)
            {
                return OperationResult.CreateFailedResult<IBluetoothChannel>(validation);
            }

            return OperationResult.CreateSuccessResult<IBluetoothChannel>(
                new DesktopBluetoothServerChannel(
                    options.ServiceId,
                    options.WriteCharacteristicId,
                    options.NotifyCharacteristicId,
                    options.ConnectTimeout,
                    options.ReceiveTimeout,
                    options.SendTimeout));
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<IBluetoothChannel>(ex);
        }
    }

    private sealed class BleDeviceContext(BluetoothLEDevice device) : IAsyncDisposable
    {
        public BluetoothLEDevice Device { get; } = device;

        public static async Task<BleDeviceContext> CreateAsync(string deviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (device == null)
            {
                throw new InvalidOperationException("无法创建 BLE 设备连接，请确认设备仍在附近且系统已授予访问权限。");
            }

            return new BleDeviceContext(device);
        }

        public ValueTask DisposeAsync()
        {
            Device.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
