using System;

namespace Wombat.IndustrialCommunication.Tools.ViewModels;

public sealed class PacketTraceItemViewModel : ViewModelBase
{
    public PacketTraceItemViewModel(string meaning, string payload)
    {
        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Meaning = meaning;
        Payload = payload;
    }

    public string Timestamp { get; }

    public string Meaning { get; }

    public string Payload { get; }
}
