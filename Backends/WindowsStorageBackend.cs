using System.Globalization;
using System.Management;

namespace Hwmon.Backends;

/// <summary>Windows disk inventory and OS performance counters, independent of a sensor driver.</summary>
public sealed class WindowsStorageBackend : ISensorBackend
{
    public string Name => "Windows storage";
    public string? InitializationError { get; private set; }
    private readonly List<DeviceInfo> _devices = [];
    public IReadOnlyList<DeviceInfo> Devices => _devices;
    private string? _reliabilityError;
    private List<SensorReading> _reliability = [];
    private DateTimeOffset _reliabilityTime;
    public WindowsStorageBackend()
    {
        try {
            using var query = new ManagementObjectSearcher("SELECT Index,Model,FirmwareRevision,Size,Status,InterfaceType FROM Win32_DiskDrive");
            using var result = query.Get();
            foreach (ManagementObject disk in result) using (disk) {
                var index = Convert.ToString(disk["Index"], CultureInfo.InvariantCulture)!;
                var metadata = new Dictionary<string, string>();
                foreach (var key in new[] { "FirmwareRevision", "Size", "Status", "InterfaceType" })
                    metadata[key] = Convert.ToString(disk[key], CultureInfo.InvariantCulture) ?? "unknown";
                _devices.Add(new("windows/disk/" + index, Convert.ToString(disk["Model"])?.Trim() ?? "Disk " + index, "storage", Name, metadata));
            }
        } catch (Exception e) { InitializationError = e.Message; }
    }
    public List<SensorReading> Read()
    {
        var rows = new List<SensorReading>();
        try {
            using var query = new ManagementObjectSearcher("SELECT Name,DiskReadBytesPerSec,DiskWriteBytesPerSec,DiskReadsPerSec,DiskWritesPerSec,CurrentDiskQueueLength FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
            using var result = query.Get();
            foreach (ManagementObject counter in result) using (counter) {
                var instance = Convert.ToString(counter["Name"]) ?? "";
                var index = instance.Split(' ')[0];
                var device = _devices.FirstOrDefault(d => d.Id == "windows/disk/" + index);
                if (device == null) continue;
                foreach (var (field, name, unit) in new[] {
                    ("DiskReadBytesPerSec", "Disk read rate", "B/s"), ("DiskWriteBytesPerSec", "Disk write rate", "B/s"),
                    ("DiskReadsPerSec", "Disk read operations", "ops/s"), ("DiskWritesPerSec", "Disk write operations", "ops/s"),
                    ("CurrentDiskQueueLength", "Disk queue length", "requests") })
                    rows.Add(new($"{device.Id}/{field}", device.Id, device.Name, name, unit, Name,
                        counter[field] == null ? null : Convert.ToDouble(counter[field], CultureInfo.InvariantCulture),
                        counter[field] == null ? "unavailable" : "ok", "OS physical-disk performance counter; not a firmware sensor."));
            }
        } catch (Exception e) {
            foreach (var device in _devices) rows.Add(new(device.Id + "/performance", device.Id, device.Name, "Disk performance counters", "", Name, null, "error", e.Message));
        }
        // Reliability/SMART counters are optional and often denied without elevation.
        // Poll slowly: their timestamps are separate from the faster OS counters.
        if (_reliabilityError == null && (DateTimeOffset.Now - _reliabilityTime).TotalSeconds > 30)
        {
            _reliabilityTime = DateTimeOffset.Now; _reliability = [];
            try {
                using var query = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Storage", "SELECT DeviceId,Temperature,TemperatureMax,Wear,PowerOnHours,ReadErrorsTotal,WriteErrorsTotal FROM MSFT_StorageReliabilityCounter");
                using var result = query.Get();
                foreach (ManagementObject counter in result) using (counter) {
                    var id = "windows/disk/" + Convert.ToString(counter["DeviceId"], CultureInfo.InvariantCulture);
                    var device = _devices.FirstOrDefault(d => d.Id == id); if (device == null) continue;
                    foreach (var (field, unit) in new[] { ("Temperature", "°C"), ("TemperatureMax", "°C"), ("Wear", "%"), ("PowerOnHours", "h"), ("ReadErrorsTotal", "count"), ("WriteErrorsTotal", "count") })
                        _reliability.Add(new(id + "/reliability/" + field, id, device.Name, "Disk " + field, unit, Name,
                            counter[field] == null ? null : Convert.ToDouble(counter[field], CultureInfo.InvariantCulture),
                            counter[field] == null ? "unsupported" : "ok", $"Windows storage-reliability provider; sampled {_reliabilityTime:O}. Refreshed at most every 30s.") { SampledAt = _reliabilityTime });
                }
                if (_reliability.Count == 0) _reliabilityError = "Windows provider exposes no storage reliability counters in this session.";
            } catch (Exception e) { _reliabilityError = e.Message; }
        }
        if (_reliabilityError != null)
            foreach (var device in _devices) rows.Add(new(device.Id + "/reliability/status", device.Id, device.Name,
                "Disk reliability/SMART provider", "", Name, null,
                _reliabilityError.Contains("denied", StringComparison.OrdinalIgnoreCase) ? "permission" : "unavailable", _reliabilityError));
        else rows.AddRange(_reliability.Select(s => s with { Status = (DateTimeOffset.Now - _reliabilityTime).TotalSeconds < 1 ? s.Status : s.Status == "ok" ? "cached" : s.Status }));
        return rows;
    }
    public void Dispose() { }
}
