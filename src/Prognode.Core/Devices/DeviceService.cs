using Prognode.Contracts.Devices;
using Prognode.Contracts.Licensing;
using Prognode.Core.Tags;

namespace Prognode.Core.Devices;

internal static class EngineeringNameGate
{
    internal static readonly SemaphoreSlim Write = new(1,1);
}

public sealed class DeviceService(IDeviceRepository repository, IDeviceCapacityPolicy capacityPolicy,
    ITagRepository tagRepository)
{
    private readonly SemaphoreSlim _createGate = new(1, 1);
    public Task<IReadOnlyList<DeviceDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public Task<DeviceDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        repository.GetByIdAsync(id, cancellationToken);

    public Task<int> CountAsync(
        CancellationToken cancellationToken = default) =>
        repository.CountAsync(cancellationToken);

    public async Task<DeviceDefinition> CreateMockAsync(
        string? name,
        CancellationToken cancellationToken = default)
    {
        var cleanName = ValidateName(name);
        var now = DateTimeOffset.UtcNow;

        var device = new DeviceDefinition(
            Id: Guid.NewGuid(),
            Name: cleanName,
            Protocol: "Mock",
            Status: "Simulator",
            Host: null,
            Port: null,
            UnitId: null,
            PollIntervalMs: 1000,
            CreatedAt: now,
            UpdatedAt: now
        );

        await AddWithinCapacityAsync(device, cancellationToken);
        return device;
    }

    public async Task<DeviceDefinition> CreateModbusTcpAsync(
        string? name,
        string? host,
        int port,
        int unitId,
        int pollIntervalMs,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var device = BuildModbusDevice(
            Guid.NewGuid(),
            createdAt: now,
            name,
            host,
            port,
            unitId,
            pollIntervalMs);

        await AddWithinCapacityAsync(device, cancellationToken);
        return device;
    }

    // UnitId encodes rack * 32 + slot; rack=0 slot=1 -> 1. No schema migration.
    public async Task<DeviceDefinition> CreateS7TcpAsync(string? name,string? host,int port,int rack,int slot,int pollIntervalMs,CancellationToken ct=default)
    {
        if(rack is < 0 or > 7 || slot is < 0 or > 31) throw new ArgumentException("Rack 0..7 and slot 0..31 required.");
        var now=DateTimeOffset.UtcNow;
        var item=BuildModbusDevice(Guid.NewGuid(),now,name,host,port,rack*32+slot,pollIntervalMs) with {Protocol="Siemens S7 TCP"};
        await AddWithinCapacityAsync(item,ct);return item;
    }

    public async Task<DeviceDefinition> CreateMqttAsync(
        string? name, string? host, int port, int pollIntervalMs, CancellationToken ct = default)
    {
        ValidateMqttBroker(host);
        var now = DateTimeOffset.UtcNow;
        var item = BuildModbusDevice(Guid.NewGuid(), now, name, host, port, 0, pollIntervalMs)
            with { Protocol = "MQTT", UnitId = null };
        await AddWithinCapacityAsync(item, ct);
        return item;
    }

    public async Task<DeviceDefinition> CreateOpcUaAsync(
        string? name, string? endpointUrl, int pollIntervalMs, CancellationToken ct = default)
    {
        var uri = ValidateOpcUaEndpoint(endpointUrl);
        var now = DateTimeOffset.UtcNow;
        var item = BuildModbusDevice(Guid.NewGuid(), now, name, endpointUrl,
            uri.Port, 0, pollIntervalMs) with { Protocol = "OPC UA", UnitId = null };
        await AddWithinCapacityAsync(item, ct);
        return item;
    }

    public async Task<DeviceDefinition> UpdateAsync(
        Guid id,
        string? name,
        string? host,
        int? port,
        int? unitId,
        int? pollIntervalMs,
        CancellationToken cancellationToken = default)
    {
        await EngineeringNameGate.Write.WaitAsync(cancellationToken);
        try
        {
        var existing = await repository.GetByIdAsync(id, cancellationToken);

        if (existing is null)
            throw new KeyNotFoundException("Device was not found.");

        DeviceDefinition updated;

        if (string.Equals(existing.Protocol, "Modbus TCP", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(existing.Protocol, "Siemens S7 TCP", StringComparison.OrdinalIgnoreCase))
        {
            updated = BuildModbusDevice(
                existing.Id,
                existing.CreatedAt,
                name,
                host,
                port ?? existing.Port ?? 502,
                unitId ?? existing.UnitId ?? 1,
                pollIntervalMs ?? existing.PollIntervalMs) with { Protocol = existing.Protocol };
        }
        else if (string.Equals(existing.Protocol, "MQTT", StringComparison.OrdinalIgnoreCase))
        {
            var broker = host ?? existing.Host;
            ValidateMqttBroker(broker);
            updated = BuildModbusDevice(existing.Id, existing.CreatedAt, name, broker,
                port ?? existing.Port ?? 1883, 0, pollIntervalMs ?? existing.PollIntervalMs)
                with { Protocol = "MQTT", UnitId = null };
        }
        else if (string.Equals(existing.Protocol, "OPC UA", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = host ?? existing.Host;
            var uri = ValidateOpcUaEndpoint(endpoint);
            if (port is not null && port != uri.Port)
                throw new ArgumentException("OPC UA port is part of the endpoint URL.");
            updated = BuildModbusDevice(existing.Id, existing.CreatedAt, name, endpoint,
                uri.Port, 0, pollIntervalMs ?? existing.PollIntervalMs)
                with { Protocol = "OPC UA", UnitId = null };
        }
        else
        {
            updated = existing with
            {
                Name = ValidateName(name),
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        await EnsureUniqueNameAsync(updated.Name,id,cancellationToken);
        await repository.UpdateAsync(updated, cancellationToken);
        return updated;
        }
        finally { EngineeringNameGate.Write.Release(); }
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetByIdAsync(id, cancellationToken);

        if (existing is null)
            return false;

        await repository.DeleteAsync(id, cancellationToken);
        return true;
    }

    private async Task AddWithinCapacityAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        await _createGate.WaitAsync(cancellationToken);
        try
        {
            await EngineeringNameGate.Write.WaitAsync(cancellationToken);
            try
            {
                await EnsureUniqueNameAsync(device.Name,null,cancellationToken);
                var currentCount = await repository.CountAsync(cancellationToken);
                if (!capacityPolicy.AllowsDeviceCount(currentCount + 1))
                    throw new DeviceCapacityExceededException(currentCount, capacityPolicy.DeviceLimit.Value);

                await repository.AddAsync(device, cancellationToken);
            }
            finally { EngineeringNameGate.Write.Release(); }
        }
        finally
        {
            _createGate.Release();
        }
    }

    private async Task EnsureUniqueNameAsync(string name,Guid? selfId,CancellationToken ct)
    {
        if((await repository.GetAllAsync(ct)).Any(d=>d.Id!=selfId &&
            string.Equals(d.Name,name,StringComparison.OrdinalIgnoreCase)) ||
           (await tagRepository.GetAllAsync(ct)).Any(t=>
            string.Equals(t.Name,name,StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Name '{name}' is already used by a Device or Tag.");
    }

    private static DeviceDefinition BuildModbusDevice(
        Guid id,
        DateTimeOffset createdAt,
        string? name,
        string? host,
        int port,
        int unitId,
        int pollIntervalMs)
    {
        var cleanName = ValidateName(name);
        var cleanHost = (host ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(cleanHost))
            throw new ArgumentException("Host / IP is required.");

        if (port is < 1 or > 65535)
            throw new ArgumentException("Port must be between 1 and 65535.");

        if (unitId is < 0 or > 255)
            throw new ArgumentException("Unit ID must be between 0 and 255.");

        if (pollIntervalMs < 100)
            throw new ArgumentException("Poll interval must be at least 100 ms.");

        if (pollIntervalMs > 3_600_000)
            throw new ArgumentException("Poll interval cannot exceed 1 hour.");

        return new DeviceDefinition(
            Id: id,
            Name: cleanName,
            Protocol: "Modbus TCP",
            Status: "Configured",
            Host: cleanHost,
            Port: port,
            UnitId: unitId,
            PollIntervalMs: pollIntervalMs,
            CreatedAt: createdAt,
            UpdatedAt: DateTimeOffset.UtcNow
        );
    }

    private static string ValidateName(string? name)
    {
        var cleanName = (name ?? string.Empty).Trim();

        if (cleanName.Length < 1)
            throw new ArgumentException("Device name is required.");

        if (cleanName.Length > 80)
            throw new ArgumentException("Device name cannot exceed 80 characters.");

        return cleanName;
    }

    private static void ValidateMqttBroker(string? host)
    {
        var value = (host ?? string.Empty).Trim();
        var source = value.Contains("://", StringComparison.Ordinal) ? value : $"mqtts://{value}";
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("mqtt" or "mqtts") ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.IsDefaultPort)
            throw new ArgumentException("MQTT host must be a broker name or mqtt:// / mqtts:// URL without credentials, port or path.");
    }

    private static Uri ValidateOpcUaEndpoint(string? endpointUrl)
    {
        if (!Uri.TryCreate((endpointUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "opc.tcp" || string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port is < 1 or > 65535 || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("OPC UA endpoint must be an opc.tcp://host:port/path URL without credentials or query.");
        return uri;
    }
}
