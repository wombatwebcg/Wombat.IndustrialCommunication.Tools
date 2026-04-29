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

public sealed class DesktopBluetoothServerChannel : IBluetoothChannel
{
    private readonly Guid _serviceId;
    private readonly Guid _writeCharacteristicId;
    private readonly Guid _notifyCharacteristicId;
    private readonly Queue<byte> _receiveBuffer = new();
    private readonly SemaphoreSlim _bufferSignal = new(0);
    private readonly object _receiveSync = new();

    private GattServiceProvider? _serviceProvider;
    private GattLocalCharacteristic? _writeCharacteristic;
    private GattLocalCharacteristic? _notifyCharacteristic;
    private bool _disposed;
    private bool _started;

    public DesktopBluetoothServerChannel(
        string serviceId,
        string writeCharacteristicId,
        string notifyCharacteristicId,
        TimeSpan connectTimeout,
        TimeSpan receiveTimeout,
        TimeSpan sendTimeout)
    {
        _serviceId = ParseGuid(serviceId, nameof(serviceId));
        _writeCharacteristicId = ParseGuid(writeCharacteristicId, nameof(writeCharacteristicId));
        _notifyCharacteristicId = ParseGuid(notifyCharacteristicId, nameof(notifyCharacteristicId));
        ConnectTimeout = connectTimeout;
        ReceiveTimeout = receiveTimeout;
        SendTimeout = sendTimeout;
    }

    public bool Connected => !_disposed && _started;
    public TimeSpan ConnectTimeout { get; set; }
    public TimeSpan ReceiveTimeout { get; set; }
    public TimeSpan SendTimeout { get; set; }

    public async Task<OperationResult> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return OperationResult.CreateFailedResult("蓝牙服务通道已释放。");
        }

        if (_started)
        {
            return OperationResult.CreateSuccessResult("蓝牙服务已启动。");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ConnectTimeout);

            var providerResult = await GattServiceProvider.CreateAsync(_serviceId).AsTask(timeoutCts.Token).ConfigureAwait(false);
            if (providerResult.Error != BluetoothError.Success || providerResult.ServiceProvider == null)
            {
                return OperationResult.CreateFailedResult($"创建本机 BLE 服务失败: {providerResult.Error}");
            }

            _serviceProvider = providerResult.ServiceProvider;
            var writeResult = await _serviceProvider.Service.CreateCharacteristicAsync(_writeCharacteristicId, CreateWriteParameters()).AsTask(timeoutCts.Token).ConfigureAwait(false);
            if (writeResult.Error != BluetoothError.Success || writeResult.Characteristic == null)
            {
                await DisconnectCoreAsync().ConfigureAwait(false);
                return OperationResult.CreateFailedResult($"创建写入特征失败: {writeResult.Error}");
            }

            var notifyResult = await _serviceProvider.Service.CreateCharacteristicAsync(_notifyCharacteristicId, CreateNotifyParameters()).AsTask(timeoutCts.Token).ConfigureAwait(false);
            if (notifyResult.Error != BluetoothError.Success || notifyResult.Characteristic == null)
            {
                await DisconnectCoreAsync().ConfigureAwait(false);
                return OperationResult.CreateFailedResult($"创建通知特征失败: {notifyResult.Error}");
            }

            _writeCharacteristic = writeResult.Characteristic;
            _notifyCharacteristic = notifyResult.Characteristic;
            _writeCharacteristic.WriteRequested += OnWriteRequested;

            _serviceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters
            {
                IsConnectable = true,
                IsDiscoverable = true
            });

            _started = true;
            return OperationResult.CreateSuccessResult("本机 BLE 服务已启动。");
        }
        catch (OperationCanceledException)
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            return OperationResult.CreateFailedResult("本机 BLE 服务启动超时。");
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
            return OperationResult.CreateSuccessResult("本机 BLE 服务已停止。");
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
            return OperationResult.CreateFailedResult<int>("本机 BLE 服务未启动。");
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

                await _bufferSignal.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }

            return OperationResult.CreateSuccessResult(totalRead);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailedResult<int>("本机 BLE 服务接收超时。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<int>(ex);
        }
    }

    public async Task<OperationResult> SendAsync(byte[] buffer, int offset, int size, CancellationToken cancellationToken)
    {
        if (!Connected || _notifyCharacteristic == null)
        {
            return OperationResult.CreateFailedResult("本机 BLE 服务未启动。");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(SendTimeout);

            var payload = new byte[size];
            Array.Copy(buffer, offset, payload, 0, size);
            using var writer = new DataWriter();
            writer.WriteBytes(payload);

            var notifyResults = await _notifyCharacteristic.NotifyValueAsync(writer.DetachBuffer()).AsTask(timeoutCts.Token).ConfigureAwait(false);
            if (notifyResults != null)
            {
                foreach (var notifyResult in notifyResults)
                {
                    if (notifyResult.Status != GattCommunicationStatus.Success)
                    {
                        return OperationResult.CreateFailedResult($"本机 BLE 服务发送失败: {notifyResult.Status}");
                    }
                }
            }

            return OperationResult.CreateSuccessResult();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.CreateFailedResult("本机 BLE 服务发送超时。");
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

    private static Guid ParseGuid(string value, string parameterName)
    {
        return Guid.TryParse(value, out var guid)
            ? guid
            : throw new ArgumentException("UUID 格式无效。", parameterName);
    }

    private static GattLocalCharacteristicParameters CreateWriteParameters()
    {
        return new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "Modbus RTU Write"
        };
    }

    private static GattLocalCharacteristicParameters CreateNotifyParameters()
    {
        return new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.Plain,
            UserDescription = "Modbus RTU Notify"
        };
    }

    private async Task DisconnectCoreAsync()
    {
        _started = false;
        ClearReceiveBuffer();

        if (_writeCharacteristic != null)
        {
            _writeCharacteristic.WriteRequested -= OnWriteRequested;
        }

        _writeCharacteristic = null;
        _notifyCharacteristic = null;

        if (_serviceProvider != null)
        {
            _serviceProvider.StopAdvertising();
        }

        _serviceProvider = null;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async void OnWriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        try
        {
            var request = await args.GetRequestAsync().AsTask().ConfigureAwait(false);
            if (request?.Value != null)
            {
                AppendReceivedData(ReadBuffer(request.Value));
                request.Respond();
            }
        }
        catch
        {
        }
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
}
