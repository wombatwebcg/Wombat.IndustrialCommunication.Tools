using Wombat.IndustrialCommunication.Tools.ViewModels;

namespace Wombat.IndustrialCommunication.Tools.Services;

public sealed class OperationLogService
{
    public OperationLogItemViewModel Create(string component, string action, bool isSuccess, string message, double? durationMilliseconds)
    {
        return new OperationLogItemViewModel(component, action, isSuccess, message, durationMilliseconds);
    }
}
