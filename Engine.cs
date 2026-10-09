using Hwmon.Backends;

namespace Hwmon;

public sealed class Engine : IDisposable
{
    private readonly List<ISensorBackend> _backends = [];
    private readonly TelemetryRules _rules = new();
    public Engine(bool gpuOnly = false, bool noRails = false, string? windowsHelper = null)
    {
#if WINDOWS
        _backends.Add(new LibreBackend(gpuOnly));
#endif
        _backends.Add(new NvmlBackend());
#if WINDOWS
        _backends.Add(new NvApiBackend());
        if (!noRails) _backends.Add(new RailsBackend());
        if (!gpuOnly) _backends.Add(new WindowsStorageBackend());
#else
        if (!gpuOnly) _backends.Add(new LinuxBackend());
        if (windowsHelper != null) _backends.Add(new WslHostBackend(windowsHelper, noRails));
#endif
    }
    public Snapshot Read(bool trackStatistics = true)
    {
        var rows = new List<SensorReading>();
        var errors = new Dictionary<string, string?>();
        foreach (var backend in _backends)
        {
            try {
                var readings = backend.Read(); var sampledAt = DateTimeOffset.Now;
                rows.AddRange(readings.Select(s => s.SampledAt.HasValue ? s : s with { SampledAt = sampledAt }));
                errors[backend.Name] = backend.InitializationError;
            }
            catch (Exception e) { errors[backend.Name] = e.Message; }
        }
        // NVML/NVAPI can enumerate in different orders. Join their IDs by PCI bus
        // when both provide it. LHM/worker enumeration is checked by GPU name.
        var allDevices = _backends.SelectMany(b => b.Devices).ToList();
        var anchors = allDevices.Where(d => d.Source == "NVML").ToList();
        var aliases = new Dictionary<(string Source, string Id), string>();
        foreach (var d in allDevices.Where(d => d.Id.StartsWith("nvidia/")))
        {
            var target = anchors.Where(a => d.Metadata?.GetValueOrDefault("uuid") is string uuid &&
                a.Metadata?.GetValueOrDefault("uuid") == uuid).ToList();
            if (target.Count == 0) target = anchors.Where(a => d.Metadata?.GetValueOrDefault("pciBus") is string bus &&
                a.Metadata?.GetValueOrDefault("pciBus") == bus).ToList();
            if (target.Count == 0) target = anchors.Where(a => CleanName(a.Name) == CleanName(d.Name)).ToList();
            if (target.Count == 1) aliases[(d.Source, d.Id)] = target[0].Id;
        }
        rows = rows.Select(s => {
            var source = s.Source.StartsWith("Windows host / ") ? "Windows host" :
                s.Source == "NVAPI PowerMonitor" || s.Source.StartsWith("NVAPI GetAllClocks") ? "NVAPI isolated worker" : s.Source;
            if (aliases.TryGetValue((source, s.DeviceId), out var id) && id != s.DeviceId)
                return s with { Id = id + s.Id[s.DeviceId.Length..], DeviceId = id };
            return s;
        }).ToList();
        var devices = allDevices.Select(d => aliases.TryGetValue((d.Source, d.Id), out var id) ? d with { Id = id } : d)
            .GroupBy(d => d.Id).Select(g => {
                var metadata = new Dictionary<string, string>();
                foreach (var d in g) if (d.Metadata != null) foreach (var item in d.Metadata) metadata[item.Key] = item.Value;
                return g.First() with { Source = string.Join(" + ", g.Select(d => d.Source).Distinct()), Metadata = metadata };
            }).ToList();
        foreach (var ambiguous in anchors.GroupBy(d => CleanName(d.Name)).Where(g => g.Count() > 1))
            errors["device identity"] = "Identical NVIDIA models: verify worker/library IDs against PCI metadata before combining channels.";
        rows = _rules.Apply(rows, trackStatistics);
        var derived = Derive(rows);
        rows.AddRange(_rules.Apply(derived.Select(s => s.SampledAt.HasValue ? s : s with { SampledAt = DateTimeOffset.Now }).ToList(), trackStatistics));
        return new(DateTimeOffset.Now, devices, rows, TelemetryRules.Diagnose(rows, errors), errors);
    }
    private static string CleanName(string name) => name.Replace("NVIDIA ", "").Replace(" GPU", "").Trim();
    public static List<SensorReading> Derive(List<SensorReading> sensors)
    {
        var output = new List<SensorReading>();
        foreach (var group in sensors.Where(s => s.DeviceId.StartsWith("nvidia/")).GroupBy(s => s.DeviceId))
        {
            var rows = group.ToList(); var device = rows[0].Device;
            void Aggregate(string id, string name, string unit, List<SensorReading> parts, int required, string note, bool any = false)
            {
                bool available = parts.Count == required && parts.All(s => s.Status == "ok" && s.Value.HasValue);
                output.Add(new($"{group.Key}/derived/{id}", group.Key, device, name, unit, "Derived",
                    available ? any ? (parts.Any(s => s.Value != 0) ? 1 : 0) : parts.Average(s => s.Value!.Value) : null,
                    available ? "ok" : "unavailable", note + (available ? "" : " One or more inputs are missing or invalid.")) {
                    SampledAt = parts.Where(s => s.SampledAt.HasValue).Select(s => s.SampledAt!.Value).DefaultIfEmpty(DateTimeOffset.Now).Max()
                });
            }
            List<SensorReading> Rails(params string[] suffixes) => suffixes.Select(x => rows.FirstOrDefault(s => s.Id == $"{group.Key}/rails/{x}"))
                .Where(s => s != null).Cast<SensorReading>().ToList();
            bool verifiedLaptop = device.Contains("RTX 4070 Laptop", StringComparison.OrdinalIgnoreCase);
            Aggregate("rail-voltage-average", "GPU Rail Voltages (avg)", "V", verifiedLaptop ? Rails("3.voltage", "5.voltage") : [], 2,
                "Mean of FBVDD and HVPWR voltages. Formula verified against 11 active HWiNFO rows on this GPU.");
            Aggregate("rail-power-average", "GPU Rail Powers (avg)", "W", verifiedLaptop ? Rails("1.power", "3.power", "5.power", "10.power", "12.power", "18.power", "19.power") : [], 7,
                "Mean of seven rail powers: channels 1,3,5,10,12,18,19. Verified against HWiNFO on this RTX 4070 Laptop only.");
            var flags = rows.Where(s => s.Source == "NVAPI" && s.Name.StartsWith("Performance Limit - ")).ToList();
            Aggregate("performance-limiter-any", "GPU Performance Limiters (any active)", "bool", flags, 6,
                "Any of six verified NVAPI limit flags; unsupported flags prevent an all-clear result.", true);
            var d3d = rows.Where(s => s.Source == "LibreHardwareMonitor" && s.Name.StartsWith("D3D ") && s.Unit == "%").ToList();
            if (d3d.Count > 0) Aggregate("d3d-average", "GPU D3D engine average (all exposed engines)", "%", d3d, d3d.Count,
                "Mean of all exposed D3D engine channels. HWiNFO may average a different subset; this is not an exact equivalence claim.");
            var gen = rows.FirstOrDefault(s => s.Id.EndsWith("/nvml/pcie.generation"));
            if (gen != null)
            {
                double[] rates = [0, 2.5, 5, 8, 16, 32, 64];
                bool valid = gen.Status == "ok" && gen.Value is >= 1 and <= 6;
                output.Add(new($"{group.Key}/derived/pcie-link-speed", group.Key, device, "PCIe Link Speed", "GT/s", "Derived",
                    valid ? rates[(int)gen.Value!.Value] : null, valid ? "ok" : gen.Status == "ok" ? "unavailable" : gen.Status,
                    "Transfer rate per lane from PCIe generation; distinct from measured Rx/Tx throughput.") { SampledAt = gen.SampledAt });
            }
        }
        return output;
    }
    public void Dispose() { foreach (var backend in Enumerable.Reverse(_backends)) try { backend.Dispose(); } catch { } }
}
