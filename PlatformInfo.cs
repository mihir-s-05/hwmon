using System.Runtime.InteropServices;
#if WINDOWS
using LibreHardwareMonitor.PawnIo;
using System.Security.Principal;
#endif

namespace Hwmon;

public static class PlatformInfo
{
    public static bool IsWsl => OperatingSystem.IsLinux() &&
        (File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop") || File.ReadAllText("/proc/sys/kernel/osrelease").Contains("microsoft", StringComparison.OrdinalIgnoreCase));
    public static Dictionary<string, object?> Doctor()
    {
        var report = new Dictionary<string, object?> { ["version"] = VersionInfo.Value, ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(), ["wsl"] = IsWsl,
            ["continuousMonitoring"] = "No HWiNFO shared-memory dependency or application timer." };
#if WINDOWS
        var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        report["administrator"] = admin; report["pawnIoInstalled"] = PawnIo.IsInstalled;
        report["cpuLowLevelAccess"] = admin && PawnIo.IsInstalled;
        report["cpuSetup"] = "For CPU register sensors install official PawnIO (https://pawnio.eu/) and use an elevated terminal.";
        report["privateWorker"] = "Private NVIDIA queries are isolated with an 8-second timeout; --no-rails disables them.";
#else
        report["procAvailable"] = File.Exists("/proc/stat");
        report["hwmonChips"] = Directory.Exists("/sys/class/hwmon") ? Directory.EnumerateDirectories("/sys/class/hwmon").Count() : 0;
        report["scope"] = IsWsl ? "CPU/RAM/network/disks describe the WSL VM. Physical CPU temperatures/rails are usually not exposed." : "CPU/RAM/network/disk counters and driver-exposed hwmon sensors.";
        report["gpuSetup"] = IsWsl ? "Uses /usr/lib/wsl/lib/libnvidia-ml.so.1 supplied by the Windows NVIDIA driver. Do not install a Linux display driver inside WSL." : "Uses the installed NVIDIA Linux driver libnvidia-ml.so.1.";
        report["windowsHelper"] = IsWsl ? "Optional: --windows-helper /path/to/windows/hwmon.exe adds direct Windows NVIDIA telemetry through WSL interop; no GUI automation." : "Windows helper is WSL-only. Native Linux uses NVML and kernel drivers.";
        report["bundledWindowsHelper"] = File.Exists(Path.Combine(AppContext.BaseDirectory, "windows", "hwmon.exe"));
#endif
        using var nvml = new Backends.NvmlBackend();
        report["nvmlLibrary"] = Backends.NvmlBackend.LibraryPath;
        report["nvmlStatus"] = nvml.InitializationError ?? "ok";
        report["nvidiaDevices"] = nvml.Devices.Count;
        return report;
    }
}
