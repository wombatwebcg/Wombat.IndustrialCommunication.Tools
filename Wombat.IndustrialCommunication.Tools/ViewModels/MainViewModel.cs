using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wombat.IndustrialCommunication.Tools.Models;
using Wombat.IndustrialCommunication.Tools.Services;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ComponentCatalogService _catalogService = new();
    private readonly DeviceSessionService _sessionService = new();
    private readonly AddressOperationService _addressOperationService = new();
    private readonly ModbusServerMemoryService _modbusMemoryService = new();
    private readonly OperationLogService _operationLogService = new();

    public MainViewModel()
    {
        ConnectionForm = new ConnectionFormViewModel();
        AddressWorkbench = new AddressWorkbenchViewModel();
        ModbusMemoryEditor = new ModbusMemoryEditorViewModel();
        Components = new ObservableCollection<CommunicationComponentItemViewModel>(_catalogService.GetComponents().Select(definition => new CommunicationComponentItemViewModel(definition)));
        SelectedComponent = Components.FirstOrDefault();
    }

    public ObservableCollection<CommunicationComponentItemViewModel> Components { get; }
    public ObservableCollection<OperationLogItemViewModel> Logs { get; } = [];
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

    partial void OnSelectedComponentChanged(CommunicationComponentItemViewModel? value)
    {
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
        NotifySelectionProperties();
    }

    partial void OnIsBusyChanged(bool value) => NotifyStateProperties();
    partial void OnIsSessionActiveChanged(bool value) => NotifyStateProperties();

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (SelectedDefinition == null)
        {
            return;
        }

        var validation = ValidateSessionInput();
        if (validation != null)
        {
            ApplyFailure(validation);
            AppendLog("启动前校验", false, validation, null);
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _sessionService.StartSession(SelectedDefinition, ConnectionForm, HandleBackgroundEvent));
            if (result.IsSuccess)
            {
                IsSessionActive = true;
                SessionStateText = SelectedDefinition.IsServer ? "监听中" : "已连接";
                CurrentEndpoint = _sessionService.ActiveEndpoint;
                StatusBrush = Brushes.SeaGreen;
                StatusSummary = SelectedDefinition.IsServer ? "监听已启动，可使用下方工作区。" : "连接成功，可开始任意地址读写。";
                LatestResult = result.Message;
            }
            else
            {
                ApplyFailure(result.Message);
            }

            AppendLog(StartButtonText, result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (!IsSessionActive)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(_sessionService.StopSession);
            IsSessionActive = false;
            SessionStateText = SelectedDefinition?.IsServer == true ? "未监听" : "未连接";
            CurrentEndpoint = "未连接";
            StatusBrush = result.IsSuccess ? Brushes.SlateGray : Brushes.IndianRed;
            StatusSummary = result.IsSuccess ? "会话已停止。" : result.Message;
            LatestResult = result.Message;
            AppendLog(StopButtonText, result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReadAddressAsync()
    {
        if (!CanUseAddressWorkbench || _sessionService.CurrentAddressAccessor == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AddressWorkbench.Address) || AddressWorkbench.Length <= 0)
        {
            ApplyFailure("地址不能为空，长度至少为 1。");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _addressOperationService.Read(_sessionService.CurrentAddressAccessor, AddressWorkbench.SelectedDataType, AddressWorkbench.Address, AddressWorkbench.Length));
            AddressWorkbench.ReadResultText = result.IsSuccess ? result.ResultValue ?? string.Empty : result.Message;
            AddressWorkbench.LastDurationText = $"最近耗时: {(result.TimeConsuming.HasValue ? result.TimeConsuming.Value.ToString("0.##") : "-")} ms";
            LatestResult = result.IsSuccess ? $"读取成功: {AddressWorkbench.ReadResultText}" : result.Message;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            AppendLog("读取地址", result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task WriteAddressAsync()
    {
        if (!CanUseAddressWorkbench || _sessionService.CurrentAddressAccessor == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AddressWorkbench.Address) || string.IsNullOrWhiteSpace(AddressWorkbench.WriteValue))
        {
            ApplyFailure("地址和值都不能为空。");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _addressOperationService.Write(_sessionService.CurrentAddressAccessor, AddressWorkbench.SelectedDataType, AddressWorkbench.Address, AddressWorkbench.WriteValue));
            AddressWorkbench.WriteFeedbackText = result.IsSuccess ? $"写入成功: {result.ResultValue}" : result.Message;
            AddressWorkbench.LastDurationText = $"最近耗时: {(result.TimeConsuming.HasValue ? result.TimeConsuming.Value.ToString("0.##") : "-")} ms";
            LatestResult = AddressWorkbench.WriteFeedbackText;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            AppendLog("写入地址", result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReadMemoryAsync()
    {
        if (!CanUseMemoryEditor || _sessionService.CurrentDataStore == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _modbusMemoryService.Read(_sessionService.CurrentDataStore, ModbusMemoryEditor.SelectedArea, ModbusMemoryEditor.Offset, ModbusMemoryEditor.Length, ModbusMemoryEditor.SelectedDataType));
            ModbusMemoryEditor.SnapshotText = result.IsSuccess ? result.ResultValue ?? string.Empty : result.Message;
            LatestResult = result.Message;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            AppendLog("读取 DataStore", result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task WriteMemoryAsync()
    {
        if (!CanUseMemoryEditor || _sessionService.CurrentDataStore == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _modbusMemoryService.Write(_sessionService.CurrentDataStore, ModbusMemoryEditor.SelectedArea, ModbusMemoryEditor.Offset, ModbusMemoryEditor.SelectedDataType, ModbusMemoryEditor.WriteValue));
            ModbusMemoryEditor.SnapshotText = result.IsSuccess ? result.ResultValue ?? string.Empty : result.Message;
            LatestResult = result.Message;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            AppendLog("写入 DataStore", result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateDefaultDbAsync()
    {
        if (!CanCreateDefaultDb)
        {
            return;
        }

        if (ConnectionForm.DefaultDbNumber <= 0 || ConnectionForm.DefaultDbSize <= 0)
        {
            ApplyFailure("默认 DB 编号和大小必须大于 0。");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _sessionService.CreateDefaultDataBlock(ConnectionForm.DefaultDbNumber, ConnectionForm.DefaultDbSize));
            LatestResult = result.Message;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            AppendLog("创建默认 DB", result.IsSuccess, result.Message, result.TimeConsuming);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearLogs() => Logs.Clear();

    public void Dispose()
    {
        _sessionService.Dispose();
    }

    private void HandleBackgroundEvent(string action, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LatestResult = message;
            AppendLog(action, true, message, null);
        });
    }

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

    private void ApplyFailure(string message)
    {
        StatusBrush = Brushes.IndianRed;
        StatusSummary = message;
        LatestResult = message;
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
