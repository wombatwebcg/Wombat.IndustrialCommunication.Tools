using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class MainViewModel
{
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
            var result = await Task.Run(() => _sessionService.StartSession(SelectedDefinition, ConnectionForm, HandleBackgroundEvent, HandleServerPacketTrace));
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

            RecordClientPackets(result, $"{StartButtonText}请求", $"{StartButtonText}响应");
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

        StopAllAutoOperations();
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
}
