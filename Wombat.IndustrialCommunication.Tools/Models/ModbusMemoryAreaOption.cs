namespace Wombat.IndustrialCommunication.Tools.Models;

public sealed record ModbusMemoryAreaOption(string Key, string DisplayName, bool IsDiscreteArea)
{
    public override string ToString() => DisplayName;
}
