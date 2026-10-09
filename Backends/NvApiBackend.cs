using System.Globalization;
using System.Reflection;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;

namespace Hwmon.Backends;

/// <summary>Read-only NVIDIA diagnostics through the installed NVIDIA driver.</summary>
public sealed class NvApiBackend : ISensorBackend
{
    private readonly List<(PhysicalGPU Gpu, DeviceInfo Device)> _gpus = [];
    private bool _initialized;
    private bool _disposed;
    public string Name => "NVAPI";
    public IReadOnlyList<DeviceInfo> Devices => _gpus.Select(x => x.Device).ToArray();
    public string? InitializationError { get; private set; }

    // Read the wrapper's already-marshaled structure, never an invented native ABI.
    // The NuGet version is pinned; if this field changes we report the mask unavailable.
    private static readonly FieldInfo? SupportedLimitField = typeof(PrivatePerformanceInfoV1)
        .GetField("_SupportedLimits", BindingFlags.Instance | BindingFlags.NonPublic);

    public NvApiBackend()
    {
        try
        {
            NVIDIA.Initialize();
            _initialized = true;
            var devices = PhysicalGPU.GetPhysicalGPUs();
            for (var index = 0; index < devices.Length; index++)
            {
                var gpu = devices[index];
                var metadata = new Dictionary<string, string>();
                Metadata(metadata, "pciBus", () => gpu.BusInformation.BusId);
                Metadata(metadata, "pciSlot", () => gpu.BusInformation.BusSlot);
                Metadata(metadata, "pciDeviceId", () => $"0x{gpu.BusInformation.PCIIdentifiers.ExternalDeviceId:X4}");
                Metadata(metadata, "pciVendorId", () => $"0x{gpu.BusInformation.PCIIdentifiers.VendorId:X4}");
                Metadata(metadata, "vbios", () => gpu.Bios.VersionString);
                Metadata(metadata, "driverVersion", () => NVIDIA.DriverVersion);
                Metadata(metadata, "driverBranch", () => NVIDIA.DriverBranchVersion);
                Metadata(metadata, "memoryType", () => gpu.MemoryInformation.RAMType);
                Metadata(metadata, "memoryBusWidthBits", () => gpu.MemoryInformation.RAMBusWidth);
                Metadata(metadata, "systemType", () => gpu.SystemType);
                var name = "NVIDIA GPU " + index;
                try { name = gpu.FullName; } catch { /* Enumeration still succeeded. */ }
                _gpus.Add((gpu, new DeviceInfo(DeviceIdentity.Nvidia((uint)index), name, "gpu", Name, metadata)));
            }
        }
        catch (Exception ex) { InitializationError = Describe(ex); }
    }

    public List<SensorReading> Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var readings = new List<SensorReading>();
        foreach (var (gpu, device) in _gpus)
        {
            ReadPerformance(readings, gpu, device);
            ReadClocks(readings, gpu, device);
            ReadThermalPolicies(readings, gpu, device);
            ReadPowerPolicies(readings, gpu, device);
            ReadPowerTopology(readings, gpu, device);
            ReadFans(readings, gpu, device);
            Scalar(readings, device, "pcie/lanes", "GPU PCIe Link Width", "lanes", () => gpu.BusInformation.CurrentPCIeLanes);
            Scalar(readings, device, "performance/state", "GPU Performance State", "P-state", () => (int)GPUApi.GetCurrentPerformanceState(gpu.Handle));
        }
        return readings;
    }

    private void ReadPerformance(List<SensorReading> output, PhysicalGPU gpu, DeviceInfo device)
    {
        uint? supported = null;
        Exception? infoError = null;
        try
        {
            var info = GPUApi.PerformancePoliciesGetInfo(gpu.Handle);
            if (SupportedLimitField?.GetValue(info) is PerformanceLimit flags) supported = (uint)flags;
            else infoError = new NotSupportedException("The pinned NVAPI wrapper does not expose its supported-limit mask.");
        }
        catch (Exception ex) { infoError = ex; }
        Add(output, device, "limits/supported-mask", "GPU Performance Limit Support Mask", "bitmask", supported,
            infoError is null ? "ok" : FailureStatus(infoError), infoError is null ? "Raw NVAPI policy capability flags." : Describe(infoError));

        uint? active = null;
        Exception? statusError = null;
        try { active = (uint)gpu.PerformanceControl.CurrentActiveLimit; }
        catch (Exception ex) { statusError = ex; }
        Add(output, device, "limits/active-mask", "GPU Performance Limit Mask", "bitmask", active,
            statusError is null ? "ok" : FailureStatus(statusError), statusError is null ? "Raw active NVAPI policy flags; preserved even when individual flags are unsupported." : Describe(statusError));

        // Bit 4/8 names are verified in arcnmx/nvapi-rs sys/src/gpu/power.rs.
        // NvAPIWrapper's older enum calls 4 VoltageLimit and 8 Unknown8.
        var flagsToRead = new (uint Bit, string Id, string Label)[]
        {
            (1, "power", "Power"), (2, "thermal", "Thermal"),
            (4, "reliability-voltage", "Reliability Voltage"),
            (8, "operating-voltage", "Max Operating Voltage"),
            (16, "utilization", "Utilization"), (32, "sli-gpuboost-sync", "SLI GPUBoost Sync")
        };
        foreach (var flag in flagsToRead)
        {
            var status = "ok";
            double? value = null;
            string? note = $"NVAPI policy bit 0x{flag.Bit:X}; 1 means active, 0 inactive.";
            if (statusError is not null) { status = FailureStatus(statusError); note = Describe(statusError); }
            else if (infoError is not null) { status = FailureStatus(infoError); note = "Cannot establish this flag's support: " + Describe(infoError); }
            else if ((supported.GetValueOrDefault() & flag.Bit) == 0)
            { status = "unsupported"; note = "Driver capability mask does not advertise this flag; absence is not recorded as false."; }
            else if (active.HasValue) value = (active.Value & flag.Bit) != 0 ? 1 : 0;
            Add(output, device, "limits/" + flag.Id, "Performance Limit - " + flag.Label, "bool", value, status, note);
        }
        // Bit 32 is verified against HWiNFO 8.16's read-only policy decoding:
        // function 0x1402c4418, limits field at buffer+16, BT by label index.
        Add(output, device, "limits/unknown-mask", "GPU Performance Limit Unknown Bits", "bitmask",
            active.HasValue ? active.Value & ~63u : null, statusError is null ? "ok" : FailureStatus(statusError),
            statusError is null ? "Active bits outside the six verified flags." : Describe(statusError));

        try
        {
            var decrease = (uint)gpu.PerformanceControl.CurrentPerformanceDecreaseReason;
            Add(output, device, "decrease/mask", "GPU Performance Decrease Reason Mask", "bitmask", decrease,
                note: "Legacy NVAPI decrease reasons; distinct from GPU Boost performance limits.");
            foreach (var reason in new (uint Bit, string Id, string Label)[]
            {
                (1, "thermal", "Thermal Protection"), (2, "power", "Power Control"),
                (4, "ac-battery", "AC/Battery Event"), (8, "api", "API Triggered"),
                (16, "insufficient-power", "Insufficient Power"), (0x80000000, "unknown", "Unknown")
            })
                Add(output, device, "decrease/" + reason.Id, "Performance Decrease - " + reason.Label, "bool",
                    (decrease & reason.Bit) != 0 ? 1 : 0, note: "Legacy NVAPI decrease reason; 1 active, 0 inactive.");
        }
        catch (Exception ex) { Failure(output, device, "decrease/mask", "GPU Performance Decrease Reason Mask", "bitmask", ex); }
    }

    private void ReadClocks(List<SensorReading> output, PhysicalGPU gpu, DeviceInfo device)
    {
        var domains = new (PublicClockDomain Domain, string Id, string Name)[]
        {
            (PublicClockDomain.Graphics, "graphics", "GPU Clock"),
            (PublicClockDomain.Memory, "memory", "GPU Memory Clock (NVAPI raw scale)"),
            (PublicClockDomain.Processor, "processor", "GPU Processor Clock"),
            (PublicClockDomain.Video, "video", "GPU Video Clock")
        };
        try
        {
            var clocks = gpu.CurrentClockFrequencies;
            foreach (var domain in domains)
            {
                if (clocks.Clocks.TryGetValue(domain.Domain, out var clock) && clock.IsPresent)
                    Add(output, device, "clock/" + domain.Id, domain.Name, "MHz", clock.Frequency / 1000.0,
                        note: domain.Domain == PublicClockDomain.Memory ? "NVAPI reporting scale; do not equate this directly to effective DDR transfer rate or HWiNFO memory-clock scale." : null,
                        rawValue: clock.Frequency);
                else Add(output, device, "clock/" + domain.Id, domain.Name, "MHz", null, "unsupported", "Clock domain is not present in the driver's response.");
            }
        }
        catch (Exception ex)
        {
            foreach (var domain in domains) Failure(output, device, "clock/" + domain.Id, domain.Name, "MHz", ex);
        }
        // Crossbar is queried by the isolated worker through verified GetAllClocks v2.
    }

    private void ReadThermalPolicies(List<SensorReading> output, PhysicalGPU gpu, DeviceInfo device)
    {
        try
        {
            var policies = gpu.PerformanceControl.ThermalLimitPolicies.ToArray();
            for (var i = 0; i < policies.Length; i++)
                Add(output, device, $"thermal/target/{i}", $"GPU Thermal Target {i}", "°C", policies[i].TargetTemperature,
                    note: $"NVAPI thermal-policy target; controller {policies[i].Controller}; state {policies[i].PerformanceStateId}.");
            if (policies.Length == 0) Add(output, device, "thermal/target/0", "GPU Thermal Target 0", "°C", null, "unsupported", "Driver returned no thermal policies.");
        }
        catch (Exception ex) { Failure(output, device, "thermal/target/0", "GPU Thermal Target 0", "°C", ex); }
        try
        {
            var policies = gpu.PerformanceControl.ThermalLimitInformation.ToArray();
            for (var i = 0; i < policies.Length; i++)
            {
                var prefix = $"thermal/range/{i}/";
                var note = $"NVAPI allowable thermal-target settings; controller {policies[i].Controller}; not a measured temperature.";
                Add(output, device, prefix + "minimum", $"GPU Thermal Target Minimum {i}", "°C", policies[i].MinimumTemperature, note: note);
                Add(output, device, prefix + "default", $"GPU Thermal Target Default {i}", "°C", policies[i].DefaultTemperature, note: note);
                Add(output, device, prefix + "maximum", $"GPU Thermal Target Maximum {i}", "°C", policies[i].MaximumTemperature, note: note);
            }
            if (policies.Length == 0) Add(output, device, "thermal/range/0", "GPU Thermal Target Range", "°C", null, "unsupported", "Driver returned no thermal-policy range.");
        }
        catch (Exception ex) { Failure(output, device, "thermal/range/0", "GPU Thermal Target Range", "°C", ex); }
    }

    private void ReadPowerPolicies(List<SensorReading> output, PhysicalGPU gpu, DeviceInfo device)
    {
        const string unitNote = "Relative percentage of the NVAPI reference power target; PCM / 1000. This is not watts.";
        try
        {
            var policies = gpu.PerformanceControl.PowerLimitPolicies.ToArray();
            for (var i = 0; i < policies.Length; i++)
                Add(output, device, $"power-policy/target/{i}", $"GPU Power Target {i}", "%", policies[i].PowerTargetInPercent,
                    note: unitNote + " State " + policies[i].PerformanceStateId + ".", rawValue: policies[i].PowerTargetInPCM);
            if (policies.Length == 0) Add(output, device, "power-policy/target/0", "GPU Power Target 0", "%", null, "unsupported", "Driver returned no power policies.");
        }
        catch (Exception ex) { Failure(output, device, "power-policy/target/0", "GPU Power Target 0", "%", ex); }
        try
        {
            var policies = gpu.PerformanceControl.PowerLimitInformation.ToArray();
            for (var i = 0; i < policies.Length; i++)
            {
                var prefix = $"power-policy/range/{i}/";
                var note = unitNote + " Allowable setting, not measured consumption. State " + policies[i].PerformanceStateId + ".";
                Add(output, device, prefix + "minimum", $"GPU Power Target Minimum {i}", "%", policies[i].MinimumPowerInPercent, note: note, rawValue: policies[i].MinimumPowerInPCM);
                Add(output, device, prefix + "default", $"GPU Power Target Default {i}", "%", policies[i].DefaultPowerInPercent, note: note, rawValue: policies[i].DefaultPowerInPCM);
                Add(output, device, prefix + "maximum", $"GPU Power Target Maximum {i}", "%", policies[i].MaximumPowerInPercent, note: note, rawValue: policies[i].MaximumPowerInPCM);
            }
            if (policies.Length == 0) Add(output, device, "power-policy/range/0", "GPU Power Target Range", "%", null, "unsupported", "Driver returned no power-policy range.");
        }
        catch (Exception ex) { Failure(output, device, "power-policy/range/0", "GPU Power Target Range", "%", ex); }
    }

    private void ReadPowerTopology(List<SensorReading> output, PhysicalGPU gpu, DeviceInfo device)
    {
        try
        {
            var entries = gpu.PowerTopologyInformation.PowerTopologyEntries.ToArray();
            for (var i = 0; i < entries.Length; i++)
                Add(output, device, $"power-topology/{(int)entries[i].Domain}/{i}", $"GPU Power Topology - {entries[i].Domain} {i}", "%", entries[i].PowerUsageInPercent,
                    note: "NVAPI normalized relative power (PCM / 1000); not an individual voltage-rail reading and not watts.", rawValue: entries[i].PowerUsageInPCM);
            if (entries.Length == 0) Add(output, device, "power-topology", "GPU Power Topology", "%", null, "unsupported", "Driver returned no power-topology entries.");
        }
        catch (Exception ex) { Failure(output, device, "power-topology", "GPU Power Topology", "%", ex); }
    }

    private void ReadFans(List<SensorReading> output, PhysicalGPU gpu, DeviceInfo device)
    {
        // Use direct getter APIs: convenience aggregate properties can swallow a
        // failure and substitute zero, which would misrepresent an unavailable fan.
        Scalar(output, device, "fan/tach", "GPU Fan Tachometer", "RPM", () => GPUApi.GetTachReading(gpu.Handle));
        Scalar(output, device, "fan/level", "GPU Fan Level", "%", () => GPUApi.GetCurrentFanSpeedLevel(gpu.Handle));
        try
        {
            var coolers = gpu.CoolerInformation.Coolers.ToArray();
            foreach (var cooler in coolers)
            {
                var note = $"Controller {cooler.CoolerController}; target {cooler.Target}; policy {cooler.CurrentPolicy}.";
                var id = cooler.CoolerId;
                if (cooler.CurrentFanSpeedInRPM >= 0)
                    Add(output, device, $"fan/{id}/rpm", $"GPU Fan {id} Speed", "RPM", cooler.CurrentFanSpeedInRPM, note: note);
                else Add(output, device, $"fan/{id}/rpm", $"GPU Fan {id} Speed", "RPM", null, "unsupported", "Cooler exists but tachometer is unavailable. " + note);
                Add(output, device, $"fan/{id}/level", $"GPU Fan {id} Level", "%", cooler.CurrentLevel, note: note);
            }
            if (coolers.Length == 0) Add(output, device, "fan/coolers", "GPU Cooler Enumeration", "count", null, "unsupported", "Driver returned no GPU coolers; laptop fans may belong to the embedded controller.");
        }
        catch (Exception ex) { Failure(output, device, "fan/coolers", "GPU Cooler Enumeration", "count", ex); }
    }

    private void Scalar(List<SensorReading> output, DeviceInfo device, string id, string name, string unit, Func<double> get)
    {
        try { Add(output, device, id, name, unit, get()); }
        catch (Exception ex) { Failure(output, device, id, name, unit, ex); }
    }

    private void Add(List<SensorReading> output, DeviceInfo device, string id, string name, string unit, double? value,
        string status = "ok", string? note = null, double? rawValue = null)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
        { rawValue = null; value = null; status = "error"; note = "Driver returned a non-finite value."; }
        output.Add(new SensorReading($"{device.Id}/nvapi/{id}", device.Id, device.Name, name, unit, Name, value, status, note, rawValue));
    }

    private void Failure(List<SensorReading> output, DeviceInfo device, string id, string name, string unit, Exception ex)
        => Add(output, device, id, name, unit, null, FailureStatus(ex), Describe(ex));

    private static string FailureStatus(Exception ex)
        => ex is NVIDIANotSupportedException or NotSupportedException
            || ex is NVIDIAApiException nv && nv.Status is Status.NotSupported or Status.NoImplementation
            ? "unsupported" : "error";

    private static string Describe(Exception ex) => ex is NVIDIAApiException nv
        ? $"NVAPI {nv.Status}: {nv.Message}" : $"{ex.GetType().Name}: {ex.Message}";

    private static void Metadata(Dictionary<string, string> values, string key, Func<object> read)
    {
        try { values[key] = Convert.ToString(read(), CultureInfo.InvariantCulture) ?? ""; }
        catch (Exception ex) { values[key + "Status"] = FailureStatus(ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized) { try { NVIDIA.Unload(); } catch { /* Best-effort library release. */ } }
    }
}
