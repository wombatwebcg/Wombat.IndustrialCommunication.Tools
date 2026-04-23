using System.Collections.Generic;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.Services;

public sealed class ComponentCatalogService
{
    public IReadOnlyList<CommunicationComponentDefinition> GetComponents()
    {
        return
        [
            new CommunicationComponentDefinition(CommunicationComponentKind.ModbusRtuClient, "Modbus RTU 客户端", "客户端", "适合串口站点调试，连接成功后即可通过统一地址字符串进行任意地址读写。", "支持串口连接、统一地址读写、自动重连和安全默认值。", true, false, ["串口", "统一地址", "读写"], [ "1;40001", "1;3;0", "1;40003"], ["默认值采用 COM1 / 9600 / 8N1。", "地址可用 标准格式 站号;功能码;地址，也可用增强格式 站号;逻辑地址。", "字符串类型未开放，请优先使用数值和布尔类型。"]),
            new CommunicationComponentDefinition(CommunicationComponentKind.ModbusTcpClient, "Modbus TCP 客户端", "客户端", "面向网络设备调试，连接成功后使用统一地址字符串完成任意地址读写。", "支持网络连接、统一地址读写、自动重连和示例地址提示。", true, false, ["网络", "统一地址", "读写"], ["1;40001", "1;3;0", "1;40003"], ["默认目标为 127.0.0.1:502。", "长连接模式更适合连续调试，短连接更适合间歇访问。", "若地址无法识别，优先尝试 站号;功能码;地址 的完整格式。"]),
            new CommunicationComponentDefinition(CommunicationComponentKind.SiemensClient, "Siemens 客户端", "客户端", "适用于 S7 PLC 调试，连接后使用 Siemens 地址格式进行任意地址读写。", "支持机型选择、Rack/Slot 配置、统一地址读写和连接状态展示。", true, false, ["S7", "统一地址", "PLC"], ["DB1.DBW0", "DB1.DBX2.0", "M10"], ["先确认 SiemensVersion、Rack 和 Slot 再连接。", "DB 地址适合结构化读写，M/I/Q/T/C/V 地址适合现场快速排查。", "字符串类型未开放，请优先使用 Bool/Int/Float/Double。"]),
            new CommunicationComponentDefinition(CommunicationComponentKind.ModbusRtuServer, "Modbus RTU 服务端", "服务端", "通过 DataStore 本地内存区模拟 RTU 从站，适合联调外部主站。", "支持串口监听、DataStore 内存编辑、本地快照浏览和协议访问说明。", false, true, ["串口", "DataStore", "服务端"], ["CoilDiscretes @ 0", "HoldingRegisters @ 0", "InputRegisters @ 10"], ["统一地址读写在该组件上不开放，请使用右侧内存编辑器。", "Coil/InputDiscrete 使用布尔模式。", "Holding/InputRegister 支持 16/32/64 位数值和浮点类型。"]),
            new CommunicationComponentDefinition(CommunicationComponentKind.ModbusTcpServer, "Modbus TCP 服务端", "服务端", "通过 DataStore 本地内存区模拟 TCP 从站，适合网络主站联调。", "支持网络监听、DataStore 内存编辑、监听状态反馈和结果日志。", false, true, ["网络", "DataStore", "服务端"], ["CoilDiscretes @ 0", "HoldingRegisters @ 0", "InputRegisters @ 10"], ["默认监听 0.0.0.0:502。", "本地编辑即修改服务端对外暴露的内存镜像。", "建议先读快照确认偏移和长度，再执行写入。"]),
            new CommunicationComponentDefinition(CommunicationComponentKind.S7TcpServer, "S7 TCP 服务端", "服务端", "模拟 Siemens S7 服务端，支持监听、默认 DB 创建、统一地址读写和事件日志。", "支持网络监听、默认 DB 初始化、统一地址读写和 DataRead/DataWritten 事件观察。", true, false, ["S7", "服务端", "事件日志"], ["DB1.DBX0.0", "DB1.DBW0", "M0", "Q0"], ["建议先创建 DB1，再开始监听。", "监听后可直接在统一地址工作区验证读写。", "右侧日志会持续展示 DataRead/DataWritten 事件。"])
        ];
    }
}
