using System;
using Avalonia.Media;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public sealed class OperationLogItemViewModel : ViewModelBase
{
    public OperationLogItemViewModel(string component, string action, bool isSuccess, string message, double? durationMilliseconds)
    {
        Timestamp = DateTime.Now.ToString("HH:mm:ss");
        Component = component;
        Action = action;
        IsSuccess = isSuccess;
        Message = message;
        DurationText = durationMilliseconds.HasValue ? $"{durationMilliseconds.Value:0.##} ms" : "-";
    }

    public string Timestamp { get; }

    public string Component { get; }

    public string Action { get; }

    public bool IsSuccess { get; }

    public string Message { get; }

    public string DurationText { get; }

    public string StatusText => IsSuccess ? "成功" : "失败";

    public IBrush AccentBrush => IsSuccess ? Brushes.SeaGreen : Brushes.IndianRed;
}
