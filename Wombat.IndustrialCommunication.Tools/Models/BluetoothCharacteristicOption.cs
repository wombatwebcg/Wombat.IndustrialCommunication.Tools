namespace Wombat.IndustrialCommunication.Tools.Models;

public sealed class BluetoothCharacteristicOption
{
    public string CharacteristicId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool CanRead { get; init; }

    public bool CanWrite { get; init; }

    public bool CanNotify { get; init; }

    public string Description { get; init; } = string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? CharacteristicId : DisplayName;
}
