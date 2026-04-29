namespace Wombat.IndustrialCommunication.Tools.Models;

public sealed class BluetoothDeviceOption
{
    public string DeviceId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool IsPaired { get; init; }

    public string Description { get; init; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? DeviceId : DisplayName;
}
