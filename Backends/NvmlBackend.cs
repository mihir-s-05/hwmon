using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;

namespace Hwmon.Backends;

public sealed class NvmlBackend : ISensorBackend
{
    public static string? LibraryPath { get; private set; }
    static NvmlBackend() => NativeLibrary.SetDllImportResolver(typeof(NvmlBackend).Assembly, ResolveLibrary);
    private static IntPtr ResolveLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != "hwmon-nvml") return IntPtr.Zero;
        var candidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.SystemDirectory, "nvml.dll") }
            : new[] { "/usr/lib/wsl/lib/libnvidia-ml.so.1", "libnvidia-ml.so.1", "libnvidia-ml.so" };
        foreach (var path in candidates)
            if (NativeLibrary.TryLoad(path, out var handle)) { LibraryPath = path; return handle; }
        throw new DllNotFoundException("NVIDIA NVML unavailable. Tried: " + string.Join(", ", candidates) + ". On WSL use the Windows NVIDIA driver; on native Linux install the NVIDIA driver.");
    }
    public string Name => "NVML";
    public string? InitializationError { get; private set; }
    public IReadOnlyList<DeviceInfo> Devices => _devices;
    private readonly List<DeviceInfo> _devices = [];
    private readonly List<IntPtr> _handles = [];
    private bool _initialized;

    public NvmlBackend()
    {
        try
        {
            var result = Native.Init();
            if (result != 0) throw new InvalidOperationException(Error(result));
            _initialized = true;
            result = Native.Count(out var count);
            if (result != 0) throw new InvalidOperationException(Error(result));
            var driver = new StringBuilder(96);
            Native.Driver(driver, 96);
            for (uint i = 0; i < count; i++)
            {
                result = Native.Handle(i, out var handle);
                if (result != 0) continue;
                var name = new StringBuilder(128);
                var uuid = new StringBuilder(96);
                Native.Name(handle, name, 128);
                Native.Uuid(handle, uuid, 96);
                var metadata = new Dictionary<string, string> { ["driver"] = driver.ToString(), ["uuid"] = uuid.ToString() };
                if (Native.Pci(handle, out var pci) == 0)
                {
                    metadata["pciBus"] = pci.Bus.ToString();
                    metadata["pciAddress"] = pci.BusId;
                }
                _handles.Add(handle);
                _devices.Add(new(DeviceIdentity.Nvidia(i), name.ToString(), "gpu", Name, metadata));
            }
        }
        catch (Exception e) { InitializationError = e.Message; }
    }

    public List<SensorReading> Read()
    {
        var rows = new List<SensorReading>();
        for (var index = 0; index < _handles.Count; index++)
        {
            var device = _devices[index];
            var handle = _handles[index];
            void Add(string id, string name, string unit, int result, double value, string? note = null)
                => rows.Add(new($"{device.Id}/nvml/{id}", device.Id, device.Name, name, unit, Name,
                    result == 0 ? value : null, Status(result), result == 0 ? note : Error(result)));
            void Query(string id, string name, string unit, QueryUInt call, double scale = 1, string? note = null)
            {
                try { var r = call(handle, out var value); Add(id, name, unit, r, value * scale, note); }
                catch (Exception e) { rows.Add(new($"{device.Id}/nvml/{id}", device.Id, device.Name, name, unit, Name, null, "unsupported", e.Message)); }
            }

            Query("temperature.core", "GPU core temperature (NVML)", "°C", (IntPtr h, out uint v) => Native.Temperature(h, 0, out v));
            Query("clock.graphics", "GPU core clock (NVML)", "MHz", (IntPtr h, out uint v) => Native.Clock(h, 0, out v));
            Query("clock.sm", "GPU SM clock", "MHz", (IntPtr h, out uint v) => Native.Clock(h, 1, out v));
            Query("clock.memory", "GPU memory clock (driver reported)", "MHz", (IntPtr h, out uint v) => Native.Clock(h, 2, out v));
            Query("clock.video", "GPU video clock", "MHz", (IntPtr h, out uint v) => Native.Clock(h, 3, out v));
            Query("pstate", "GPU performance state", "P-state", Native.Pstate);
            Query("fan", "GPU fan intended speed", "%", Native.Fan, note: "Intended fan speed, not measured fan RPM.");
            Query("power.reported", "GPU power (NVML reported)", "W", Native.Power, 0.001,
                "NVML nvmlDeviceGetPowerUsage; averaging depends on architecture. Instantaneous power is separate.");
            Query("power.limit.enforced", "GPU power limit (enforced)", "W", Native.EnforcedPower, 0.001);
            Query("power.limit.requested", "GPU power limit (requested)", "W", Native.RequestedPower, 0.001);
            Query("power.limit.default", "GPU power limit (default)", "W", Native.DefaultPower, 0.001);
            Query("pcie.generation", "PCIe link generation", "generation", Native.PcieGeneration);
            Query("pcie.width", "PCIe link width", "lanes", Native.PcieWidth);
            Query("pcie.tx", "GPU PCIe transmit", "KiB/s", (IntPtr h, out uint v) => Native.PcieThroughput(h, 0, out v));
            Query("pcie.rx", "GPU PCIe receive", "KiB/s", (IntPtr h, out uint v) => Native.PcieThroughput(h, 1, out v));
            Query("temperature.limit.shutdown", "GPU shutdown temperature threshold", "°C", (IntPtr h, out uint v) => Native.Threshold(h, 0, out v));
            Query("temperature.limit.slowdown", "GPU slowdown temperature threshold", "°C", (IntPtr h, out uint v) => Native.Threshold(h, 1, out v));
            Query("temperature.limit.memory", "GPU memory operating temperature threshold", "°C", (IntPtr h, out uint v) => Native.Threshold(h, 2, out v));
            Query("temperature.limit.gpu", "GPU operating temperature threshold", "°C", (IntPtr h, out uint v) => Native.Threshold(h, 3, out v));
            try
            {
                var r = Native.PowerConstraints(handle, out var min, out var max);
                Add("power.limit.min", "GPU power limit (minimum)", "W", r, min / 1000.0);
                Add("power.limit.max", "GPU power limit (maximum)", "W", r, max / 1000.0);
                r = Native.Utilization(handle, out var utilization);
                Add("load.gpu", "GPU core utilization (NVML)", "%", r, utilization.Gpu);
                Add("load.memory", "GPU memory access utilization (NVML)", "%", r, utilization.Memory,
                    "Memory-controller busy time; distinct from allocated VRAM percentage.");
                r = Native.Encoder(handle, out var encoder, out var encoderPeriod);
                Add("load.encoder", "GPU encoder utilization", "%", r, encoder, $"Native sampling period {encoderPeriod} us.");
                r = Native.Decoder(handle, out var decoder, out var decoderPeriod);
                Add("load.decoder", "GPU decoder utilization", "%", r, decoder, $"Native sampling period {decoderPeriod} us.");
                r = Native.EncoderStats(handle, out var sessions, out var fps, out var latency);
                Add("encoder.sessions", "Active encoding sessions", "count", r, sessions);
                Add("encoder.fps", "Encoder average frame rate", "FPS", r, fps, "Encoder statistics, not game presentation FPS.");
                Add("encoder.latency", "Encoder average latency", "us", r, latency);
                ReadMemory(handle, device, rows);
                ReadClockEvents(handle, device, rows);
                ReadFields(handle, device, rows);
            }
            catch (Exception e)
            {
                rows.Add(new($"{device.Id}/nvml/backend-error", device.Id, device.Name, "NVML optional query failure", "", Name, null, "error", e.Message));
            }
        }
        return rows;
    }

    private void ReadMemory(IntPtr handle, DeviceInfo device, List<SensorReading> rows)
    {
        var memory = new MemoryV2 { Version = (uint)Marshal.SizeOf<MemoryV2>() | (2u << 24) };
        int result;
        try { result = Native.Memory2(handle, ref memory); }
        catch (EntryPointNotFoundException) { result = 3; }
        var hasReserved = result == 0;
        if (result != 0)
        {
            result = Native.Memory(handle, out var old);
            memory.Total = old.Total; memory.Free = old.Free; memory.Used = old.Used;
        }
        void Add(string id, string name, ulong value, string? note = null)
            => rows.Add(new($"{device.Id}/nvml/memory.{id}", device.Id, device.Name, name, "MiB", Name,
                result == 0 ? value / 1048576.0 : null, Status(result), result == 0 ? note : Error(result)));
        Add("total", "GPU memory total", memory.Total);
        Add("free", "GPU memory free", memory.Free);
        Add("used", hasReserved ? "GPU memory used (excluding reserved)" : "GPU memory used (v1 native accounting)", memory.Used,
            hasReserved ? null : "Legacy NVML accounting does not separately expose reserved memory; do not assume this is application-only VRAM.");
        if (hasReserved)
            Add("reserved", "GPU memory reserved", memory.Reserved, "Driver/firmware reserved memory.");
        else rows.Add(new($"{device.Id}/nvml/memory.reserved", device.Id, device.Name, "GPU memory reserved", "MiB", Name,
            null, "unsupported", "The driver's v1 memory query does not distinguish reserved memory."));
    }

    private void ReadClockEvents(IntPtr handle, DeviceInfo device, List<SensorReading> rows)
    {
        int result, supportResult;
        ulong active, supported;
        try { result = Native.Events(handle, out active); supportResult = Native.SupportedEvents(handle, out supported); }
        catch (EntryPointNotFoundException) { result = Native.LegacyEvents(handle, out active); supportResult = Native.LegacySupportedEvents(handle, out supported); }
        (string Id, string Label, ulong Mask)[] flags = [
            ("idle", "GPU clock event: idle", 1), ("application-clocks", "GPU clock event: application clocks", 2),
            ("power", "GPU clock event: software power cap", 4), ("hardware-slowdown", "GPU clock event: hardware slowdown", 8),
            ("sync-boost", "GPU clock event: sync boost", 16), ("thermal.software", "GPU clock event: software thermal slowdown", 32),
            ("thermal.hardware", "GPU clock event: hardware thermal slowdown", 64), ("power-brake", "GPU clock event: hardware power brake", 128),
            ("display-clocks", "GPU clock event: display clocks", 256), ("board-limit", "GPU clock event: board limit", 512),
            ("reliability", "GPU clock event: reliability policy", 1024)];
        foreach (var flag in flags)
        {
            var available = result == 0 && supportResult == 0 && (supported & flag.Mask) != 0;
            rows.Add(new($"{device.Id}/nvml/event.{flag.Id}", device.Id, device.Name, flag.Label, "bool", Name,
                available ? ((active & flag.Mask) != 0 ? 1 : 0) : null,
                result != 0 ? Status(result) : available ? "ok" : "unsupported",
                result != 0 ? Error(result) : available ? null : "The driver does not advertise this event bit as supported."));
        }
        rows.Add(new($"{device.Id}/nvml/events.mask", device.Id, device.Name, "GPU clock event raw mask", "mask", Name,
            result == 0 ? active : null, Status(result), result == 0 ? $"0x{active:X}; supported mask 0x{supported:X}" : Error(result)));
    }

    private void ReadFields(IntPtr handle, DeviceInfo device, List<SensorReading> rows)
    {
        (uint Field, string Id, string Label, string Unit, double Scale)[] wanted = [
            (185, "power.average", "GPU power averaged over one second", "W", 0.001),
            (186, "power.instant", "GPU instantaneous power", "W", 0.001),
            (82, "temperature.memory", "GPU memory temperature (NVML)", "°C", 1),
            (191, "energy", "GPU cumulative energy since driver start", "J", 0.001)];
        var values = wanted.Select(f => new FieldValue { FieldId = f.Field }).ToArray();
        var result = Native.Fields(handle, values.Length, values);
        for (var i = 0; i < values.Length; i++)
        {
            var field = wanted[i]; var value = values[i]; var r = result == 0 ? value.Return : result;
            var number = value.Type switch { 0 => value.Value.Double, 1 => value.Value.UInt,
                2 => OperatingSystem.IsWindows() ? value.Value.UInt : value.Value.ULong,
                3 => (double)value.Value.ULong, 4 => value.Value.Long, 5 => value.Value.Int, 6 => value.Value.UShort, _ => double.NaN };
            rows.Add(new($"{device.Id}/nvml/{field.Id}", device.Id, device.Name, field.Label, field.Unit, Name,
                r == 0 && double.IsFinite(number) ? number * field.Scale : null, Status(r),
                r == 0 ? $"NVML field {field.Field}; source timestamp {value.Timestamp} us since epoch." : Error(r)));
        }
    }

    public void Dispose() { if (_initialized) { Native.Shutdown(); _initialized = false; } }
    private static string Status(int result) => result switch { 0 => "ok", 3 or 13 => "unsupported", 4 => "permission", 15 => "unavailable", _ => "error" };
    private static string Error(int result)
    {
        try { return Marshal.PtrToStringAnsi(Native.ErrorString(result)) ?? $"NVML error {result}"; }
        catch { return $"NVML error {result}"; }
    }
    private delegate int QueryUInt(IntPtr handle, out uint value);
    [StructLayout(LayoutKind.Sequential)] private struct UtilizationInfo { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo { public ulong Total, Free, Used; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryV2 { public uint Version; public ulong Total, Reserved, Free, Used; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] private struct PciInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Legacy;
        public uint Domain, Bus, Device, DeviceId, SubsystemId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string BusId;
    }
    [StructLayout(LayoutKind.Explicit)] private struct FieldUnion
    {
        [FieldOffset(0)] public double Double;
        [FieldOffset(0)] public uint UInt;
        [FieldOffset(0)] public ulong ULong;
        [FieldOffset(0)] public long Long;
        [FieldOffset(0)] public int Int;
        [FieldOffset(0)] public ushort UShort;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FieldValue
    { public uint FieldId, Scope; public long Timestamp, Latency; public uint Type; public int Return; public FieldUnion Value; }

    private static class Native
    {
        private const string Dll = "hwmon-nvml";
        [DllImport(Dll, EntryPoint = "nvmlInit_v2")] internal static extern int Init();
        [DllImport(Dll, EntryPoint = "nvmlShutdown")] internal static extern int Shutdown();
        [DllImport(Dll, EntryPoint = "nvmlErrorString")] internal static extern IntPtr ErrorString(int result);
        [DllImport(Dll, EntryPoint = "nvmlSystemGetDriverVersion", CharSet = CharSet.Ansi)] internal static extern int Driver(StringBuilder value, uint length);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetCount_v2")] internal static extern int Count(out uint count);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] internal static extern int Handle(uint index, out IntPtr handle);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetName", CharSet = CharSet.Ansi)] internal static extern int Name(IntPtr h, StringBuilder name, uint length);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetUUID", CharSet = CharSet.Ansi)] internal static extern int Uuid(IntPtr h, StringBuilder uuid, uint length);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetPciInfo_v3")] internal static extern int Pci(IntPtr h, out PciInfo pci);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetTemperature")] internal static extern int Temperature(IntPtr h, uint sensor, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetTemperatureThreshold")] internal static extern int Threshold(IntPtr h, uint threshold, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetClockInfo")] internal static extern int Clock(IntPtr h, uint clock, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetPerformanceState")] internal static extern int Pstate(IntPtr h, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetFanSpeed")] internal static extern int Fan(IntPtr h, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetPowerUsage")] internal static extern int Power(IntPtr h, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")] internal static extern int EnforcedPower(IntPtr h, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetPowerManagementLimit")] internal static extern int RequestedPower(IntPtr h, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetPowerManagementDefaultLimit")] internal static extern int DefaultPower(IntPtr h, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetPowerManagementLimitConstraints")] internal static extern int PowerConstraints(IntPtr h, out uint minimum, out uint maximum);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetCurrPcieLinkGeneration")] internal static extern int PcieGeneration(IntPtr h, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetCurrPcieLinkWidth")] internal static extern int PcieWidth(IntPtr h, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetPcieThroughput")] internal static extern int PcieThroughput(IntPtr h, uint counter, out uint value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetUtilizationRates")] internal static extern int Utilization(IntPtr h, out UtilizationInfo value);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetEncoderUtilization")] internal static extern int Encoder(IntPtr h, out uint value, out uint period);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetDecoderUtilization")] internal static extern int Decoder(IntPtr h, out uint value, out uint period);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetEncoderStats")] internal static extern int EncoderStats(IntPtr h, out uint sessions, out uint fps, out uint latency);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetMemoryInfo")] internal static extern int Memory(IntPtr h, out MemoryInfo info);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetMemoryInfo_v2")] internal static extern int Memory2(IntPtr h, ref MemoryV2 info);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetCurrentClocksEventReasons")] internal static extern int Events(IntPtr h, out ulong reasons);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetSupportedClocksEventReasons")] internal static extern int SupportedEvents(IntPtr h, out ulong reasons);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetCurrentClocksThrottleReasons")] internal static extern int LegacyEvents(IntPtr h, out ulong reasons);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetSupportedClocksThrottleReasons")] internal static extern int LegacySupportedEvents(IntPtr h, out ulong reasons);
        [DllImport(Dll, EntryPoint = "nvmlDeviceGetFieldValues")] internal static extern int Fields(IntPtr h, int count, [In, Out] FieldValue[] values);
    }
}
