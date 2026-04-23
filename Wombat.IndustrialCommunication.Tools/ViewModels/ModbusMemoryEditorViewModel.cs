using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Wombat.Extensions.DataTypeExtensions;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public partial class ModbusMemoryEditorViewModel : ViewModelBase
{
    public ModbusMemoryEditorViewModel()
    {
        Areas =
        [
            new ModbusMemoryAreaOption("CoilDiscretes", "CoilDiscretes", true),
            new ModbusMemoryAreaOption("InputDiscretes", "InputDiscretes", true),
            new ModbusMemoryAreaOption("HoldingRegisters", "HoldingRegisters", false),
            new ModbusMemoryAreaOption("InputRegisters", "InputRegisters", false)
        ];
        SelectedArea = Areas[0];
    }

    public ModbusMemoryAreaOption[] Areas { get; }

    public DataTypeEnums[] DiscreteDataTypes { get; } = [DataTypeEnums.Bool];

    public DataTypeEnums[] RegisterDataTypes { get; } =
    [
        DataTypeEnums.Int16,
        DataTypeEnums.UInt16,
        DataTypeEnums.Int32,
        DataTypeEnums.UInt32,
        DataTypeEnums.Int64,
        DataTypeEnums.UInt64,
        DataTypeEnums.Float,
        DataTypeEnums.Double
    ];

    [ObservableProperty] private ModbusMemoryAreaOption? _selectedArea;
    [ObservableProperty] private DataTypeEnums _selectedDataType = DataTypeEnums.Bool;
    [ObservableProperty] private int _offset;
    [ObservableProperty] private int _length = 1;
    [ObservableProperty] private string _writeValue = "true";
    [ObservableProperty] private string _snapshotText = "监听成功后可在这里读取 DataStore 快照。";

    public DataTypeEnums[] CurrentDataTypes => SelectedArea?.IsDiscreteArea == true ? DiscreteDataTypes : RegisterDataTypes;

    public bool IsDiscreteArea => SelectedArea?.IsDiscreteArea == true;

    public void ConfigureFor(CommunicationComponentDefinition? definition)
    {
        Offset = 0;
        Length = 1;
        SnapshotText = definition?.DisplayName is null ? "监听成功后可在这里读取 DataStore 快照。" : $"{definition.DisplayName} 启动后，可在这里查看当前内存片段。";
        SelectedArea = Areas[0];
    }

    partial void OnSelectedAreaChanged(ModbusMemoryAreaOption? value)
    {
        SelectedDataType = value?.IsDiscreteArea == true ? DataTypeEnums.Bool : DataTypeEnums.Int16;
        WriteValue = value?.IsDiscreteArea == true ? "true" : "1";
        OnPropertyChanged(nameof(CurrentDataTypes));
        OnPropertyChanged(nameof(IsDiscreteArea));
    }
}
