using System.Text.Json;

namespace Prognode.Licensing;

public sealed class CoreCloudLicenseStateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();

    public CoreCloudLicenseStateStore(string dataRoot)
    {
        var folder = Path.Combine(dataRoot, "cloud-license");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "state.json");
    }

    public CoreCloudLicenseStatus Load(bool configured)
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return new(configured, "NOT_SYNCED", null, null, null, null, null, null);
            try
            {
                var value = JsonSerializer.Deserialize<CoreCloudLicenseStatus>(File.ReadAllText(_path), Json);
                return value is null ? new(configured, "NOT_SYNCED", null, null, null, null, null, null) : value with { Configured = configured };
            }
            catch
            {
                return new(configured, "STATE_INVALID", null, null, null, null, null, "Cloud license cache could not be read.");
            }
        }
    }

    public void Save(CoreCloudLicenseStatus value)
    {
        lock (_gate)
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, _path, true);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }
}
