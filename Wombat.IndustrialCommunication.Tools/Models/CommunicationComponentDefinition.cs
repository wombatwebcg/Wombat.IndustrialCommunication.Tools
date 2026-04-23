using System;
using System.Collections.Generic;

namespace Wombat.IndustrialCommunication.Tools.Models;

public sealed record CommunicationComponentDefinition(
    CommunicationComponentKind Kind,
    string DisplayName,
    string CategoryName,
    string Summary,
    string CapabilitySummary,
    bool SupportsAddressWorkbench,
    bool SupportsMemoryEditor,
    IReadOnlyList<string> CapabilityBadges,
    IReadOnlyList<string> ExampleAddresses,
    IReadOnlyList<string> QuickTips)
{
    public bool IsServer => string.Equals(CategoryName, "服务端", StringComparison.Ordinal);

    public string StartActionText => IsServer ? "开始监听" : "连接设备";

    public string StopActionText => IsServer ? "停止监听" : "断开连接";
}
