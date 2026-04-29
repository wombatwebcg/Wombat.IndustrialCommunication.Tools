using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Wombat.IndustrialCommunication.PLC;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class ConnectionFormViewModel : ViewModelBase
{
    public const string DefaultBluetoothServiceId = "";
    public const string DefaultBluetoothCharacteristicId = "";
    public const string DefaultBluetoothServerServiceId = "0000FF00-0000-1000-8000-00805F9B34FB";
    public const string DefaultBluetoothServerWriteCharacteristicId = "0000FF01-0000-1000-8000-00805F9B34FB";
    public const string DefaultBluetoothServerNotifyCharacteristicId = "0000FF02-0000-1000-8000-00805F9B34FB";

    public ConnectionFormViewModel()
    {
        ConfigureFor(CommunicationComponentKind.ModbusRtuClient);
    }

    public string[] StopBitsOptions { get; } = Enum.GetNames<StopBits>();
    public string[] ParityOptions { get; } = Enum.GetNames<Parity>();
    public string[] HandshakeOptions { get; } = Enum.GetNames<Handshake>();
    public SiemensVersion[] SiemensVersions { get; } = Enum.GetValues<SiemensVersion>().Where(value => value != SiemensVersion.None).ToArray();
    public ObservableCollection<BluetoothDeviceOption> BluetoothDevices { get; } = [];
    public ObservableCollection<BluetoothServiceOption> BluetoothServices { get; } = [];
    public ObservableCollection<BluetoothCharacteristicOption> BluetoothCharacteristics { get; } = [];

    [ObservableProperty] private CommunicationComponentKind _currentKind;
    [ObservableProperty] private string _portName = "COM1";
    [ObservableProperty] private int _baudRate = 9600;
    [ObservableProperty] private int _dataBits = 8;
    [ObservableProperty] private string _selectedStopBits = nameof(StopBits.One);
    [ObservableProperty] private string _selectedParity = nameof(Parity.None);
    [ObservableProperty] private string _selectedHandshake = nameof(Handshake.None);
    [ObservableProperty] private string _ip = "127.0.0.1";
    [ObservableProperty] private int _port = 502;
    [ObservableProperty] private bool _isLongConnection = true;
    [ObservableProperty] private bool _enableAutoReconnect = true;
    [ObservableProperty] private int _maxReconnectAttempts = 5;
    [ObservableProperty] private int _reconnectDelaySeconds = 2;
    [ObservableProperty] private SiemensVersion _selectedSiemensVersion = SiemensVersion.S7_1200;
    [ObservableProperty] private int _rack;
    [ObservableProperty] private int _slot;
    [ObservableProperty] private int _maxConnections = 10;
    [ObservableProperty] private int _defaultDbNumber = 1;
    [ObservableProperty] private int _defaultDbSize = 256;
    [ObservableProperty] private string _bluetoothDeviceId = string.Empty;
    [ObservableProperty] private string _bluetoothDeviceName = string.Empty;
    [ObservableProperty] private string _bluetoothServiceId = DefaultBluetoothServiceId;
    [ObservableProperty] private string _bluetoothWriteCharacteristicId = DefaultBluetoothCharacteristicId;
    [ObservableProperty] private string _bluetoothNotifyCharacteristicId = DefaultBluetoothCharacteristicId;
    [ObservableProperty] private BluetoothDeviceOption? _selectedBluetoothDevice;
    [ObservableProperty] private BluetoothServiceOption? _selectedBluetoothService;
    [ObservableProperty] private BluetoothCharacteristicOption? _selectedBluetoothWriteCharacteristic;
    [ObservableProperty] private BluetoothCharacteristicOption? _selectedBluetoothNotifyCharacteristic;
    [ObservableProperty] private int _connectTimeoutSeconds = 5;
    [ObservableProperty] private int _receiveTimeoutSeconds = 3;
    [ObservableProperty] private int _sendTimeoutSeconds = 3;
    [ObservableProperty] private string _bluetoothStatusText = "蓝牙设备未加载。";
    [ObservableProperty] private bool _isBluetoothBusy;

    public bool IsClient => CurrentKind is CommunicationComponentKind.ModbusRtuClient or CommunicationComponentKind.ModbusRtuBluetoothClient or CommunicationComponentKind.ModbusTcpClient or CommunicationComponentKind.SiemensClient;
    public bool IsServer => !IsClient;
    public bool IsSerial => CurrentKind is CommunicationComponentKind.ModbusRtuClient or CommunicationComponentKind.ModbusRtuServer;
    public bool IsBluetoothClient => CurrentKind == CommunicationComponentKind.ModbusRtuBluetoothClient;
    public bool IsBluetoothServer => CurrentKind == CommunicationComponentKind.ModbusRtuBluetoothServer;
    public bool IsBluetooth => IsBluetoothClient || IsBluetoothServer;
    public bool IsNetwork => CurrentKind is CommunicationComponentKind.ModbusTcpClient or CommunicationComponentKind.SiemensClient or CommunicationComponentKind.ModbusTcpServer or CommunicationComponentKind.S7TcpServer;
    public bool IsSiemens => CurrentKind is CommunicationComponentKind.SiemensClient or CommunicationComponentKind.S7TcpServer;
    public bool SupportsAutoReconnect => IsClient;
    public bool SupportsMaxConnections => CurrentKind is CommunicationComponentKind.ModbusTcpServer or CommunicationComponentKind.S7TcpServer;
    public bool SupportsDefaultDb => CurrentKind == CommunicationComponentKind.S7TcpServer;
    public bool SupportsBluetoothDiscovery => IsBluetoothClient;
    public bool SupportsBluetoothServiceSelection => IsBluetoothClient;
    public string StartActionText => IsServer ? "开始监听" : "连接设备";
    public string StopActionText => IsServer ? "停止监听" : "断开连接";
    public string BluetoothResolvedServiceText => string.IsNullOrWhiteSpace(BluetoothServiceId) ? "未识别" : BluetoothServiceId;
    public string BluetoothResolvedWriteCharacteristicText => string.IsNullOrWhiteSpace(BluetoothWriteCharacteristicId) ? "未识别" : BluetoothWriteCharacteristicId;
    public string BluetoothResolvedNotifyCharacteristicText => string.IsNullOrWhiteSpace(BluetoothNotifyCharacteristicId) ? "未识别" : BluetoothNotifyCharacteristicId;

    public string ParameterSummary => CurrentKind switch
    {
        CommunicationComponentKind.ModbusRtuClient => "串口主站参数 + 自动重连 + 统一地址读写。",
        CommunicationComponentKind.ModbusRtuBluetoothClient => "BLE 设备/服务/特征参数 + 自动重连 + 统一地址读写。",
        CommunicationComponentKind.ModbusRtuBluetoothServer => "BLE 设备/服务/特征参数，启动后改用 DataStore 内存编辑器。",
        CommunicationComponentKind.ModbusTcpClient => "网络主站参数 + 自动重连 + 统一地址读写。",
        CommunicationComponentKind.SiemensClient => "PLC 机型、Rack/Slot、长连接与统一地址读写。",
        CommunicationComponentKind.ModbusRtuServer => "串口监听参数，启动后改用 DataStore 内存编辑器。",
        CommunicationComponentKind.ModbusTcpServer => "网络监听参数，启动后改用 DataStore 内存编辑器。",
        CommunicationComponentKind.S7TcpServer => "网络监听参数 + SiemensVersion/Rack/Slot + 默认 DB。",
        _ => string.Empty
    };

    public void ConfigureFor(CommunicationComponentKind kind)
    {
        CurrentKind = kind;
        switch (kind)
        {
            case CommunicationComponentKind.ModbusRtuClient:
                PortName = "COM1";
                BaudRate = 9600;
                DataBits = 8;
                SelectedStopBits = nameof(StopBits.One);
                SelectedParity = nameof(Parity.None);
                SelectedHandshake = nameof(Handshake.None);
                IsLongConnection = true;
                EnableAutoReconnect = true;
                MaxReconnectAttempts = 5;
                ReconnectDelaySeconds = 2;
                break;
            case CommunicationComponentKind.ModbusTcpClient:
                Ip = "127.0.0.1";
                Port = 502;
                IsLongConnection = true;
                EnableAutoReconnect = true;
                MaxReconnectAttempts = 5;
                ReconnectDelaySeconds = 2;
                break;
            case CommunicationComponentKind.ModbusRtuBluetoothClient:
                IsLongConnection = true;
                EnableAutoReconnect = true;
                MaxReconnectAttempts = 5;
                ReconnectDelaySeconds = 2;
                BluetoothDeviceId = string.Empty;
                BluetoothDeviceName = string.Empty;
                BluetoothServiceId = DefaultBluetoothServiceId;
                BluetoothWriteCharacteristicId = DefaultBluetoothCharacteristicId;
                BluetoothNotifyCharacteristicId = DefaultBluetoothCharacteristicId;
                SelectedBluetoothDevice = null;
                SelectedBluetoothService = null;
                SelectedBluetoothWriteCharacteristic = null;
                SelectedBluetoothNotifyCharacteristic = null;
                ConnectTimeoutSeconds = 5;
                ReceiveTimeoutSeconds = 3;
                SendTimeoutSeconds = 3;
                BluetoothStatusText = "请选择 BLE 设备，系统会自动识别服务与读写特征。";
                IsBluetoothBusy = false;
                BluetoothDevices.Clear();
                BluetoothServices.Clear();
                BluetoothCharacteristics.Clear();
                break;
            case CommunicationComponentKind.ModbusRtuBluetoothServer:
                IsLongConnection = false;
                EnableAutoReconnect = false;
                MaxReconnectAttempts = 0;
                ReconnectDelaySeconds = 0;
                BluetoothDeviceId = string.Empty;
                BluetoothDeviceName = Environment.MachineName;
                BluetoothServiceId = DefaultBluetoothServerServiceId;
                BluetoothWriteCharacteristicId = DefaultBluetoothServerWriteCharacteristicId;
                BluetoothNotifyCharacteristicId = DefaultBluetoothServerNotifyCharacteristicId;
                SelectedBluetoothDevice = null;
                SelectedBluetoothService = null;
                SelectedBluetoothWriteCharacteristic = null;
                SelectedBluetoothNotifyCharacteristic = null;
                ConnectTimeoutSeconds = 5;
                ReceiveTimeoutSeconds = 3;
                SendTimeoutSeconds = 3;
                BluetoothStatusText = "将使用本机蓝牙适配器发布 BLE 服务。";
                IsBluetoothBusy = false;
                BluetoothDevices.Clear();
                BluetoothServices.Clear();
                BluetoothCharacteristics.Clear();
                break;
            case CommunicationComponentKind.SiemensClient:
                Ip = "127.0.0.1";
                Port = 102;
                SelectedSiemensVersion = SiemensVersion.S7_1200;
                Rack = 0;
                Slot = 0;
                IsLongConnection = true;
                EnableAutoReconnect = true;
                MaxReconnectAttempts = 3;
                ReconnectDelaySeconds = 5;
                break;
            case CommunicationComponentKind.ModbusRtuServer:
                PortName = "COM1";
                BaudRate = 9600;
                DataBits = 8;
                SelectedStopBits = nameof(StopBits.One);
                SelectedParity = nameof(Parity.None);
                SelectedHandshake = nameof(Handshake.None);
                break;
            case CommunicationComponentKind.ModbusTcpServer:
                Ip = "0.0.0.0";
                Port = 502;
                MaxConnections = 10;
                break;
            case CommunicationComponentKind.S7TcpServer:
                Ip = "0.0.0.0";
                Port = 102;
                MaxConnections = 10;
                SelectedSiemensVersion = SiemensVersion.S7_1200;
                Rack = 0;
                Slot = 0;
                DefaultDbNumber = 1;
                DefaultDbSize = 256;
                break;
        }
    }

    public StopBits ParseStopBits() => Enum.TryParse<StopBits>(SelectedStopBits, out var value) ? value : StopBits.One;
    public Parity ParseParity() => Enum.TryParse<Parity>(SelectedParity, out var value) ? value : Parity.None;
    public Handshake ParseHandshake() => Enum.TryParse<Handshake>(SelectedHandshake, out var value) ? value : Handshake.None;

    partial void OnCurrentKindChanged(CommunicationComponentKind value)
    {
        OnPropertyChanged(nameof(IsClient));
        OnPropertyChanged(nameof(IsServer));
        OnPropertyChanged(nameof(IsSerial));
        OnPropertyChanged(nameof(IsBluetoothClient));
        OnPropertyChanged(nameof(IsBluetoothServer));
        OnPropertyChanged(nameof(IsBluetooth));
        OnPropertyChanged(nameof(IsNetwork));
        OnPropertyChanged(nameof(IsSiemens));
        OnPropertyChanged(nameof(SupportsAutoReconnect));
        OnPropertyChanged(nameof(SupportsMaxConnections));
        OnPropertyChanged(nameof(SupportsDefaultDb));
        OnPropertyChanged(nameof(SupportsBluetoothDiscovery));
        OnPropertyChanged(nameof(SupportsBluetoothServiceSelection));
        OnPropertyChanged(nameof(StartActionText));
        OnPropertyChanged(nameof(StopActionText));
        OnPropertyChanged(nameof(ParameterSummary));
    }

    partial void OnSelectedBluetoothDeviceChanged(BluetoothDeviceOption? value)
    {
        if (value == null)
        {
            return;
        }

        BluetoothDeviceId = value.DeviceId;
        BluetoothDeviceName = value.DisplayName;
        BluetoothServices.Clear();
        BluetoothCharacteristics.Clear();
        SelectedBluetoothService = null;
        SelectedBluetoothWriteCharacteristic = null;
        SelectedBluetoothNotifyCharacteristic = null;
        BluetoothServiceId = DefaultBluetoothServiceId;
        BluetoothWriteCharacteristicId = DefaultBluetoothCharacteristicId;
        BluetoothNotifyCharacteristicId = DefaultBluetoothCharacteristicId;
    }

    partial void OnSelectedBluetoothServiceChanged(BluetoothServiceOption? value)
    {
        if (value == null)
        {
            return;
        }

        BluetoothServiceId = value.ServiceId;
        BluetoothCharacteristics.Clear();
        SelectedBluetoothWriteCharacteristic = null;
        SelectedBluetoothNotifyCharacteristic = null;
        BluetoothWriteCharacteristicId = DefaultBluetoothCharacteristicId;
        BluetoothNotifyCharacteristicId = DefaultBluetoothCharacteristicId;
    }

    partial void OnBluetoothServiceIdChanged(string value)
    {
        OnPropertyChanged(nameof(BluetoothResolvedServiceText));
    }

    partial void OnBluetoothWriteCharacteristicIdChanged(string value)
    {
        OnPropertyChanged(nameof(BluetoothResolvedWriteCharacteristicText));
    }

    partial void OnBluetoothNotifyCharacteristicIdChanged(string value)
    {
        OnPropertyChanged(nameof(BluetoothResolvedNotifyCharacteristicText));
    }

    partial void OnSelectedBluetoothWriteCharacteristicChanged(BluetoothCharacteristicOption? value)
    {
        if (value == null)
        {
            return;
        }

        BluetoothWriteCharacteristicId = value.CharacteristicId;
    }

    partial void OnSelectedBluetoothNotifyCharacteristicChanged(BluetoothCharacteristicOption? value)
    {
        if (value == null)
        {
            return;
        }

        BluetoothNotifyCharacteristicId = value.CharacteristicId;
    }
}
