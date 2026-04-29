using System;
using System.IO.Ports;
using System.Linq;
using Wombat.Extensions.DataTypeExtensions;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Extensions.Bluetooth.Factory;
using Wombat.IndustrialCommunication.Extensions.Bluetooth.Models;
using Wombat.IndustrialCommunication.Extensions.Bluetooth.Modbus;
using Wombat.IndustrialCommunication.Modbus;
using Wombat.IndustrialCommunication.Modbus.Data;
using Wombat.IndustrialCommunication.PLC;
using Wombat.IndustrialCommunication.Tools.Models;
using Wombat.IndustrialCommunication.Tools.Services.Platform;
using Wombat.IndustrialCommunication.Tools.ViewModels;

namespace Wombat.IndustrialCommunication.Tools.Services;

public sealed class DeviceSessionService : IDisposable
{
    private readonly IBluetoothPlatformService _bluetoothPlatformService;
    private SessionHandle? _currentSession;

    public DeviceSessionService(IBluetoothPlatformService bluetoothPlatformService)
    {
        _bluetoothPlatformService = bluetoothPlatformService;
    }

    public bool HasActiveSession => _currentSession != null;
    public bool SupportsAddressWorkbench => _currentSession?.AddressAccessor != null;
    public bool SupportsMemoryEditor => _currentSession?.DataStore != null;
    public bool SupportsDefaultDbCreation => _currentSession?.S7Server != null;
    public IAddressAccessor? CurrentAddressAccessor => _currentSession?.AddressAccessor;
    public DataStore? CurrentDataStore => _currentSession?.DataStore;
    public string ActiveEndpoint => _currentSession?.Endpoint ?? "未连接";
    public CommunicationComponentKind? ActiveKind => _currentSession?.Definition.Kind;

    public OperationResult StartSession(
        CommunicationComponentDefinition definition,
        ConnectionFormViewModel form,
        Action<string, string>? eventSink,
        Action<PacketTraceEventArgs>? packetSink)
    {
        StopSession();

        try
        {
            var session = CreateSession(definition, form, eventSink, packetSink);
            var startResult = session.Start();
            if (!startResult.IsSuccess)
            {
                session.Dispose();
                return startResult;
            }

            _currentSession = session;
            return startResult;
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult(ex);
        }
    }

    public OperationResult StopSession()
    {
        if (_currentSession == null)
        {
            return OperationResult.CreateSuccessResult("当前没有活动会话。");
        }

        var session = _currentSession;
        _currentSession = null;
        try
        {
            return session.Stop();
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult(ex);
        }
        finally
        {
            session.Dispose();
        }
    }

    public OperationResult CreateDefaultDataBlock(int dbNumber, int size)
    {
        try
        {
            if (_currentSession?.S7Server == null)
            {
                return OperationResult.CreateFailedResult("当前会话不是 S7 TCP 服务端。");
            }

            return _currentSession.S7Server.CreateDataBlock(dbNumber, size);
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult(ex);
        }
    }

    public void Dispose() => StopSession();

    private SessionHandle CreateSession(
        CommunicationComponentDefinition definition,
        ConnectionFormViewModel form,
        Action<string, string>? eventSink,
        Action<PacketTraceEventArgs>? packetSink)
    {
        return definition.Kind switch
        {
            CommunicationComponentKind.ModbusRtuClient => CreateModbusRtuClientSession(definition, form),
            CommunicationComponentKind.ModbusRtuBluetoothClient => CreateModbusRtuBluetoothClientSession(definition, form),
            CommunicationComponentKind.ModbusRtuBluetoothServer => CreateModbusRtuBluetoothServerSession(definition, form, eventSink, packetSink),
            CommunicationComponentKind.ModbusTcpClient => CreateModbusTcpClientSession(definition, form),
            CommunicationComponentKind.SiemensClient => CreateSiemensClientSession(definition, form),
            CommunicationComponentKind.ModbusRtuServer => CreateModbusRtuServerSession(definition, form, eventSink, packetSink),
            CommunicationComponentKind.ModbusTcpServer => CreateModbusTcpServerSession(definition, form, eventSink, packetSink),
            CommunicationComponentKind.S7TcpServer => CreateS7ServerSession(definition, form, eventSink, packetSink),
            _ => throw new InvalidOperationException($"未知组件类型: {definition.Kind}")
        };
    }

    private SessionHandle CreateModbusRtuBluetoothClientSession(CommunicationComponentDefinition definition, ConnectionFormViewModel form)
    {
        var options = new BluetoothConnectionOptions
        {
            DeviceId = form.BluetoothDeviceId,
            ServiceId = form.BluetoothServiceId,
            WriteCharacteristicId = form.BluetoothWriteCharacteristicId,
            NotifyCharacteristicId = form.BluetoothNotifyCharacteristicId,
            ConnectTimeout = TimeSpan.FromSeconds(form.ConnectTimeoutSeconds),
            ReceiveTimeout = TimeSpan.FromSeconds(form.ReceiveTimeoutSeconds),
            SendTimeout = TimeSpan.FromSeconds(form.SendTimeoutSeconds)
        };

        var validation = options.Validate();
        if (!validation.IsSuccess)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var channelResult = _bluetoothPlatformService.CreateChannel(options);
        if (!channelResult.IsSuccess || channelResult.ResultValue == null)
        {
            throw new InvalidOperationException(channelResult.Message);
        }

        var client = BluetoothClientFactory.CreateModbusRtuClient(channelResult.ResultValue, options);
        client.IsLongConnection = form.IsLongConnection;
        client.EnableAutoReconnect = form.EnableAutoReconnect;
        client.MaxReconnectAttempts = form.MaxReconnectAttempts;
        client.ReconnectDelay = TimeSpan.FromSeconds(form.ReconnectDelaySeconds);

        var endpointName = string.IsNullOrWhiteSpace(form.BluetoothDeviceName) ? form.BluetoothDeviceId : form.BluetoothDeviceName;
        return new SessionHandle(
            definition,
            $"{endpointName} / {form.BluetoothServiceId} / 写入 {form.BluetoothWriteCharacteristicId} / 通知 {form.BluetoothNotifyCharacteristicId}",
            client.Connect,
            client.Disconnect,
            new DeviceAddressAccessor(client),
            null,
            null,
            client,
            null);
    }

    private SessionHandle CreateModbusRtuBluetoothServerSession(
        CommunicationComponentDefinition definition,
        ConnectionFormViewModel form,
        Action<string, string>? eventSink,
        Action<PacketTraceEventArgs>? packetSink)
    {
        var options = new BluetoothServerOptions
        {
            ServiceId = form.BluetoothServiceId,
            WriteCharacteristicId = form.BluetoothWriteCharacteristicId,
            NotifyCharacteristicId = form.BluetoothNotifyCharacteristicId,
            ConnectTimeout = TimeSpan.FromSeconds(form.ConnectTimeoutSeconds),
            ReceiveTimeout = TimeSpan.FromSeconds(form.ReceiveTimeoutSeconds),
            SendTimeout = TimeSpan.FromSeconds(form.SendTimeoutSeconds)
        };

        var validation = options.Validate();
        if (!validation.IsSuccess)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var channelResult = _bluetoothPlatformService.CreateLocalServerChannel(options);
        if (!channelResult.IsSuccess || channelResult.ResultValue == null)
        {
            throw new InvalidOperationException(channelResult.Message);
        }

        var server = new ModbusRtuBluetoothServer(channelResult.ResultValue)
        {
            ConnectTimeout = TimeSpan.FromSeconds(form.ConnectTimeoutSeconds),
            ReceiveTimeout = TimeSpan.FromSeconds(form.ReceiveTimeoutSeconds),
            SendTimeout = TimeSpan.FromSeconds(form.SendTimeoutSeconds)
        };

        EventHandler<DataStoreEventArgs> written = (_, args) => eventSink?.Invoke("服务端写入事件", FormatModbusEvent(args));
        EventHandler<DataStoreEventArgs> read = (_, args) => eventSink?.Invoke("服务端读取事件", FormatModbusEvent(args));
        EventHandler<PacketTraceEventArgs> traced = (_, args) => packetSink?.Invoke(args);
        server.DataWritten += written;
        server.DataRead += read;
        server.PacketTraced += traced;

        return new SessionHandle(definition, $"本机蓝牙服务 / {form.BluetoothServiceId}", server.Listen, server.Shutdown, null, server.DataStore, null, server, () =>
        {
            server.DataWritten -= written;
            server.DataRead -= read;
            server.PacketTraced -= traced;
        });
    }

    private static SessionHandle CreateModbusRtuClientSession(CommunicationComponentDefinition definition, ConnectionFormViewModel form)
    {
        var client = new ModbusRtuClient(form.PortName, form.BaudRate, form.DataBits, form.ParseStopBits(), form.ParseParity(), form.ParseHandshake())
        {
            IsLongConnection = form.IsLongConnection,
            EnableAutoReconnect = form.EnableAutoReconnect,
            MaxReconnectAttempts = form.MaxReconnectAttempts,
            ReconnectDelay = TimeSpan.FromSeconds(form.ReconnectDelaySeconds)
        };

        return new SessionHandle(definition, $"{form.PortName} / {form.BaudRate}bps", client.Connect, client.Disconnect, new DeviceAddressAccessor(client), null, null, client, null);
    }

    private static SessionHandle CreateModbusTcpClientSession(CommunicationComponentDefinition definition, ConnectionFormViewModel form)
    {
        var client = new ModbusTcpClient(form.Ip, form.Port)
        {
            IsLongConnection = form.IsLongConnection,
            EnableAutoReconnect = form.EnableAutoReconnect,
            MaxReconnectAttempts = form.MaxReconnectAttempts,
            ReconnectDelay = TimeSpan.FromSeconds(form.ReconnectDelaySeconds)
        };

        return new SessionHandle(definition, $"{form.Ip}:{form.Port}", client.Connect, client.Disconnect, new DeviceAddressAccessor(client), null, null, client, null);
    }

    private static SessionHandle CreateSiemensClientSession(CommunicationComponentDefinition definition, ConnectionFormViewModel form)
    {
        var client = new SiemensClient(form.Ip, form.Port, form.SelectedSiemensVersion, (byte)form.Slot, (byte)form.Rack)
        {
            IsLongConnection = form.IsLongConnection,
            EnableAutoReconnect = form.EnableAutoReconnect,
            MaxReconnectAttempts = form.MaxReconnectAttempts,
            ReconnectDelay = TimeSpan.FromSeconds(form.ReconnectDelaySeconds)
        };

        return new SessionHandle(definition, $"{form.Ip}:{form.Port} / {form.SelectedSiemensVersion}", client.Connect, client.Disconnect, new DeviceAddressAccessor(client), null, null, client, null);
    }

    private static SessionHandle CreateModbusRtuServerSession(
        CommunicationComponentDefinition definition,
        ConnectionFormViewModel form,
        Action<string, string>? eventSink,
        Action<PacketTraceEventArgs>? packetSink)
    {
        var server = new ModbusRtuServer(form.PortName, form.BaudRate, form.DataBits, form.ParseStopBits(), form.ParseParity(), form.ParseHandshake());
        EventHandler<DataStoreEventArgs> written = (_, args) => eventSink?.Invoke("服务端写入事件", FormatModbusEvent(args));
        EventHandler<DataStoreEventArgs> read = (_, args) => eventSink?.Invoke("服务端读取事件", FormatModbusEvent(args));
        EventHandler<PacketTraceEventArgs> traced = (_, args) => packetSink?.Invoke(args);
        server.DataWritten += written;
        server.DataRead += read;
        server.PacketTraced += traced;

        return new SessionHandle(definition, $"{form.PortName} / {form.BaudRate}bps", server.Listen, server.Shutdown, null, server.DataStore, null, server, () =>
        {
            server.DataWritten -= written;
            server.DataRead -= read;
            server.PacketTraced -= traced;
        });
    }

    private static SessionHandle CreateModbusTcpServerSession(
        CommunicationComponentDefinition definition,
        ConnectionFormViewModel form,
        Action<string, string>? eventSink,
        Action<PacketTraceEventArgs>? packetSink)
    {
        var server = new ModbusTcpServer(form.Ip, form.Port)
        {
            MaxConnections = form.MaxConnections
        };
        EventHandler<DataStoreEventArgs> written = (_, args) => eventSink?.Invoke("服务端写入事件", FormatModbusEvent(args));
        EventHandler<DataStoreEventArgs> read = (_, args) => eventSink?.Invoke("服务端读取事件", FormatModbusEvent(args));
        EventHandler<PacketTraceEventArgs> traced = (_, args) => packetSink?.Invoke(args);
        server.DataWritten += written;
        server.DataRead += read;
        server.PacketTraced += traced;

        return new SessionHandle(definition, $"{form.Ip}:{form.Port}", server.Listen, server.Shutdown, null, server.DataStore, null, server, () =>
        {
            server.DataWritten -= written;
            server.DataRead -= read;
            server.PacketTraced -= traced;
        });
    }

    private static SessionHandle CreateS7ServerSession(
        CommunicationComponentDefinition definition,
        ConnectionFormViewModel form,
        Action<string, string>? eventSink,
        Action<PacketTraceEventArgs>? packetSink)
    {
        var server = new S7TcpServer(form.Ip, form.Port)
        {
            MaxConnections = form.MaxConnections
        };
        server.SetSiemensVersion(form.SelectedSiemensVersion);
        server.SetRackSlot((byte)form.Rack, (byte)form.Slot);
        server.CreateDataBlock(form.DefaultDbNumber, form.DefaultDbSize);

        EventHandler<S7DataStoreEventArgs> written = (_, args) => eventSink?.Invoke("DataWritten 事件", FormatS7Event(args));
        EventHandler<S7DataStoreEventArgs> read = (_, args) => eventSink?.Invoke("DataRead 事件", FormatS7Event(args));
        EventHandler<PacketTraceEventArgs> traced = (_, args) => packetSink?.Invoke(args);
        server.DataWritten += written;
        server.DataRead += read;
        server.PacketTraced += traced;

        return new SessionHandle(definition, $"{form.Ip}:{form.Port} / DB{form.DefaultDbNumber}", server.Listen, server.Shutdown, new S7AddressAccessor(server), null, server, server, () =>
        {
            server.DataWritten -= written;
            server.DataRead -= read;
            server.PacketTraced -= traced;
        });
    }

    private static string FormatModbusEvent(DataStoreEventArgs args)
    {
        var payload = args.Data.Option == DiscriminatedUnionOption.A
            ? string.Join(", ", args.Data.A.Select(value => value ? "1" : "0"))
            : string.Join(", ", args.Data.B);
        return $"{args.ModbusDataType} @ {args.StartAddress}: {payload}";
    }

    private static string FormatS7Event(S7DataStoreEventArgs args)
    {
        var prefix = args.Area == S7Area.DB ? $"DB{args.DbNumber}" : args.Area.ToString();
        var bytes = string.Join("-", args.Data.Select(value => value.ToString("X2")));
        return $"{prefix} @ {args.StartAddress}, Len={args.Length}, Data={bytes}";
    }

    private sealed class DeviceAddressAccessor(DeviceDataReaderWriterBase device) : IAddressAccessor
    {
        public OperationResult<object> Read(DataTypeEnums dataType, string address, int length) => length <= 1 ? device.Read(dataType, address) : device.Read(dataType, address, length);
        public OperationResult Write(DataTypeEnums dataType, string address, object value) => device.Write(dataType, address, value);
        public OperationResult WriteMany(DataTypeEnums dataType, string address, object[] values) => device.Write(dataType, address, values);
    }

    private sealed class S7AddressAccessor(S7TcpServer server) : IAddressAccessor
    {
        public OperationResult<object> Read(DataTypeEnums dataType, string address, int length) => length <= 1 ? server.Read(dataType, address) : server.Read(dataType, address, length);
        public OperationResult Write(DataTypeEnums dataType, string address, object value) => server.Write(dataType, address, value);
        public OperationResult WriteMany(DataTypeEnums dataType, string address, object[] values) => server.Write(dataType, address, values);
    }

    private sealed class SessionHandle(
        CommunicationComponentDefinition definition,
        string endpoint,
        Func<OperationResult> start,
        Func<OperationResult> stop,
        IAddressAccessor? addressAccessor,
        DataStore? dataStore,
        S7TcpServer? s7Server,
        IDisposable? disposable,
        Action? cleanup) : IDisposable
    {
        public CommunicationComponentDefinition Definition { get; } = definition;
        public string Endpoint { get; } = endpoint;
        public IAddressAccessor? AddressAccessor { get; } = addressAccessor;
        public DataStore? DataStore { get; } = dataStore;
        public S7TcpServer? S7Server { get; } = s7Server;
        public OperationResult Start() => start();
        public OperationResult Stop() => stop();

        public void Dispose()
        {
            cleanup?.Invoke();
            disposable?.Dispose();
        }
    }
}
