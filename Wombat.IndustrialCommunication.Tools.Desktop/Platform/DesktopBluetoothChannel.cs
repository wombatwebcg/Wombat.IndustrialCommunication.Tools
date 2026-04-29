using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Extensions.Bluetooth;

namespace Wombat.IndustrialCommunication.Tools.Desktop.Platform;

public sealed class DesktopBluetoothChannel : IBluetoothChannel
{
    private readonly string _deviceId;
    private readonly Guid _serviceId;
    private readonly Guid _writeCharacteristicId;
    private readonly Guid _notifyCharacteristicId;
    private readonly Queue<byte> _receiveBuffer = new();
    private readonly SemaphoreSlim _bufferSignal = new(0);
    private readonly object _receiveSync = new();
    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _writeCharacteristic;
    private GattCharacteristic? _notifyCharacteristic;
    private bool _disposed;
    private bool _notificationsEnabled;
    private bool _usesNotifications;

    public DesktopBluetoothChannel(
        string deviceId,
        string serviceId,
        string writeCharacteristicId,
        string notifyCharacteristicId,
        TimeSpan connectTimeout,
        TimeSpan receiveTimeout,
        TimeSpan sendTimeout)
    {
        _deviceId = string.IsNullOrWhiteSpace(deviceId) ? throw new ArgumentException("BLE 设备标识不能为空。", nameof(deviceId)) : deviceId;
        _serviceId = ParseGuid(serviceId, nameof(serviceId));
        _writeCharacteristicId = ParseGuid(writeCharacteristicId, nameof(writeCharacteristicId));
        _notifyCharacteristicId = ParseGuid(notifyCharacteristicId, nameof(notifyCharacteristicId));
        ConnectTimeout = connectTimeout;
        ReceiveTimeout = receiveTimeout;
        SendTimeout = sendTimeout;
    }

    public bool Connected => !_disposed && _device != null && _service != null && _writeCharacteristic != null && _notifyCharacteristic != null;

    public TimeSpan ConnectTimeout { get; set; }

    public TimeSpan ReceiveTimeout { get; set; }

    public TimeSpan SendTimeout { get; set; }

    public async Task<OperationResult> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return OperationResult.CreateFailedResult("BLE 通道已释放。");
        }

        if (Connected)
        {
            return OperationResult.CreateSuccessResult("BLE 通道已连接。");
        }

        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ConnectTimeout);

            var device = await BluetoothLEDevice.FromIdAsync(_deviceId).AsTask(timeoutCts.Token).ConfigureAwait(false);
            if (device == null)
            {
                return OperationResult.CreateFailedResult("无法访问 BLE 设备，请确认设备仍在附近且系统已授予访问权限。");
            }

            var serviceResult = await device.GetGattServicesForUuidAsync(_serviceId, BluetoothCacheMode.Uncached).AsTask(timeoutCts.Token).ConfigureAwait(false);
            if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services == null || serviceResult.Services.Count == 0)
            {
                device.Dispose();
                return OperationResult.CreateFailedResult($"BLE 服务访问失败: {serviceResult.Status}");
            }

            var service = serviceResult.Services[0];
            var writeCharacteristic = await ResolveCharacteristicAsync(service, _writeCharacteristicId, timeoutCts.Token).ConfigureAwait(false);
            var notifyCharacteristic = _notifyCharacteristicId == _writeCharacteristicId
                ? writeCharacteristic
                : await ResolveCharacteristicAsync(service, _notifyCharacteristicId, timeoutCts.Token).ConfigureAwait(false);

            if (!CanWrite(writeCharacteristic))
            {
                service.Dispose();
                device.Dispose();
                return OperationResult.CreateFailedResult("写入特征不支持写操作。");
            }

            if (!CanNotify(notifyCharacteristic) && !CanRead(notifyCharacteristic))
            {
                service.Dispose();
                device.Dispose();
                return OperationResult.CreateFailedResult("接收特征既不支持通知，也不支持读取。");
            }

            _device = device;
            _service = service;
            _writeCharacteristic = writeCharacteristic;
            _notifyCharacteristic = notifyCharacteristic;
            ClearReceiveBuffer();
            _usesNotifications = false;
            _notificationsEnabled = false;

            if (CanNotify(_notifyCharacteristic))
            {
                _notifyCharacteristic.ValueChanged += NotifyCharacteristicOnValueChanged;
                var configuration = _notifyCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate)
                    ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                    : GattClientCharacteristicConfigurationDescriptorValue.Notify;
                var notifyStatus = await _notifyCharacteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(configuration)
                    .AsTask(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (notifyStatus != GattCommunicationStatus.Success)
                {
                    await DisconnectCoreAsync().ConfigureAwait(false);
                    return OperationResult.CreateFailedResult($"BLE 通知订阅失败: {notifyStatus}");
                }

                _notificationsEnabled = true;
                _usesNotifications = true;
            }

            return OperationResult.CreateSuccessResult("BLE 连接成功。");
        }
        catch (OperationCanceledException)
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            return OperationResult.CreateFailedResult("BLE 连接超时。");
        }
        catch (Exception ex)
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            return OperationResult.CreateFailedResult(ex);
        }
    }

    public async Task<OperationResult> DisconnectAsync()
    {
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            return OperationResult.CreateSuccessResult("BLE 已断开。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult(ex);
        }
    }

    public async Task<OperationResult<int>> ReceiveAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken)
    {
        if (!Connected)
        {
            return OperationResult.CreateFailedResult<int>("BLE 未连接。");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ReceiveTimeout);

            var totalRead = 0;
            while (totalRead < size)
            {
                totalRead += DrainBufferedData(buffer, offset + totalRead, size - totalRead);
                if (totalRead >= size)
                {
                    break;
                }

                if (_usesNotifications)
                {
                    await _bufferSignal.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                    continue;
                }

                var pollResult = await PollReadCharacteristicAsync(timeoutCts.Token).ConfigureAwait(false);
                if (!pollResult.IsSuccess)
                {
                    return OperationResult.CreateFailedResult<int>(pollResult);
                }
            }

            return OperationResult.CreateSuccessResult(totalRead);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailedResult<int>("BLE 接收超时。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<int>(ex);
        }
    }

    public async Task<OperationResult> SendAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken)
    {
        if (!Connected || _writeCharacteristic == null)
        {
            return OperationResult.CreateFailedResult("BLE 未连接。");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(SendTimeout);

            var payload = new byte[size];
            Array.Copy(buffer, offset, payload, 0, size);
            using var writer = new DataWriter();
            writer.WriteBytes(payload);

            var writeOption = _writeCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                ? GattWriteOption.WriteWithoutResponse
                : GattWriteOption.WriteWithResponse;
            var result = await _writeCharacteristic
                .WriteValueWithResultAsync(writer.DetachBuffer(), writeOption)
                .AsTask(timeoutCts.Token)
                .ConfigureAwait(false);

            return result.Status == GattCommunicationStatus.Success
                ? OperationResult.CreateSuccessResult()
                : OperationResult.CreateFailedResult($"BLE 发送失败: {result.Status}");
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailedResult("BLE 发送超时。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult(ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisconnectCoreAsync().GetAwaiter().GetResult();
        _bufferSignal.Dispose();
    }

    private static bool CanWrite(GattCharacteristic characteristic)
    {
        var properties = characteristic.CharacteristicProperties;
        return properties.HasFlag(GattCharacteristicProperties.Write) ||
               properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse);
    }

    private static bool CanRead(GattCharacteristic characteristic)
    {
        return characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read);
    }

    private static bool CanNotify(GattCharacteristic characteristic)
    {
        var properties = characteristic.CharacteristicProperties;
        return properties.HasFlag(GattCharacteristicProperties.Notify) ||
               properties.HasFlag(GattCharacteristicProperties.Indicate);
    }

    private static Guid ParseGuid(string value, string parameterName)
    {
        return Guid.TryParse(value, out var guid)
            ? guid
            : throw new ArgumentException("UUID 格式无效。", parameterName);
    }

    private static byte[] ReadBuffer(IBuffer buffer)
    {
        if (buffer.Length == 0)
        {
            return [];
        }

        using var reader = DataReader.FromBuffer(buffer);
        var data = new byte[buffer.Length];
        reader.ReadBytes(data);
        return data;
    }

    private static async Task<GattCharacteristic> ResolveCharacteristicAsync(GattDeviceService service, Guid characteristicId, CancellationToken cancellationToken)
    {
        var result = await service.GetCharacteristicsForUuidAsync(characteristicId, BluetoothCacheMode.Uncached).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics == null || result.Characteristics.Count == 0)
        {
            throw new InvalidOperationException($"未找到指定特征 {characteristicId}。");
        }

        return result.Characteristics[0];
    }

    private void AppendReceivedData(byte[] data)
    {
        if (data.Length == 0)
        {
            return;
        }

        lock (_receiveSync)
        {
            foreach (var value in data)
            {
                _receiveBuffer.Enqueue(value);
            }
        }

        _bufferSignal.Release();
    }

    private void ClearReceiveBuffer()
    {
        lock (_receiveSync)
        {
            _receiveBuffer.Clear();
        }

        while (_bufferSignal.CurrentCount > 0)
        {
            _bufferSignal.Wait(0);
        }
    }

    private int DrainBufferedData(byte[] target, int offset, int size)
    {
        var copied = 0;
        lock (_receiveSync)
        {
            while (copied < size && _receiveBuffer.Count > 0)
            {
                target[offset + copied] = _receiveBuffer.Dequeue();
                copied++;
            }
        }

        return copied;
    }

    private async Task DisconnectCoreAsync()
    {
        if (_notifyCharacteristic != null)
        {
            _notifyCharacteristic.ValueChanged -= NotifyCharacteristicOnValueChanged;
            if (_notificationsEnabled)
            {
                try
                {
                    await _notifyCharacteristic
                        .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None)
                        .AsTask()
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        _notificationsEnabled = false;
        _usesNotifications = false;
        ClearReceiveBuffer();

        _notifyCharacteristic = null;
        _writeCharacteristic = null;
        _service?.Dispose();
        _service = null;
        _device?.Dispose();
        _device = null;
    }

    private void NotifyCharacteristicOnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        AppendReceivedData(ReadBuffer(args.CharacteristicValue));
    }

    private async Task<OperationResult> PollReadCharacteristicAsync(CancellationToken cancellationToken)
    {
        if (_notifyCharacteristic == null)
        {
            return OperationResult.CreateFailedResult("接收特征不可用。");
        }

        if (!CanRead(_notifyCharacteristic))
        {
            return OperationResult.CreateFailedResult("接收特征不支持读取。");
        }

        var result = await _notifyCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success || result.Value == null)
        {
            return OperationResult.CreateFailedResult($"BLE 读取特征失败: {result.Status}");
        }

        AppendReceivedData(ReadBuffer(result.Value));
        if (!HasBufferedData())
        {
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.CreateSuccessResult();
    }

    private bool HasBufferedData()
    {
        lock (_receiveSync)
        {
            return _receiveBuffer.Count > 0;
        }
    }
}
