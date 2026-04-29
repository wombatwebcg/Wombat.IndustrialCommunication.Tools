using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Wombat.Extensions.DataTypeExtensions;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class AddressWorkbenchViewModel : ViewModelBase
{
    public AddressWorkbenchViewModel()
    {
        SupportedDataTypes =
        [
            DataTypeEnums.Bool,
            DataTypeEnums.Byte,
            DataTypeEnums.Int16,
            DataTypeEnums.UInt16,
            DataTypeEnums.Int32,
            DataTypeEnums.UInt32,
            DataTypeEnums.Int64,
            DataTypeEnums.UInt64,
            DataTypeEnums.Float,
            DataTypeEnums.Double
        ];
        SelectedDataType = DataTypeEnums.Int16;
        ConfigureFor(null);
    }

    public DataTypeEnums[] SupportedDataTypes { get; }

    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private DataTypeEnums _selectedDataType;
    [ObservableProperty] private int _length = 1;
    [ObservableProperty] private string _writeValue = "1";
    [ObservableProperty] private string _readResultText = "连接成功后可在这里查看读取结果。";
    [ObservableProperty] private string _writeFeedbackText = "写入结果会显示在这里。";
    [ObservableProperty] private string _lastDurationText = "最近耗时: -";
    [ObservableProperty] private int _autoReadIntervalMs = 1000;
    [ObservableProperty] private int _autoWriteIntervalMs = 1000;
    [ObservableProperty] private bool _isAutoReadRunning;
    [ObservableProperty] private bool _isAutoWriteRunning;
    [ObservableProperty] private string _autoReadStatusText = "定时读取未启动。";
    [ObservableProperty] private string _autoWriteStatusText = "定时写入未启动。";

    public void ConfigureFor(CommunicationComponentDefinition? definition)
    {
        Address = definition?.ExampleAddresses.FirstOrDefault() ?? string.Empty;
        Length = 1;
        ReadResultText = "连接成功后可在这里查看读取结果。";
        WriteFeedbackText = "写入结果会显示在这里。";
        IsAutoReadRunning = false;
        IsAutoWriteRunning = false;
        AutoReadStatusText = "定时读取未启动。";
        AutoWriteStatusText = "定时写入未启动。";
        UpdateWriteSample();
    }

    partial void OnSelectedDataTypeChanged(DataTypeEnums value)
    {
        UpdateWriteSample();
    }

    private void UpdateWriteSample()
    {
        WriteValue = SelectedDataType switch
        {
            DataTypeEnums.Bool => "true",
            DataTypeEnums.Float => "12.5",
            DataTypeEnums.Double => "123.456",
            _ => "1"
        };
    }

    partial void OnAutoReadIntervalMsChanged(int value)
    {
        if (value < 1)
        {
            AutoReadIntervalMs = 1;
        }
    }

    partial void OnAutoWriteIntervalMsChanged(int value)
    {
        if (value < 1)
        {
            AutoWriteIntervalMs = 1;
        }
    }
}
