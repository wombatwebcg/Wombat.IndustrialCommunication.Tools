using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public sealed class CommunicationComponentItemViewModel : ViewModelBase
{
    public CommunicationComponentItemViewModel(CommunicationComponentDefinition definition)
    {
        Definition = definition;
    }

    public CommunicationComponentDefinition Definition { get; }

    public string DisplayName => Definition.DisplayName;

    public string CategoryName => Definition.CategoryName;

    public string Summary => Definition.Summary;

    public string BadgeSummary => string.Join(" / ", Definition.CapabilityBadges);
}
