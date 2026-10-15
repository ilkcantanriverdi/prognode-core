using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Bacnet;

/// <summary>
/// Reads the Present_Value of BACnet objects, up to 20 per ReadPropertyMultiple. Each object carries its
/// own result, so a missing object makes only its own Tag Bad. The value is decoded by the type the device
/// sends; a Tag whose data type does not fit it is Bad and names that type.
/// </summary>
public sealed class BacnetTagReader(BacnetClient client) : ITagReader
{
    public const string Protocol = "BACnet/IP";
    private const int TimeoutMs = 3000;

    public BacnetTagReader() : this(new BacnetClient()) { }

    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(Protocol, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(DeviceDefinition device, IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
            throw new InvalidOperationException($"Device '{device.Name}' has incomplete BACnet/IP settings.");
        var result = new List<TagValueSnapshot>();
        var planned = new List<(TagDefinition Tag, BacnetObjectAddress Object)>();
        foreach (var tag in tags.Where(t => t.Enabled))
        {
            try { planned.Add((tag, BacnetObjectAddress.Parse(tag.Address, tag.DataType))); }
            catch (ArgumentException ex) { result.Add(Bad(device, tag, ex.Message)); }
        }
        foreach (var batch in planned.Chunk(BacnetClient.MaxObjectsPerRequest))
        {
            BacnetValue[] values;
            try
            {
                values = await client.ReadPresentValuesAsync(device.Host!, device.Port!.Value, batch.Select(x => x.Object).ToArray(), TimeoutMs, cancellationToken);
            }
            catch (BacnetRefusedException ex)
            {
                // The device answered but refused this request as a whole.
                result.AddRange(batch.Select(x => Bad(device, x.Tag, ex.Message)));
                continue;
            }
            for (var i = 0; i < batch.Length; i++)
            {
                var (tag, obj) = batch[i];
                var value = values[i];
                if (value.ErrorClass is { } errorClass)
                {
                    result.Add(Bad(device, tag, $"{obj}: {BacnetClient.ErrorMessage(errorClass, value.ErrorCode ?? 0)}"));
                    continue;
                }
                try { result.Add(Good(device, tag, Decode(tag.DataType, obj, value))); }
                catch (ArgumentException ex) { result.Add(Bad(device, tag, ex.Message)); }
            }
        }
        return result;
    }

    internal static string TypeName(int tag) => tag switch
    {
        0 => "NULL (relinquished)", 1 => "BOOLEAN", 2 => "UNSIGNED", 3 => "SIGNED", 4 => "REAL", 5 => "DOUBLE", 9 => "ENUMERATED",
        _ => $"application type {tag}",
    };

    internal static double Decode(TagDataType type, BacnetObjectAddress obj, BacnetValue value)
    {
        if (value.Tag == 0) throw new ArgumentException($"{obj} Present_Value is NULL.");
        var v = value.Value;
        bool fits = type switch
        {
            TagDataType.Bool => value.Tag == 1 || value.Tag == 9 && obj.Type.Binary && v is 0 or 1,
            TagDataType.Float32 => value.Tag == 4,
            TagDataType.Float64 => value.Tag is 4 or 5,
            TagDataType.Int16 or TagDataType.Int32 or TagDataType.Int64 => value.Tag is 2 or 3 || value.Tag == 9 && !obj.Type.Binary,
            TagDataType.Word or TagDataType.UInt16 or TagDataType.UInt32 or TagDataType.UInt64 => value.Tag == 2 || value.Tag == 9 && !obj.Type.Binary || value.Tag == 3 && v >= 0,
            _ => false,
        };
        if (!fits)
            throw new ArgumentException($"{obj} Present_Value is {TypeName(value.Tag)}; set the Tag data type to {Suggest(value.Tag)}.");
        if (value.Tag is 2 or 3 or 9 && !TagDataRange.Fits(v, type))
            throw new ArgumentException($"{obj} Present_Value {v} does not fit {type}.");
        return v;
    }

    private static string Suggest(int tag) => tag switch
    {
        1 => "BOOL", 4 => "FLOAT32", 5 => "FLOAT64", 2 => "UINT32", 3 => "INT32", 9 => "UINT16 (state number)", _ => "a matching type",
    };

    private static TagValueSnapshot Good(DeviceDefinition device, TagDefinition tag, double raw) =>
        new(tag.Id, device.Id, raw, EngineeringValue.From(raw, tag), TagQuality.Good, DateTimeOffset.UtcNow, Protocol, null);

    private static TagValueSnapshot Bad(DeviceDefinition device, TagDefinition tag, string error) =>
        new(tag.Id, device.Id, null, null, TagQuality.Bad, DateTimeOffset.UtcNow, Protocol, error);
}

/// <summary>Checks BACnet object addresses against the data type (binary objects are BOOL).</summary>
public sealed class BacnetTagValidator : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(BacnetTagReader.Protocol, StringComparison.OrdinalIgnoreCase);

    public void Validate(TagDefinition candidate, IReadOnlyList<TagDefinition> existingTags)
    {
        if (candidate.BitIndex is not null) throw new ArgumentException("BACnet Tags read a whole Present_Value. Leave BitIndex empty.");
        BacnetObjectAddress.Parse(candidate.Address, candidate.DataType);
    }
}

/// <summary>A recent good poll counts; otherwise the device object's name is read (any answer counts).</summary>
public sealed class BacnetHealthProbe(BacnetClient client) : IDeviceHealthProbe
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(BacnetTagReader.Protocol, StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
            return new(false, "Device IP configuration is incomplete.", 0);
        if (client.HasRecentSuccess(device.Host, device.Port.Value, TimeSpan.FromSeconds(30)))
            return new(true, "Polling connection is healthy.", 0);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var name = await client.PingAsync(device.Host, device.Port.Value, 1500, cancellationToken);
            return new(true, string.IsNullOrWhiteSpace(name) ? "BACnet device answered." : $"BACnet device '{name}' answered.", Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
        catch (IOException ex)
        {
            return new(false, ex.Message, Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
    }
}
