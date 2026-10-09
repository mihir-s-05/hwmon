using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using System.Security.Principal;

namespace Hwmon.Backends;

public sealed class LibreBackend : ISensorBackend
{
    public string Name => "LibreHardwareMonitor";
    public string? InitializationError { get; private set; }
    public IReadOnlyList<DeviceInfo> Devices => _devices;
    private readonly Computer _computer;
    private readonly List<DeviceInfo> _devices = [];
    private readonly bool _lowLevelAccess;

    public LibreBackend(bool gpuOnly = false)
    {
        var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        _lowLevelAccess = admin && PawnIo.IsInstalled;
        _computer = new Computer {
            IsGpuEnabled = true, IsCpuEnabled = !gpuOnly, IsMemoryEnabled = !gpuOnly,
            IsMotherboardEnabled = !gpuOnly, IsStorageEnabled = !gpuOnly,
            IsNetworkEnabled = !gpuOnly, IsBatteryEnabled = !gpuOnly,
            IsControllerEnabled = !gpuOnly, IsPsuEnabled = !gpuOnly,
        };
        try { _computer.Open(); RefreshDevices(); }
        catch (Exception e) { InitializationError = e.Message; }
    }

    private static string DeviceId(IHardware hardware)
    {
        var id = hardware.Identifier.ToString();
        return id.StartsWith("/gpu-nvidia/", StringComparison.Ordinal)
            ? "nvidia/" + id.Split('/')[2] : "lhm" + id;
    }
    private void RefreshDevices()
    {
        _devices.Clear();
        foreach (var hardware in _computer.Hardware) AddDevice(hardware);
    }
    private void AddDevice(IHardware hardware)
    {
        var kind = hardware.HardwareType.ToString().ToLowerInvariant();
        if (kind.StartsWith("gpu")) kind = "gpu";
        _devices.Add(new(DeviceId(hardware), hardware.Name, kind, Name,
            new() { ["libraryIdentifier"] = hardware.Identifier.ToString() }));
        foreach (var child in hardware.SubHardware) AddDevice(child);
    }
    public List<SensorReading> Read()
    {
        var rows = new List<SensorReading>();
        foreach (var hardware in _computer.Hardware) ReadHardware(hardware, rows);
        RefreshDevices();
        return rows;
    }
    private void ReadHardware(IHardware hardware, List<SensorReading> rows)
    {
        try { hardware.Update(); }
        catch (Exception e)
        {
            rows.Add(new($"{DeviceId(hardware)}/lhm/update-error", DeviceId(hardware), hardware.Name,
                "Hardware update", "", Name, null, "error", e.Message));
            return;
        }
        foreach (var sensor in hardware.Sensors)
        {
            var kind = sensor.SensorType.ToString();
            var unit = Unit(sensor.SensorType);
            double? value = sensor.Value;
            string? note = null;
            var status = value.HasValue ? "ok" : "unavailable";
            if (!_lowLevelAccess && hardware.HardwareType == HardwareType.Cpu &&
                (kind is "Temperature" or "Clock" or "Voltage" or "Power") && (value is null or 0))
            {
                value = null; status = "permission";
                note = "CPU register measurements need PawnIO installed and an administrator process. Run hwmon doctor.";
            }
            var metricName = sensor.Name;
            if (hardware.HardwareType.ToString().StartsWith("Gpu"))
            {
                if (kind == "Temperature") metricName += " temperature";
                if (kind == "Clock") metricName += " clock";
                if (kind == "Power") metricName += " power";
                if (kind == "Load" && metricName != "GPU Memory") metricName += " load";
            }
            if (unit == "GB") { unit = "GiB"; note = "Binary capacity from LibreHardwareMonitor."; }
            if (unit == "MB") { unit = "MiB"; note = "Binary capacity from LibreHardwareMonitor."; }
            // Some library versions reuse an identifier for bus load and memory
            // capacity load. Include the display name to preserve both channels.
            var slug = string.Concat(sensor.Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));
            rows.Add(new($"{DeviceId(hardware)}/lhm{sensor.Identifier}/{slug}", DeviceId(hardware),
                hardware.Name, metricName, unit, Name, value, status, note, status == "ok" ? null : sensor.Value));
            if (hardware.Name.Contains("RTX 4070 Laptop", StringComparison.OrdinalIgnoreCase) && kind == "Clock" && sensor.Name == "GPU Memory")
                rows.Add(new($"{DeviceId(hardware)}/derived/memory-clock", DeviceId(hardware), hardware.Name,
                    "GPU memory clock (HWiNFO convention)", "MHz", "Derived", value / 4, status,
                    "Verified on this RTX 4070 Laptop: driver-reported GDDR6 clock divided by four; raw clock remains available.", value));
        }
        foreach (var child in hardware.SubHardware) ReadHardware(child, rows);
    }
    public void Dispose() => _computer.Close();
    public static string Unit(SensorType type) => type.ToString() switch {
        "Voltage" => "V", "Current" => "A", "Clock" => "MHz", "Load" or "Control" or "Level" => "%",
        "Temperature" => "°C", "Fan" => "RPM", "Flow" => "L/h", "Power" => "W",
        "Data" => "GB", "SmallData" => "MB", "Throughput" => "B/s", "TimeSpan" => "s",
        "Energy" => "mWh", "Noise" => "dBA", "Conductivity" => "uS/cm", _ => "",
    };
}
