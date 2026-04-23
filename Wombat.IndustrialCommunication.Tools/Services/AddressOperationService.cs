using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Wombat.Extensions.DataTypeExtensions;
using Wombat.IndustrialCommunication;

namespace Wombat.IndustrialCommunication.Tools.Services;

public sealed class AddressOperationService
{
    public IReadOnlyList<DataTypeEnums> SupportedDataTypes { get; } =
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

    public OperationResult<string> Read(IAddressAccessor accessor, DataTypeEnums dataType, string address, int length)
    {
        var response = new OperationResult<string>();

        try
        {
            if (accessor == null)
            {
                return OperationResult.CreateFailedResult<string>("当前没有可用的地址会话。");
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return OperationResult.CreateFailedResult<string>("地址不能为空。");
            }

            var result = accessor.Read(dataType, address.Trim(), Math.Max(1, length));
            response.SetInfo(result);
            response.ResultValue = result.IsSuccess ? FormatObject(result.ResultValue) : result.Message;
            return response.Complete();
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<string>(ex);
        }
    }

    public OperationResult<string> Write(IAddressAccessor accessor, DataTypeEnums dataType, string address, string rawValue)
    {
        var response = new OperationResult<string>();

        try
        {
            if (accessor == null)
            {
                return OperationResult.CreateFailedResult<string>("当前没有可用的地址会话。");
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return OperationResult.CreateFailedResult<string>("地址不能为空。");
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return OperationResult.CreateFailedResult<string>("写入值不能为空。");
            }

            var tokens = rawValue.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            OperationResult result;

            if (tokens.Length > 1)
            {
                var values = new object[tokens.Length];
                for (var index = 0; index < tokens.Length; index++)
                {
                    values[index] = ConvertScalar(tokens[index], dataType);
                }

                result = accessor.WriteMany(dataType, address.Trim(), values);
                response.ResultValue = string.Join(", ", values);
            }
            else
            {
                var value = ConvertScalar(tokens[0], dataType);
                result = accessor.Write(dataType, address.Trim(), value);
                response.ResultValue = FormatSingleValue(value);
            }

            response.SetInfo(result);
            if (!result.IsSuccess)
            {
                response.ResultValue = result.Message;
            }

            return response.Complete();
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<string>(ex);
        }
    }

    private static object ConvertScalar(string rawValue, DataTypeEnums dataType)
    {
        if (dataType == DataTypeEnums.Bool)
        {
            if (string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(rawValue, "0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return rawValue.ConvertFromStringToObject(dataType);
    }

    private static string FormatObject(object? value)
    {
        if (value == null)
        {
            return "<null>";
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IEnumerable enumerable)
        {
            var items = new List<string>();
            foreach (var item in enumerable)
            {
                items.Add(FormatSingleValue(item));
            }

            return string.Join(", ", items);
        }

        return FormatSingleValue(value);
    }

    private static string FormatSingleValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            float floatValue => floatValue.ToString("0.####", CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString("0.####", CultureInfo.InvariantCulture),
            bool boolValue => boolValue ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}
