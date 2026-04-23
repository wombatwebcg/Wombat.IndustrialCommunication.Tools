using Wombat.Extensions.DataTypeExtensions;
using Wombat.IndustrialCommunication;

namespace Wombat.IndustrialCommunication.Tools.Services;

public interface IAddressAccessor
{
    OperationResult<object> Read(DataTypeEnums dataType, string address, int length);

    OperationResult Write(DataTypeEnums dataType, string address, object value);

    OperationResult WriteMany(DataTypeEnums dataType, string address, object[] values);
}
