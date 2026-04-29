namespace Wombat.IndustrialCommunication.Tools.Models;

public sealed class BluetoothServiceOption
{
    public string ServiceId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool IsPrimary { get; init; }

    public string Description { get; init; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? ServiceId : DisplayName;
}
