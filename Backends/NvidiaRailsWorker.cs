// NVAPI private power monitor layouts and rail identifiers adapted from:
// https://github.com/JustAMan/pynvraw/blob/9a860c547256cfc6f527bf4be3aeeed27d95fd85/src/pynvraw/nvapi_api.py
// Copyright (c) 2021 Vasily. MIT License. See THIRD-PARTY-NOTICES.md.
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Hwmon.Backends;

/// <summary>
/// Read-only private NVIDIA power monitoring. Invoke only as an isolated child
/// process: a buggy or changed driver ABI can terminate the process natively.
/// The parent owns timeout, quarantine, and restart policy.
/// </summary>
public static class NvidiaRailsWorker
{
    private const int InfoSize = 3240; // ctypes sizeof NV_POWER_MONITOR_INFO v3
    private const int StatusSize = 1436; // NV_POWER_MONITOR_STATUS v1
    private const int InfoChannelsOffset = 40, InfoChannelSize = 60;
    private const int StatusChannelsOffset = 28, StatusChannelSize = 44;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgs();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate([Out] nint[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BufferQuery(nint handle, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UIntQuery(nint handle, out uint value);

    public static int Run()
    {
        var readings = new List<SensorReading>();
        nint library = 0;
        NoArgs? unload = null;
        int exitCode = 0;
        try
        {
            // Resolve the installed driver library from the Windows system directory.
            library = NativeLibrary.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvapi64.dll"));
            var query = Marshal.GetDelegateForFunctionPointer<QueryInterface>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
            T Get<T>(uint id) where T : Delegate
            {
                var address = query(id);
                if (address == 0) throw new NotSupportedException($"NVAPI function 0x{id:X8} is unavailable in this driver.");
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }
            Check(Get<NoArgs>(0x0150E828)(), "NvAPI_Initialize");
            unload = Get<NoArgs>(0xD22BDD7E);
            var handles = new nint[64];
            Check(Get<Enumerate>(0xE5AC921F)(handles, out var count), "NvAPI_EnumPhysicalGPUs");
            if (count is < 0 or > 64) throw new InvalidDataException($"Invalid GPU count: {count}.");
            var getName = Get<BufferQuery>(0xCEEE8E9F);
            var getBus = Get<UIntQuery>(0x1BE0B8E5);
            var getSlot = Get<UIntQuery>(0x2A0A350F);
            for (uint gpuIndex = 0; gpuIndex < count; gpuIndex++)
            {
                string deviceId = DeviceIdentity.Nvidia(gpuIndex), deviceName = $"NVIDIA GPU {gpuIndex}";
                var handle = handles[gpuIndex];
                var nameBytes = new byte[64];
                if (Call(getName, handle, nameBytes) == 0)
                {
                    int terminator = Array.IndexOf(nameBytes, (byte)0);
                    deviceName = Encoding.ASCII.GetString(nameBytes, 0, terminator < 0 ? nameBytes.Length : terminator);
                }
                string pci = "";
                if (getBus(handle, out uint bus) == 0 && getSlot(handle, out uint slot) == 0)
                    pci = $" PCI bus {bus}, slot {slot}.";
                try
                {
                    var getInfo = Get<BufferQuery>(0xC12EB19E);
                    var getStatus = Get<BufferQuery>(0xF40238EF);
                    var info = new byte[InfoSize];
                    SetUInt(info, 0, 0x00030CA8); // exact version 3 and sizeof 3240
                    Check(Call(getInfo, handle, info), "NvAPI_GPU_PowerMonitorGetInfo(v3)");
                    if (info[4] == 0) throw new NotSupportedException("Driver reports power monitor unsupported.");
                    uint mask = UInt(info, 16);
                    if (mask == 0) throw new NotSupportedException("Driver reports no power monitor channels.");
                    var status = new byte[StatusSize];
                    SetUInt(status, 0, 0x0001059C); // exact version 1 and sizeof 1436
                    SetUInt(status, 4, mask);
                    Check(Call(getStatus, handle, status), "NvAPI_GPU_PowerMonitorGetStatus(v1)");
                    uint returnedMask = UInt(status, 4);
                    for (int channel = 0; channel < 32; channel++)
                    {
                        uint bit = 1u << channel;
                        if ((mask & bit) == 0) continue;
                        int infoOffset = InfoChannelsOffset + channel * InfoChannelSize;
                        uint type = UInt(info, infoOffset + 12), rail = UInt(info, infoOffset + 16);
                        if (type == 0) continue; // DEFAULT is not an active sensor in the source wrapper.
                        int sampleOffset = StatusChannelsOffset + channel * StatusChannelSize;
                        var railName = RailName(rail);
                        string note = $"Read-only private NVAPI. Channel {channel}; rail {rail}; type {ChannelTypeName(type)} ({type})." + pci;
                        string availability = (returnedMask & bit) != 0 ? "ok" : "unavailable";
                        foreach (var metric in new[] { ("power", "Power", "W", 0, 1000d), ("current", "Current", "A", 12, 1000d), ("voltage", "Voltage", "V", 16, 1000000d) })
                        {
                            uint raw = UInt(status, sampleOffset + metric.Item4);
                            bool sentinel = raw == uint.MaxValue;
                            string metricStatus = sentinel ? "unavailable" : availability;
                            string metricNote = note + (sentinel ? " Driver returned unsigned-max marker 0xFFFFFFFF; no usable measurement." : "");
                            readings.Add(new SensorReading($"{deviceId}/rails/{channel}.{metric.Item1}", deviceId, deviceName,
                                $"{railName} Channel {channel} {metric.Item2}", metric.Item3, "NVAPI PowerMonitor", metricStatus == "ok" ? raw / metric.Item5 : null,
                                metricStatus, metricNote, availability == "ok" ? raw : null));
                        }
                    }
                }
                catch (Exception error)
                {
                    readings.Add(new SensorReading($"{deviceId}/rails/status", deviceId, deviceName, "NVIDIA Rail Telemetry Status", "", "NVAPI PowerMonitor", null, "unavailable", error.Message + pci));
                }
                try
                {
                    // pynvraw's NV_GPU_CLOCKS_INFO v2: version + 288 uint32 values.
                    // HWiNFO 8.16 reads byte offset 0x0C and divides by 1000 for
                    // its Crossbar Clock. Immutable disassembly anchors are in
                    // work/rails/findings.md; this is a separate read-only call.
                    var clocks = new byte[0x484];
                    SetUInt(clocks, 0, 0x00020484);
                    Check(Call(Get<BufferQuery>(0x1BD69F49), handle, clocks), "NvAPI_GPU_GetAllClocks(v2)");
                    uint raw = UInt(clocks, 0x0C);
                    bool sentinel = raw == uint.MaxValue;
                    readings.Add(new SensorReading($"{deviceId}/clock.crossbar", deviceId, deviceName, "GPU Crossbar Clock", "MHz",
                        "NVAPI GetAllClocks", sentinel ? null : raw / 1000d, sentinel ? "unavailable" : "ok",
                        "Read-only private NVAPI v2, clocks[2]. Mapping verified against HWiNFO 8.16 immutable code." + pci, raw));
                }
                catch (Exception error)
                {
                    readings.Add(new SensorReading($"{deviceId}/clock.crossbar", deviceId, deviceName, "GPU Crossbar Clock", "MHz",
                        "NVAPI GetAllClocks", null, "unavailable", error.Message + pci));
                }
            }
            if (count == 0) readings.Add(Error("No NVIDIA GPU was enumerated."));
        }
        catch (Exception error)
        {
            readings.Add(Error(error.Message));
            exitCode = 1;
        }
        finally
        {
            if (unload != null) unload();
            if (library != 0) NativeLibrary.Free(library);
        }
        Console.WriteLine(JsonSerializer.Serialize(readings));
        return exitCode;
    }

    private static SensorReading Error(string text) => new("nvidia/rails/status", "nvidia", "NVIDIA", "NVIDIA Rail Telemetry Status", "", "NVAPI PowerMonitor", null, "unavailable", text);
    private static void Check(int result, string operation) { if (result != 0) throw new InvalidOperationException($"{operation} returned NVAPI status {result}."); }
    private static uint UInt(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void SetUInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    private static int Call(BufferQuery method, nint gpu, byte[] data)
    {
        var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { return method(gpu, pinned.AddrOfPinnedObject()); }
        finally { pinned.Free(); }
    }

    private static string ChannelTypeName(uint type) => type switch
    {
        1 => "Summation", 2 => "Estimation", 3 => "Slow", 4 => "Gemini correction",
        5 => "1X", 6 => "Sensor", 7 => "P-state estimation LUT", 8 => "Client-aligned sensor", _ => "Unknown"
    };

    private static string RailName(uint rail) => rail switch
    {
        1 => "NVVDD Output", 2 => "FBVDD Output", 3 => "FBVDDQ Output", 4 => "FBVDD/Q Output",
        5 => "PEX VDD Output", 6 => "3.3V Auxiliary Output", 7 => "3.3V NV Output", 8 => "Total GPU Output",
        9 => "FBVDDQ GPU Output", 10 => "FBVDDQ Memory Output", 11 => "SRAM Output",
        // HWiNFO 8.16 immutable rail-name dispatch maps numeric rail 0xDA (218)
        // to its exact "GPU 16-pin HVPWR" label; see work/rails findings.
        218 => "16-pin HVPWR", 222 => "PCIe 12V Input 1", 223 => "Total Board Input 2", 224 => "High-voltage Input 0", 225 => "High-voltage Input 1",
        226 => "NVVDD Input 1", 227 => "NVVDD Input 2", 228 => "External 12V 8-pin Input 2", 229 => "External 12V 8-pin Input 3",
        230 => "External 12V 8-pin Input 4", 231 => "External 12V 8-pin Input 5", 232 => "Misc 0 Input", 233 => "Misc 1 Input",
        234 => "Misc 2 Input", 235 => "Misc 3 Input", 236 => "USB-C 0 Input", 237 => "USB-C 1 Input", 238 => "Fan 0 Input",
        239 => "Fan 1 Input", 240 => "SRAM Input", 241 => "PP Source Input", 242 => "3.3V PP Input", 243 => "3.3V Main Input",
        244 => "3.3V Always-on Input", 245 => "Total Board Input", 246 => "NVVDD Input", 247 => "FBVDD Input",
        248 => "FBVDDQ Input", 249 => "FBVDD/Q Input", 250 => "External 12V 8-pin Input 0", 251 => "External 12V 8-pin Input 1",
        252 => "External 12V 6-pin Input 0", 253 => "External 12V 6-pin Input 1", 254 => "PCIe 3.3V Input", 255 => "PCIe 12V Input",
        _ => $"Unknown Rail {rail}"
    };
}
