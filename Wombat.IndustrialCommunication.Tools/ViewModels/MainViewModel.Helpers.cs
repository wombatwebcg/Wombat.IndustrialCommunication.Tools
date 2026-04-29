using System.Collections.ObjectModel;
using Wombat.IndustrialCommunication;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class MainViewModel
{
    private string? ValidateSessionInput()
    {
        if (SelectedDefinition == null)
        {
            return "请先选择组件。";
        }

        if (ConnectionForm.IsSerial && string.IsNullOrWhiteSpace(ConnectionForm.PortName))
        {
            return "串口名称不能为空。";
        }

        if (ConnectionForm.IsBluetoothClient)
        {
            if (!_bluetoothPlatformService.IsSupported)
            {
                return _bluetoothPlatformService.AvailabilityMessage;
            }

            if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothDeviceId))
            {
                return "必须选择或填写 BLE 设备。";
            }

            if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothServiceId))
            {
                return "当前设备还没有自动识别出 BLE 服务。";
            }

            if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothWriteCharacteristicId))
            {
                return "当前设备还没有自动识别出 BLE 写入特征。";
            }

            if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothNotifyCharacteristicId))
            {
                return "当前设备还没有自动识别出 BLE 通知/读取特征。";
            }

            if (ConnectionForm.ConnectTimeoutSeconds <= 0 || ConnectionForm.ReceiveTimeoutSeconds <= 0 || ConnectionForm.SendTimeoutSeconds <= 0)
            {
                return "BLE 连接、接收和发送超时都必须大于 0。";
            }
        }

        if (ConnectionForm.IsBluetoothServer)
        {
            if (!_bluetoothPlatformService.IsSupported)
            {
                return _bluetoothPlatformService.AvailabilityMessage;
            }

            if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothServiceId))
            {
                return "本机 BLE 服务 UUID 不能为空。";
            }

            if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothWriteCharacteristicId))
            {
                return "本机 BLE 写入特征 UUID 不能为空。";
            }

            if (string.IsNullOrWhiteSpace(ConnectionForm.BluetoothNotifyCharacteristicId))
            {
                return "本机 BLE 通知特征 UUID 不能为空。";
            }

            if (ConnectionForm.ConnectTimeoutSeconds <= 0 || ConnectionForm.ReceiveTimeoutSeconds <= 0 || ConnectionForm.SendTimeoutSeconds <= 0)
            {
                return "BLE 连接、接收和发送超时都必须大于 0。";
            }
        }

        if (ConnectionForm.IsNetwork && string.IsNullOrWhiteSpace(ConnectionForm.Ip))
        {
            return "IP 不能为空。";
        }

        if (ConnectionForm.Port < 0 || ConnectionForm.MaxReconnectAttempts < 0 || ConnectionForm.ReconnectDelaySeconds < 0 || ConnectionForm.MaxConnections < 0)
        {
            return "端口、重连次数、重连延迟和最大连接数都不能为负数。";
        }

        return null;
    }

    private void AppendLog(string action, bool isSuccess, string message, double? durationMilliseconds)
    {
        var component = SelectedDefinition?.DisplayName ?? "未选择组件";
        Logs.Insert(0, _operationLogService.Create(component, action, isSuccess, message, durationMilliseconds));
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private void RecordClientPackets(OperationResult result, string requestMeaning, string responseMeaning)
    {
        if (!ShowClientPacketTracePanel)
        {
            return;
        }

        foreach (var request in result.Requsts)
        {
            AppendPacket(ClientSentPackets, requestMeaning, request);
        }

        foreach (var response in result.Responses)
        {
            AppendPacket(ClientReceivedPackets, responseMeaning, response);
        }
    }

    private static void AppendPacket(ObservableCollection<PacketTraceItemViewModel> collection, string meaning, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        collection.Insert(0, new PacketTraceItemViewModel(meaning, payload));
        while (collection.Count > 200)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private void ApplyFailure(string message)
    {
        StatusBrush = Avalonia.Media.Brushes.IndianRed;
        StatusSummary = message;
        LatestResult = message;
    }
}
