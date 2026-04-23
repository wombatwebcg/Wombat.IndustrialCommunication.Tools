using System;
using System.Collections.Generic;
using System.Globalization;
using Wombat.Extensions.DataTypeExtensions;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Modbus.Data;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.Services;

public sealed class ModbusServerMemoryService
{
    public IReadOnlyList<DataTypeEnums> DiscreteDataTypes { get; } = [DataTypeEnums.Bool];

    public IReadOnlyList<DataTypeEnums> RegisterDataTypes { get; } =
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

    public OperationResult<string> Read(DataStore dataStore, ModbusMemoryAreaOption? area, int offset, int length, DataTypeEnums dataType)
    {
        try
        {
            if (dataStore == null)
            {
                return OperationResult.CreateFailedResult<string>("当前没有可用的 Modbus 服务端会话。");
            }

            if (area == null)
            {
                return OperationResult.CreateFailedResult<string>("请选择内存区域。");
            }

            if (offset < 0 || length <= 0)
            {
                return OperationResult.CreateFailedResult<string>("偏移和长度必须有效。");
            }

            if (area.IsDiscreteArea)
            {
                var values = ReadDiscretes(dataStore, area.Key, offset, length);
                return OperationResult.CreateSuccessResult<string>(string.Join(", ", values), $"读取 {length} 个离散量成功。");
            }

            ValidateRegisterDataType(dataType);
            var span = GetRegisterSpan(dataType);
            var registers = ReadRegisters(dataStore, area.Key, offset, span * length);
            var valuesText = ConvertRegistersToText(registers, dataType);
            return OperationResult.CreateSuccessResult<string>($"值: {valuesText}{Environment.NewLine}寄存器: {string.Join(", ", registers)}", $"读取 {length} 个寄存器值成功。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<string>(ex);
        }
    }

    public OperationResult<string> Write(DataStore dataStore, ModbusMemoryAreaOption? area, int offset, DataTypeEnums dataType, string rawValue)
    {
        try
        {
            if (dataStore == null)
            {
                return OperationResult.CreateFailedResult<string>("当前没有可用的 Modbus 服务端会话。");
            }

            if (area == null)
            {
                return OperationResult.CreateFailedResult<string>("请选择内存区域。");
            }

            if (offset < 0)
            {
                return OperationResult.CreateFailedResult<string>("偏移不能为负数。");
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return OperationResult.CreateFailedResult<string>("写入值不能为空。");
            }

            var tokens = rawValue.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (area.IsDiscreteArea)
            {
                var values = new bool[tokens.Length];
                for (var index = 0; index < tokens.Length; index++)
                {
                    values[index] = ParseBoolean(tokens[index]);
                }

                WriteDiscretes(dataStore, area.Key, offset, values);
                return OperationResult.CreateSuccessResult<string>(string.Join(", ", values), $"写入 {values.Length} 个离散量成功。");
            }

            ValidateRegisterDataType(dataType);
            var buffer = new List<ushort>();
            for (var index = 0; index < tokens.Length; index++)
            {
                buffer.AddRange(ConvertValueToRegisters(tokens[index].ConvertFromStringToObject(dataType), dataType));
            }

            WriteRegisters(dataStore, area.Key, offset, buffer.ToArray());
            return OperationResult.CreateSuccessResult<string>($"值: {string.Join(", ", tokens)}{Environment.NewLine}寄存器: {string.Join(", ", buffer)}", $"写入 {tokens.Length} 个寄存器值成功。");
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<string>(ex);
        }
    }

    private static bool[] ReadDiscretes(DataStore dataStore, string areaKey, int offset, int length)
    {
        var memory = areaKey == "CoilDiscretes" ? dataStore.CoilDiscretes : dataStore.InputDiscretes;
        EnsureRange(memory.Size, offset, length);
        var values = new bool[length];
        lock (dataStore.SyncRoot)
        {
            for (var index = 0; index < length; index++)
            {
                values[index] = memory[offset + index];
            }
        }

        return values;
    }

    private static ushort[] ReadRegisters(DataStore dataStore, string areaKey, int offset, int length)
    {
        var memory = areaKey == "HoldingRegisters" ? dataStore.HoldingRegisters : dataStore.InputRegisters;
        EnsureRange(memory.Size, offset, length);
        var values = new ushort[length];
        lock (dataStore.SyncRoot)
        {
            for (var index = 0; index < length; index++)
            {
                values[index] = (ushort)memory[offset + index];
            }
        }

        return values;
    }

    private static void WriteDiscretes(DataStore dataStore, string areaKey, int offset, IReadOnlyList<bool> values)
    {
        var memory = areaKey == "CoilDiscretes" ? dataStore.CoilDiscretes : dataStore.InputDiscretes;
        EnsureRange(memory.Size, offset, values.Count);
        lock (dataStore.SyncRoot)
        {
            for (var index = 0; index < values.Count; index++)
            {
                memory[offset + index] = values[index];
            }
        }
    }

    private static void WriteRegisters(DataStore dataStore, string areaKey, int offset, IReadOnlyList<ushort> values)
    {
        var memory = areaKey == "HoldingRegisters" ? dataStore.HoldingRegisters : dataStore.InputRegisters;
        EnsureRange(memory.Size, offset, values.Count);
        lock (dataStore.SyncRoot)
        {
            for (var index = 0; index < values.Count; index++)
            {
                memory[offset + index] = values[index];
            }
        }
    }

    private static void EnsureRange(int size, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > size)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "访问范围超出当前内存区大小。");
        }
    }

    private static void ValidateRegisterDataType(DataTypeEnums dataType)
    {
        if (dataType == DataTypeEnums.Bool || dataType == DataTypeEnums.Byte || dataType == DataTypeEnums.None || dataType == DataTypeEnums.String)
        {
            throw new InvalidOperationException("当前内存区仅支持 16/32/64 位整数和浮点类型。");
        }
    }

    private static int GetRegisterSpan(DataTypeEnums dataType)
    {
        return dataType switch
        {
            DataTypeEnums.Int16 or DataTypeEnums.UInt16 => 1,
            DataTypeEnums.Int32 or DataTypeEnums.UInt32 or DataTypeEnums.Float => 2,
            DataTypeEnums.Int64 or DataTypeEnums.UInt64 or DataTypeEnums.Double => 4,
            _ => throw new InvalidOperationException($"不支持的数据类型: {dataType}")
        };
    }

    private static string ConvertRegistersToText(IReadOnlyList<ushort> registers, DataTypeEnums dataType)
    {
        var span = GetRegisterSpan(dataType);
        var items = new List<string>();
        for (var index = 0; index < registers.Count; index += span)
        {
            items.Add(FormatSingleValue(ConvertRegistersToValue(registers, index, span, dataType)));
        }

        return string.Join(", ", items);
    }

    private static object ConvertRegistersToValue(IReadOnlyList<ushort> registers, int index, int span, DataTypeEnums dataType)
    {
        var bytes = new byte[span * 2];
        for (var i = 0; i < span; i++)
        {
            var register = registers[index + i];
            bytes[i * 2] = (byte)(register >> 8);
            bytes[(i * 2) + 1] = (byte)(register & 0xFF);
        }

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return dataType switch
        {
            DataTypeEnums.Int16 => BitConverter.ToInt16(bytes, 0),
            DataTypeEnums.UInt16 => BitConverter.ToUInt16(bytes, 0),
            DataTypeEnums.Int32 => BitConverter.ToInt32(bytes, 0),
            DataTypeEnums.UInt32 => BitConverter.ToUInt32(bytes, 0),
            DataTypeEnums.Int64 => BitConverter.ToInt64(bytes, 0),
            DataTypeEnums.UInt64 => BitConverter.ToUInt64(bytes, 0),
            DataTypeEnums.Float => BitConverter.ToSingle(bytes, 0),
            DataTypeEnums.Double => BitConverter.ToDouble(bytes, 0),
            _ => throw new InvalidOperationException($"不支持的数据类型: {dataType}")
        };
    }

    private static IReadOnlyList<ushort> ConvertValueToRegisters(object value, DataTypeEnums dataType)
    {
        byte[] bytes = dataType switch
        {
            DataTypeEnums.Int16 => BitConverter.GetBytes(Convert.ToInt16(value, CultureInfo.InvariantCulture)),
            DataTypeEnums.UInt16 => BitConverter.GetBytes(Convert.ToUInt16(value, CultureInfo.InvariantCulture)),
            DataTypeEnums.Int32 => BitConverter.GetBytes(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            DataTypeEnums.UInt32 => BitConverter.GetBytes(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            DataTypeEnums.Int64 => BitConverter.GetBytes(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            DataTypeEnums.UInt64 => BitConverter.GetBytes(Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
            DataTypeEnums.Float => BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            DataTypeEnums.Double => BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException($"不支持的数据类型: {dataType}")
        };

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        var registers = new ushort[bytes.Length / 2];
        for (var index = 0; index < registers.Length; index++)
        {
            registers[index] = (ushort)((bytes[index * 2] << 8) | bytes[(index * 2) + 1]);
        }

        return registers;
    }

    private static bool ParseBoolean(string rawValue)
    {
        return rawValue.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new FormatException($"无法将 '{rawValue}' 解析为布尔值。")
        };
    }

    private static string FormatSingleValue(object value)
    {
        return value switch
        {
            float floatValue => floatValue.ToString("0.####", CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString("0.####", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}
