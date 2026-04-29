using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task RefreshBluetoothDevicesAsync()
    {
        if (!ConnectionForm.SupportsBluetoothDiscovery)
        {
            return;
        }

        if (!_bluetoothPlatformService.IsSupported)
        {
            ConnectionForm.BluetoothStatusText = _bluetoothPlatformService.AvailabilityMessage;
            LatestResult = _bluetoothPlatformService.AvailabilityMessage;
            StatusBrush = Brushes.IndianRed;
            AppendLog("刷新蓝牙设备", false, _bluetoothPlatformService.AvailabilityMessage, null);
            return;
        }

        ConnectionForm.IsBluetoothBusy = true;
        ConnectionForm.BluetoothStatusText = "正在扫描 BLE 设备...";
        try
        {
            var result = await _bluetoothPlatformService.GetDevicesAsync(CancellationToken.None);
            if (result.IsSuccess && result.ResultValue != null)
            {
                ReplaceBluetoothDevices(result.ResultValue);
                var count = result.ResultValue.Count;
                ConnectionForm.BluetoothStatusText = count > 0 ? $"已加载 {count} 个 BLE 设备，请先选择目标设备。" : "未发现可用 BLE 设备。";
                LatestResult = ConnectionForm.BluetoothStatusText;
                StatusBrush = Brushes.SeaGreen;
            }
            else
            {
                ConnectionForm.BluetoothDevices.Clear();
                ConnectionForm.BluetoothServices.Clear();
                ConnectionForm.BluetoothCharacteristics.Clear();
                ConnectionForm.SelectedBluetoothDevice = null;
                ConnectionForm.BluetoothStatusText = result.Message;
                LatestResult = result.Message;
                StatusBrush = Brushes.IndianRed;
            }

            AppendLog("刷新 BLE 设备", result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            ConnectionForm.IsBluetoothBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshBluetoothServicesAsync()
    {
        if (!ConnectionForm.SupportsBluetoothServiceSelection)
        {
            return;
        }

        if (!_bluetoothPlatformService.IsSupported)
        {
            ConnectionForm.BluetoothStatusText = _bluetoothPlatformService.AvailabilityMessage;
            LatestResult = _bluetoothPlatformService.AvailabilityMessage;
            StatusBrush = Brushes.IndianRed;
            AppendLog("刷新 BLE 服务", false, _bluetoothPlatformService.AvailabilityMessage, null);
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothDeviceId))
        {
            ApplyFailure("请先选择 BLE 设备。");
            return;
        }

        ConnectionForm.IsBluetoothBusy = true;
        ConnectionForm.BluetoothStatusText = "正在自动识别 BLE 服务与特征...";
        try
        {
            var result = await _bluetoothPlatformService.GetServicesAsync(ConnectionForm.BluetoothDeviceId, CancellationToken.None);
            if (result.IsSuccess && result.ResultValue != null)
            {
                ReplaceBluetoothServices(result.ResultValue);
                var count = result.ResultValue.Count;
                if (count == 0)
                {
                    ConnectionForm.BluetoothStatusText = "未发现 BLE 服务。";
                    LatestResult = ConnectionForm.BluetoothStatusText;
                    StatusBrush = Brushes.IndianRed;
                }
                else
                {
                    var resolved = await TryAutoSelectBluetoothRouteAsync(result.ResultValue);
                    ConnectionForm.BluetoothStatusText = resolved
                        ? "已自动识别 BLE 服务和收发特征。"
                        : $"已找到 {count} 个 BLE 服务，但未识别出可用的写入/通知特征组合。";
                    LatestResult = ConnectionForm.BluetoothStatusText;
                    StatusBrush = resolved ? Brushes.SeaGreen : Brushes.DarkOrange;
                }
            }
            else
            {
                ConnectionForm.BluetoothServices.Clear();
                ConnectionForm.BluetoothCharacteristics.Clear();
                ConnectionForm.SelectedBluetoothService = null;
                ConnectionForm.BluetoothStatusText = result.Message;
                LatestResult = result.Message;
                StatusBrush = Brushes.IndianRed;
            }

            AppendLog("刷新 BLE 服务", result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            ConnectionForm.IsBluetoothBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshBluetoothCharacteristicsAsync()
    {
        if (!ConnectionForm.SupportsBluetoothServiceSelection)
        {
            return;
        }

        if (!_bluetoothPlatformService.IsSupported)
        {
            ConnectionForm.BluetoothStatusText = _bluetoothPlatformService.AvailabilityMessage;
            LatestResult = _bluetoothPlatformService.AvailabilityMessage;
            StatusBrush = Brushes.IndianRed;
            AppendLog("刷新 BLE 特征", false, _bluetoothPlatformService.AvailabilityMessage, null);
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothDeviceId))
        {
            ApplyFailure("请先选择 BLE 设备。");
            return;
        }

        if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothServiceId))
        {
            ApplyFailure("当前设备尚未识别出 BLE 服务。");
            return;
        }

        ConnectionForm.IsBluetoothBusy = true;
        ConnectionForm.BluetoothStatusText = "正在加载 BLE 特征...";
        try
        {
            var result = await _bluetoothPlatformService.GetCharacteristicsAsync(ConnectionForm.BluetoothDeviceId, ConnectionForm.BluetoothServiceId, CancellationToken.None);
            if (result.IsSuccess && result.ResultValue != null)
            {
                ReplaceBluetoothCharacteristics(result.ResultValue);
                var count = result.ResultValue.Count;
                var hasRoute = !string.IsNullOrWhiteSpace(ConnectionForm.BluetoothWriteCharacteristicId)
                    && !string.IsNullOrWhiteSpace(ConnectionForm.BluetoothNotifyCharacteristicId);
                ConnectionForm.BluetoothStatusText = count > 0
                    ? hasRoute
                        ? $"已识别 {count} 个 BLE 特征，并自动选定收发通道。"
                        : $"已加载 {count} 个 BLE 特征，但尚未找到可用的收发通道。"
                    : "未发现 BLE 特征。";
                LatestResult = ConnectionForm.BluetoothStatusText;
                StatusBrush = hasRoute ? Brushes.SeaGreen : Brushes.DarkOrange;
            }
            else
            {
                ConnectionForm.BluetoothCharacteristics.Clear();
                ConnectionForm.SelectedBluetoothWriteCharacteristic = null;
                ConnectionForm.SelectedBluetoothNotifyCharacteristic = null;
                ConnectionForm.BluetoothStatusText = result.Message;
                LatestResult = result.Message;
                StatusBrush = Brushes.IndianRed;
            }

            AppendLog("刷新 BLE 特征", result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            ConnectionForm.IsBluetoothBusy = false;
        }
    }

    private void ReplaceBluetoothDevices(IReadOnlyList<BluetoothDeviceOption> devices)
    {
        var selected = devices.FirstOrDefault(device => device.DeviceId == ConnectionForm.BluetoothDeviceId);

        ConnectionForm.BluetoothDevices.Clear();
        foreach (var device in devices)
        {
            ConnectionForm.BluetoothDevices.Add(device);
        }

        ConnectionForm.SelectedBluetoothDevice = selected;
        if (selected == null && devices.Count == 0)
        {
            ConnectionForm.BluetoothDeviceName = string.Empty;
        }

        ConnectionForm.BluetoothServices.Clear();
        ConnectionForm.BluetoothCharacteristics.Clear();
    }

    private void ReplaceBluetoothServices(IReadOnlyList<BluetoothServiceOption> services)
    {
        var selected = services.FirstOrDefault(service => service.ServiceId == ConnectionForm.BluetoothServiceId);

        ConnectionForm.BluetoothServices.Clear();
        foreach (var service in services)
        {
            ConnectionForm.BluetoothServices.Add(service);
        }

        ConnectionForm.SelectedBluetoothService = selected;
        ConnectionForm.BluetoothCharacteristics.Clear();
    }

    private void ReplaceBluetoothCharacteristics(IReadOnlyList<BluetoothCharacteristicOption> characteristics)
    {
        var selectedWrite = characteristics.FirstOrDefault(characteristic => characteristic.CharacteristicId == ConnectionForm.BluetoothWriteCharacteristicId);
        var selectedNotify = characteristics.FirstOrDefault(characteristic => characteristic.CharacteristicId == ConnectionForm.BluetoothNotifyCharacteristicId);

        ConnectionForm.BluetoothCharacteristics.Clear();
        foreach (var characteristic in characteristics)
        {
            ConnectionForm.BluetoothCharacteristics.Add(characteristic);
        }

        ConnectionForm.SelectedBluetoothWriteCharacteristic = selectedWrite;
        ConnectionForm.SelectedBluetoothNotifyCharacteristic = selectedNotify;

        if (selectedWrite == null)
        {
            ConnectionForm.SelectedBluetoothWriteCharacteristic = characteristics.FirstOrDefault(characteristic => characteristic.CanWrite);
        }

        if (selectedNotify == null)
        {
            ConnectionForm.SelectedBluetoothNotifyCharacteristic = characteristics.FirstOrDefault(characteristic => characteristic.CanNotify)
                ?? characteristics.FirstOrDefault(characteristic => characteristic.CanRead);
        }
    }

    private async Task<bool> TryAutoSelectBluetoothRouteAsync(IReadOnlyList<BluetoothServiceOption> services)
    {
        foreach (var service in services)
        {
            var characteristicResult = await _bluetoothPlatformService.GetCharacteristicsAsync(ConnectionForm.BluetoothDeviceId, service.ServiceId, CancellationToken.None);
            if (!characteristicResult.IsSuccess || characteristicResult.ResultValue == null || characteristicResult.ResultValue.Count == 0)
            {
                continue;
            }

            ReplaceBluetoothCharacteristics(characteristicResult.ResultValue);
            var write = ConnectionForm.SelectedBluetoothWriteCharacteristic;
            var notify = ConnectionForm.SelectedBluetoothNotifyCharacteristic;
            if (write != null && notify != null)
            {
                ConnectionForm.SelectedBluetoothService = service;
                ReplaceBluetoothCharacteristics(characteristicResult.ResultValue);
                return true;
            }
        }

        if (services.Count > 0)
        {
            var firstService = services[0];
            ConnectionForm.SelectedBluetoothService = firstService;
            var fallbackResult = await _bluetoothPlatformService.GetCharacteristicsAsync(ConnectionForm.BluetoothDeviceId, firstService.ServiceId, CancellationToken.None);
            if (fallbackResult.IsSuccess && fallbackResult.ResultValue != null)
            {
                ReplaceBluetoothCharacteristics(fallbackResult.ResultValue);
            }
        }

        return false;
    }
}
