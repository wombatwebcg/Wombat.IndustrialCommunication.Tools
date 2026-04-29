using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task ReadAddressAsync() => _ = await ExecuteAddressReadOnceAsync();

    [RelayCommand]
    private async Task WriteAddressAsync() => _ = await ExecuteAddressWriteOnceAsync();

    [RelayCommand]
    private async Task ReadMemoryAsync() => _ = await ExecuteMemoryReadOnceAsync();

    [RelayCommand]
    private async Task WriteMemoryAsync() => _ = await ExecuteMemoryWriteOnceAsync();

    [RelayCommand]
    private void StartAddressAutoRead()
    {
        if (AddressWorkbench.IsAutoReadRunning)
        {
            return;
        }

        if (!CanUseAddressWorkbench || _sessionService.CurrentAddressAccessor == null)
        {
            ApplyFailure("当前会话不支持地址工作区定时读取。");
            return;
        }

        var cts = new CancellationTokenSource();
        _addressAutoReadCts = cts;
        AddressWorkbench.IsAutoReadRunning = true;
        AddressWorkbench.AutoReadStatusText = $"定时读取运行中，周期 {Math.Max(1, AddressWorkbench.AutoReadIntervalMs)} ms。";
        _ = RunAddressAutoReadLoopAsync(cts);
    }

    [RelayCommand]
    private void StopAddressAutoRead() => StopAddressAutoReadInternal();

    [RelayCommand]
    private void StartAddressAutoWrite()
    {
        if (AddressWorkbench.IsAutoWriteRunning)
        {
            return;
        }

        if (!CanUseAddressWorkbench || _sessionService.CurrentAddressAccessor == null)
        {
            ApplyFailure("当前会话不支持地址工作区定时写入。");
            return;
        }

        var cts = new CancellationTokenSource();
        _addressAutoWriteCts = cts;
        AddressWorkbench.IsAutoWriteRunning = true;
        AddressWorkbench.AutoWriteStatusText = $"定时写入运行中，周期 {Math.Max(1, AddressWorkbench.AutoWriteIntervalMs)} ms。";
        _ = RunAddressAutoWriteLoopAsync(cts);
    }

    [RelayCommand]
    private void StopAddressAutoWrite() => StopAddressAutoWriteInternal();

    [RelayCommand]
    private void StartMemoryAutoRead()
    {
        if (ModbusMemoryEditor.IsAutoReadRunning)
        {
            return;
        }

        if (!CanUseMemoryEditor || _sessionService.CurrentDataStore == null)
        {
            ApplyFailure("当前会话不支持 DataStore 定时读取。");
            return;
        }

        var cts = new CancellationTokenSource();
        _memoryAutoReadCts = cts;
        ModbusMemoryEditor.IsAutoReadRunning = true;
        ModbusMemoryEditor.AutoReadStatusText = $"定时读取运行中，周期 {Math.Max(1, ModbusMemoryEditor.AutoReadIntervalMs)} ms。";
        _ = RunMemoryAutoReadLoopAsync(cts);
    }

    [RelayCommand]
    private void StopMemoryAutoRead() => StopMemoryAutoReadInternal();

    [RelayCommand]
    private void StartMemoryAutoWrite()
    {
        if (ModbusMemoryEditor.IsAutoWriteRunning)
        {
            return;
        }

        if (!CanUseMemoryEditor || _sessionService.CurrentDataStore == null)
        {
            ApplyFailure("当前会话不支持 DataStore 定时写入。");
            return;
        }

        var cts = new CancellationTokenSource();
        _memoryAutoWriteCts = cts;
        ModbusMemoryEditor.IsAutoWriteRunning = true;
        ModbusMemoryEditor.AutoWriteStatusText = $"定时写入运行中，周期 {Math.Max(1, ModbusMemoryEditor.AutoWriteIntervalMs)} ms。";
        _ = RunMemoryAutoWriteLoopAsync(cts);
    }

    [RelayCommand]
    private void StopMemoryAutoWrite() => StopMemoryAutoWriteInternal();

    [RelayCommand]
    private void ClearLogs() => Logs.Clear();

    [RelayCommand]
    private void ClearPacketTraces()
    {
        ClientSentPackets.Clear();
        ClientReceivedPackets.Clear();
        ServerSentPackets.Clear();
        ServerReceivedPackets.Clear();
    }

    private async Task<bool> ExecuteAddressReadOnceAsync(bool fromTimer = false)
    {
        return await ExecuteAddressReadOnceAsync(fromTimer, null);
    }

    private async Task<bool> ExecuteAddressReadOnceAsync(bool fromTimer, string? packetMeaningPrefix)
    {
        if (!IsSessionActive || !ShowAddressWorkbench || !_sessionService.SupportsAddressWorkbench || _sessionService.CurrentAddressAccessor == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(AddressWorkbench.Address) || AddressWorkbench.Length <= 0)
        {
            ApplyFailure("地址不能为空，长度至少为 1。");
            return false;
        }

        if (IsBusy && fromTimer)
        {
            return true;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _addressOperationService.Read(_sessionService.CurrentAddressAccessor, AddressWorkbench.SelectedDataType, AddressWorkbench.Address, AddressWorkbench.Length));
            AddressWorkbench.ReadResultText = result.IsSuccess ? result.ResultValue ?? string.Empty : result.Message;
            AddressWorkbench.LastDurationText = $"最近耗时: {(result.TimeConsuming.HasValue ? result.TimeConsuming.Value.ToString("0.##") : "-")} ms";
            LatestResult = result.IsSuccess ? $"读取成功: {AddressWorkbench.ReadResultText}" : result.Message;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            var requestMeaning = packetMeaningPrefix is null ? $"读取地址请求: {AddressWorkbench.Address}" : $"{packetMeaningPrefix} | 读取请求";
            var responseMeaning = packetMeaningPrefix is null ? $"读取地址响应: {AddressWorkbench.Address}" : $"{packetMeaningPrefix} | 读取响应";
            RecordClientPackets(result, requestMeaning, responseMeaning);
            AppendLog(fromTimer ? "定时读取地址" : "读取地址", result.IsSuccess, result.Message, result.TimeConsuming);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ExecuteAddressWriteOnceAsync(bool fromTimer = false)
    {
        return await ExecuteAddressWriteOnceAsync(fromTimer, null);
    }

    private async Task<bool> ExecuteAddressWriteOnceAsync(bool fromTimer, string? packetMeaningPrefix)
    {
        if (!IsSessionActive || !ShowAddressWorkbench || !_sessionService.SupportsAddressWorkbench || _sessionService.CurrentAddressAccessor == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(AddressWorkbench.Address) || string.IsNullOrWhiteSpace(AddressWorkbench.WriteValue))
        {
            ApplyFailure("地址和值都不能为空。");
            return false;
        }

        if (IsBusy && fromTimer)
        {
            return true;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _addressOperationService.Write(_sessionService.CurrentAddressAccessor, AddressWorkbench.SelectedDataType, AddressWorkbench.Address, AddressWorkbench.WriteValue));
            AddressWorkbench.WriteFeedbackText = result.IsSuccess ? $"写入成功: {result.ResultValue}" : result.Message;
            AddressWorkbench.LastDurationText = $"最近耗时: {(result.TimeConsuming.HasValue ? result.TimeConsuming.Value.ToString("0.##") : "-")} ms";
            LatestResult = AddressWorkbench.WriteFeedbackText;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            var requestMeaning = packetMeaningPrefix is null ? $"写入地址请求: {AddressWorkbench.Address}" : $"{packetMeaningPrefix} | 写入请求";
            var responseMeaning = packetMeaningPrefix is null ? $"写入地址响应: {AddressWorkbench.Address}" : $"{packetMeaningPrefix} | 写入响应";
            RecordClientPackets(result, requestMeaning, responseMeaning);
            AppendLog(fromTimer ? "定时写入地址" : "写入地址", result.IsSuccess, result.Message, result.TimeConsuming);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ExecuteMemoryReadOnceAsync(bool fromTimer = false)
    {
        if (!IsSessionActive || !ShowMemoryEditor || !_sessionService.SupportsMemoryEditor || _sessionService.CurrentDataStore == null)
        {
            return false;
        }

        if (ModbusMemoryEditor.Length <= 0)
        {
            ApplyFailure("读取长度必须大于 0。");
            return false;
        }

        if (IsBusy && fromTimer)
        {
            return true;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _modbusMemoryService.Read(_sessionService.CurrentDataStore, ModbusMemoryEditor.SelectedArea, ModbusMemoryEditor.Offset, ModbusMemoryEditor.Length, ModbusMemoryEditor.SelectedDataType));
            ModbusMemoryEditor.SnapshotText = result.IsSuccess ? result.ResultValue ?? string.Empty : result.Message;
            LatestResult = result.Message;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            AppendLog(fromTimer ? "定时读取 DataStore" : "读取 DataStore", result.IsSuccess, result.Message, result.TimeConsuming);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> ExecuteMemoryWriteOnceAsync(bool fromTimer = false)
    {
        if (!IsSessionActive || !ShowMemoryEditor || !_sessionService.SupportsMemoryEditor || _sessionService.CurrentDataStore == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ModbusMemoryEditor.WriteValue))
        {
            ApplyFailure("写入值不能为空。");
            return false;
        }

        if (IsBusy && fromTimer)
        {
            return true;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _modbusMemoryService.Write(_sessionService.CurrentDataStore, ModbusMemoryEditor.SelectedArea, ModbusMemoryEditor.Offset, ModbusMemoryEditor.SelectedDataType, ModbusMemoryEditor.WriteValue));
            ModbusMemoryEditor.SnapshotText = result.IsSuccess ? result.ResultValue ?? string.Empty : result.Message;
            LatestResult = result.Message;
            StatusBrush = result.IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
            AppendLog(fromTimer ? "定时写入 DataStore" : "写入 DataStore", result.IsSuccess, result.Message, result.TimeConsuming);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAddressAutoReadLoopAsync(CancellationTokenSource cts)
    {
        var round = 0;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                round++;
                var packetMeaningPrefix = $"定时读 第{round}轮 | 地址={AddressWorkbench.Address} | 长度={Math.Max(1, AddressWorkbench.Length)}";
                var shouldContinue = await ExecuteAddressReadOnceAsync(fromTimer: true, packetMeaningPrefix);
                if (!shouldContinue)
                {
                    break;
                }

                var interval = Math.Max(1, AddressWorkbench.AutoReadIntervalMs);
                AddressWorkbench.AutoReadStatusText = $"定时读取运行中，周期 {interval} ms。";
                await Task.Delay(interval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_addressAutoReadCts, cts))
            {
                cts.Dispose();
                _addressAutoReadCts = null;
                AddressWorkbench.IsAutoReadRunning = false;
                AddressWorkbench.AutoReadStatusText = "定时读取已停止。";
            }
        }
    }

    private async Task RunAddressAutoWriteLoopAsync(CancellationTokenSource cts)
    {
        var round = 0;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                round++;
                var packetMeaningPrefix = $"定时写 第{round}轮 | 地址={AddressWorkbench.Address} | 长度={GetWriteLengthEstimate(AddressWorkbench.WriteValue)}";
                var shouldContinue = await ExecuteAddressWriteOnceAsync(fromTimer: true, packetMeaningPrefix);
                if (!shouldContinue)
                {
                    break;
                }

                var interval = Math.Max(1, AddressWorkbench.AutoWriteIntervalMs);
                AddressWorkbench.AutoWriteStatusText = $"定时写入运行中，周期 {interval} ms。";
                await Task.Delay(interval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_addressAutoWriteCts, cts))
            {
                cts.Dispose();
                _addressAutoWriteCts = null;
                AddressWorkbench.IsAutoWriteRunning = false;
                AddressWorkbench.AutoWriteStatusText = "定时写入已停止。";
            }
        }
    }

    private async Task RunMemoryAutoReadLoopAsync(CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var shouldContinue = await ExecuteMemoryReadOnceAsync(fromTimer: true);
                if (!shouldContinue)
                {
                    break;
                }

                var interval = Math.Max(1, ModbusMemoryEditor.AutoReadIntervalMs);
                ModbusMemoryEditor.AutoReadStatusText = $"定时读取运行中，周期 {interval} ms。";
                await Task.Delay(interval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_memoryAutoReadCts, cts))
            {
                cts.Dispose();
                _memoryAutoReadCts = null;
                ModbusMemoryEditor.IsAutoReadRunning = false;
                ModbusMemoryEditor.AutoReadStatusText = "定时读取已停止。";
            }
        }
    }

    private async Task RunMemoryAutoWriteLoopAsync(CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var shouldContinue = await ExecuteMemoryWriteOnceAsync(fromTimer: true);
                if (!shouldContinue)
                {
                    break;
                }

                var interval = Math.Max(1, ModbusMemoryEditor.AutoWriteIntervalMs);
                ModbusMemoryEditor.AutoWriteStatusText = $"定时写入运行中，周期 {interval} ms。";
                await Task.Delay(interval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_memoryAutoWriteCts, cts))
            {
                cts.Dispose();
                _memoryAutoWriteCts = null;
                ModbusMemoryEditor.IsAutoWriteRunning = false;
                ModbusMemoryEditor.AutoWriteStatusText = "定时写入已停止。";
            }
        }
    }

    private void StopAddressAutoReadInternal()
    {
        if (_addressAutoReadCts == null)
        {
            AddressWorkbench.IsAutoReadRunning = false;
            AddressWorkbench.AutoReadStatusText = "定时读取未启动。";
            return;
        }

        _addressAutoReadCts.Cancel();
        AddressWorkbench.AutoReadStatusText = "定时读取已停止。";
    }

    private void StopAddressAutoWriteInternal()
    {
        if (_addressAutoWriteCts == null)
        {
            AddressWorkbench.IsAutoWriteRunning = false;
            AddressWorkbench.AutoWriteStatusText = "定时写入未启动。";
            return;
        }

        _addressAutoWriteCts.Cancel();
        AddressWorkbench.AutoWriteStatusText = "定时写入已停止。";
    }

    private void StopMemoryAutoReadInternal()
    {
        if (_memoryAutoReadCts == null)
        {
            ModbusMemoryEditor.IsAutoReadRunning = false;
            ModbusMemoryEditor.AutoReadStatusText = "定时读取未启动。";
            return;
        }

        _memoryAutoReadCts.Cancel();
        ModbusMemoryEditor.AutoReadStatusText = "定时读取已停止。";
    }

    private void StopMemoryAutoWriteInternal()
    {
        if (_memoryAutoWriteCts == null)
        {
            ModbusMemoryEditor.IsAutoWriteRunning = false;
            ModbusMemoryEditor.AutoWriteStatusText = "定时写入未启动。";
            return;
        }

        _memoryAutoWriteCts.Cancel();
        ModbusMemoryEditor.AutoWriteStatusText = "定时写入已停止。";
    }

    private void StopAllAutoOperations()
    {
        StopAddressAutoReadInternal();
        StopAddressAutoWriteInternal();
        StopMemoryAutoReadInternal();
        StopMemoryAutoWriteInternal();
    }

    private static int GetWriteLengthEstimate(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return 1;
        }

        var tokens = rawValue.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return Math.Max(1, tokens.Length);
    }
}
