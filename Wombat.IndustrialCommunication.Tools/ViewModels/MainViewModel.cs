using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Tools.Models;
using Wombat.IndustrialCommunication.Tools.Services;
using Wombat.IndustrialCommunication.Tools.Services.Platform;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ComponentCatalogService _catalogService = new();
    private readonly DeviceSessionService _sessionService;
    private readonly AddressOperationService _addressOperationService = new();
    private readonly ModbusServerMemoryService _modbusMemoryService = new();
    private readonly OperationLogService _operationLogService = new();
    private readonly IBluetoothPlatformService _bluetoothPlatformService;
    private CancellationTokenSource? _addressAutoReadCts;
    private CancellationTokenSource? _addressAutoWriteCts;
    private CancellationTokenSource? _memoryAutoReadCts;
    private CancellationTokenSource? _memoryAutoWriteCts;

    public MainViewModel()
        : this(new DeviceSessionService(new UnsupportedBluetoothPlatformService()), new UnsupportedBluetoothPlatformService())
    {
    }

    public MainViewModel(DeviceSessionService sessionService, IBluetoothPlatformService bluetoothPlatformService)
    {
        _sessionService = sessionService;
        _bluetoothPlatformService = bluetoothPlatformService;
        ConnectionForm = new ConnectionFormViewModel();
        ConnectionForm.PropertyChanged += OnConnectionFormPropertyChanged;
        AddressWorkbench = new AddressWorkbenchViewModel();
        ModbusMemoryEditor = new ModbusMemoryEditorViewModel();
        Components = new ObservableCollection<CommunicationComponentItemViewModel>(_catalogService.GetComponents().Select(definition => new CommunicationComponentItemViewModel(definition)));
        SelectedComponent = Components.FirstOrDefault();
    }

    public ObservableCollection<CommunicationComponentItemViewModel> Components { get; }
    public ObservableCollection<OperationLogItemViewModel> Logs { get; } = [];
    public ObservableCollection<PacketTraceItemViewModel> ClientSentPackets { get; } = [];
    public ObservableCollection<PacketTraceItemViewModel> ClientReceivedPackets { get; } = [];
    public ObservableCollection<PacketTraceItemViewModel> ServerSentPackets { get; } = [];
    public ObservableCollection<PacketTraceItemViewModel> ServerReceivedPackets { get; } = [];
    public ConnectionFormViewModel ConnectionForm { get; }
    public AddressWorkbenchViewModel AddressWorkbench { get; }
    public ModbusMemoryEditorViewModel ModbusMemoryEditor { get; }

    [ObservableProperty] private CommunicationComponentItemViewModel? _selectedComponent;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isSessionActive;
    [ObservableProperty] private string _statusSummary = "请选择左侧组件，然后填写连接参数。";
    [ObservableProperty] private string _latestResult = "暂无操作结果。";
    [ObservableProperty] private string _sessionStateText = "未启动";
    [ObservableProperty] private string _currentEndpoint = "未连接";
    [ObservableProperty] private IBrush _statusBrush = Brushes.SlateGray;

    public CommunicationComponentDefinition? SelectedDefinition => SelectedComponent?.Definition;
    public IEnumerable<string> SelectedCapabilityBadges => SelectedDefinition?.CapabilityBadges ?? [];
    public IEnumerable<string> SelectedExampleAddresses => SelectedDefinition?.ExampleAddresses ?? [];
    public IEnumerable<string> SelectedQuickTips => SelectedDefinition?.QuickTips ?? [];
    public string SelectedSummary => SelectedDefinition?.Summary ?? "请选择组件。";
    public string SelectedCapabilitySummary => SelectedDefinition?.CapabilitySummary ?? "请选择组件查看能力说明。";
    public string StartButtonText => SelectedDefinition?.StartActionText ?? "开始";
    public string StopButtonText => SelectedDefinition?.StopActionText ?? "停止";
    public bool ShowAddressWorkbench => SelectedDefinition?.SupportsAddressWorkbench ?? false;
    public bool ShowMemoryEditor => SelectedDefinition?.SupportsMemoryEditor ?? false;
    public bool CanStartSession => !IsBusy && !IsSessionActive && SelectedDefinition != null;
    public bool CanStopSession => !IsBusy && IsSessionActive;
    public bool CanUseAddressWorkbench => !IsBusy && IsSessionActive && ShowAddressWorkbench && _sessionService.SupportsAddressWorkbench;
    public bool CanUseMemoryEditor => !IsBusy && IsSessionActive && ShowMemoryEditor && _sessionService.SupportsMemoryEditor;
    public bool CanCreateDefaultDb => !IsBusy && IsSessionActive && _sessionService.SupportsDefaultDbCreation;
    public string WorkspaceStateMessage => IsSessionActive ? "会话已建立，可以开始操作。" : "会话未建立，当前面板仅展示示例与说明。";
    public bool ShowClientPacketTracePanel => SelectedDefinition?.IsServer != true;
    public bool ShowServerPacketTracePanel => SelectedDefinition?.IsServer == true;
    public string PacketTraceSectionTitle => ShowServerPacketTracePanel ? "服务端报文" : "客户端报文";
    public string PacketTraceSectionDescription => ShowServerPacketTracePanel
        ? "展示服务端设备发送和接收的原始报文，包含时间戳与报文意义。"
        : "展示客户端设备发送和接收的原始报文，包含时间戳与报文意义。";

    partial void OnSelectedComponentChanged(CommunicationComponentItemViewModel? value)
    {
        StopAllAutoOperations();
        if (_sessionService.HasActiveSession)
        {
            var stop = _sessionService.StopSession();
            AppendLog("自动释放上一个会话", stop.IsSuccess, stop.Message, stop.TimeConsuming);
        }

        if (value == null)
        {
            return;
        }

        ConnectionForm.ConfigureFor(value.Definition.Kind);
        AddressWorkbench.ConfigureFor(value.Definition);
        ModbusMemoryEditor.ConfigureFor(value.Definition);
        IsSessionActive = false;
        SessionStateText = value.Definition.IsServer ? "未监听" : "未连接";
        CurrentEndpoint = "未连接";
        StatusBrush = Brushes.SlateGray;
        StatusSummary = $"已切换到 {value.DisplayName}，请先完成参数配置。";
        LatestResult = value.Summary;
        ClearPacketTraces();
        if (ConnectionForm.IsBluetoothClient)
        {
            ConnectionForm.BluetoothStatusText = _bluetoothPlatformService.IsSupported
                ? "正在准备 BLE 设备列表。"
                : _bluetoothPlatformService.AvailabilityMessage;
            _ = RefreshBluetoothDevicesAsync();
        }
        NotifySelectionProperties();
    }

    partial void OnIsBusyChanged(bool value) => NotifyStateProperties();
    partial void OnIsSessionActiveChanged(bool value) => NotifyStateProperties();

    public void Dispose()
    {
        StopAllAutoOperations();
        ConnectionForm.PropertyChanged -= OnConnectionFormPropertyChanged;
        _sessionService.Dispose();
    }

    private void OnConnectionFormPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ConnectionForm.IsBluetoothClient)
        {
            return;
        }

        if (e.PropertyName == nameof(ConnectionFormViewModel.SelectedBluetoothDevice) &&
            ConnectionForm.SelectedBluetoothDevice != null &&
            !ConnectionForm.IsBluetoothBusy)
        {
            _ = RefreshBluetoothServicesAsync();
        }
    }

    private void HandleBackgroundEvent(string action, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LatestResult = message;
            AppendLog(action, true, message, null);
        });
    }

    private void HandleServerPacketTrace(PacketTraceEventArgs trace)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var collection = trace.Direction == PacketTraceDirection.Sent ? ServerSentPackets : ServerReceivedPackets;
            AppendPacket(collection, trace.Meaning, trace.HexText);
        });
    }


    private void NotifySelectionProperties()
    {
        OnPropertyChanged(nameof(SelectedDefinition));
        OnPropertyChanged(nameof(SelectedCapabilityBadges));
        OnPropertyChanged(nameof(SelectedExampleAddresses));
        OnPropertyChanged(nameof(SelectedQuickTips));
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(SelectedCapabilitySummary));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StopButtonText));
        OnPropertyChanged(nameof(ShowAddressWorkbench));
        OnPropertyChanged(nameof(ShowMemoryEditor));
        OnPropertyChanged(nameof(ShowClientPacketTracePanel));
        OnPropertyChanged(nameof(ShowServerPacketTracePanel));
        OnPropertyChanged(nameof(PacketTraceSectionTitle));
        OnPropertyChanged(nameof(PacketTraceSectionDescription));
        NotifyStateProperties();
    }

    private void NotifyStateProperties()
    {
        OnPropertyChanged(nameof(CanStartSession));
        OnPropertyChanged(nameof(CanStopSession));
        OnPropertyChanged(nameof(CanUseAddressWorkbench));
        OnPropertyChanged(nameof(CanUseMemoryEditor));
        OnPropertyChanged(nameof(CanCreateDefaultDb));
        OnPropertyChanged(nameof(WorkspaceStateMessage));
    }
}
