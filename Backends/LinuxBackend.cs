using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hwmon.Backends;

/// <summary>Read-only kernel telemetry; no shell, GUI, or privileged register writes.</summary>
public sealed class LinuxBackend : ISensorBackend
{
    public string Name => "Linux kernel";
    public string? InitializationError { get; private set; }
    public IReadOnlyList<DeviceInfo> Devices => _devices;
    private readonly List<DeviceInfo> _devices = [];
    private readonly Dictionary<string, (double[] Counters, long Tick)> _previous = new();
    private readonly string _root;
    private readonly bool _wsl;
    private static readonly Regex HwmonName = new(@"^(temp|fan|in|curr|power|freq|humidity)(\d+)_(input|average|max|crit|alarm|fault)$", RegexOptions.Compiled);
    public LinuxBackend(string root = "/")
    {
        _root = root; _wsl = PlatformInfo.IsWsl;
        if (!File.Exists(PathAt("proc/stat"))) InitializationError = "Linux /proc/stat is unavailable.";
    }
    private string PathAt(string relative) => Path.Combine(_root, relative);
    private static double Number(string text) => double.Parse(text.Trim(), CultureInfo.InvariantCulture);
    private static string? Text(string path) { try { return File.ReadAllText(path).Trim(); } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; } }
    private void Device(string id, string name, string kind, Dictionary<string, string>? metadata = null)
    {
        if (_devices.All(d => d.Id != id)) _devices.Add(new(id, name, kind, Name, metadata));
    }
    private (double[] Delta, double Seconds)? Delta(string key, double[] counters)
    {
        var tick = Stopwatch.GetTimestamp();
        _previous.TryGetValue(key, out var last); _previous[key] = (counters, tick);
        if (last.Counters == null || last.Counters.Length != counters.Length) return null;
        var seconds = (tick - last.Tick) / (double)Stopwatch.Frequency;
        var diff = counters.Zip(last.Counters, (a, b) => a - b).ToArray();
        return seconds > 0 && diff.All(v => v >= 0) ? (diff, seconds) : null;
    }
    public static (string Unit, double Scale) HwmonUnit(string kind, string item) => item is "alarm" or "fault" ? ("bool", 1) : kind switch {
        "temp" => ("°C", .001), "in" => ("V", .001), "curr" => ("A", .001), "power" => ("W", .000001),
        "freq" => ("MHz", .000001), "humidity" => ("%", .001), "fan" => ("RPM", 1), _ => ("", 1)
    };
    public List<SensorReading> Read()
    {
        var rows = new List<SensorReading>(); _devices.Clear();
        void Guard(Action read, string section) { try { read(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or OverflowException) {
            Device("linux/system", "Linux telemetry", "system");
            rows.Add(new("linux/system/error/" + section, "linux/system", "Linux telemetry", section + " read", "", Name, null,
                e is UnauthorizedAccessException ? "permission" : "error", e.Message));
        } }
        Guard(() => ReadCpu(rows), "cpu"); Guard(() => ReadMemory(rows), "memory");
        Guard(() => ReadNetwork(rows), "network"); Guard(() => ReadDisks(rows), "storage");
        Guard(() => ReadHwmon(rows), "hwmon"); Guard(() => ReadBatteries(rows), "battery");
        return rows;
    }
    private void Add(List<SensorReading> rows, string device, string id, string name, string unit, double? value, string? note = null, string? status = null, double? raw = null)
        => rows.Add(new(device + "/" + id, device, _devices.First(d => d.Id == device).Name, name, unit, Name, value,
            status ?? (value.HasValue ? "ok" : "unavailable"), note, raw));
    private void ReadCpu(List<SensorReading> rows)
    {
        var info = Text(PathAt("proc/cpuinfo")) ?? "";
        var model = info.Split('\n').FirstOrDefault(s => s.StartsWith("model name"))?.Split(':', 2)[1].Trim() ?? "Linux CPU";
        var note = _wsl ? "WSL virtual CPU counters/frequencies; host physical temperatures and power are not exposed by /proc." : "Kernel CPU counters; utilization excludes idle and I/O-wait time.";
        Device("linux/cpu", model, "cpu", new() { ["virtualized"] = _wsl.ToString(), ["logicalProcessors"] = Environment.ProcessorCount.ToString() });
        foreach (var line in File.ReadLines(PathAt("proc/stat")).Where(l => Regex.IsMatch(l, @"^cpu\d*\s")))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var counters = p.Skip(1).Take(8).Select(Number).ToArray();
            var change = Delta("cpu/" + p[0], counters);
            var total = change?.Delta.Sum(); var idle = change is { } c && c.Delta.Length >= 5 ? c.Delta[3] + c.Delta[4] : 0;
            var value = total > 0 ? (total - idle) / total * 100 : null;
            Add(rows, "linux/cpu", p[0] + "/load", p[0] == "cpu" ? "CPU Total" : "CPU logical " + p[0][3..] + " load", "%", value, note);
        }
        int processor = 0;
        foreach (var line in info.Split('\n'))
        {
            if (line.StartsWith("processor")) int.TryParse(line.Split(':', 2)[1].Trim(), out processor);
            if (!line.StartsWith("cpu MHz")) continue;
            Add(rows, "linux/cpu", $"cpu{processor}/clock", $"CPU logical {processor} reported clock", "MHz", Number(line.Split(':', 2)[1]), note);
        }
        var load = Text(PathAt("proc/loadavg"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (load is { Length: >= 3 }) for (int i = 0; i < 3; i++)
            Add(rows, "linux/cpu", "load-average/" + i, $"Runnable/I/O-wait load average ({new[] { 1, 5, 15 }[i]}m)", "tasks", Number(load[i]), "Linux load average is a task count, not CPU utilization percentage.");
    }
    private void ReadMemory(List<SensorReading> rows)
    {
        Device("linux/memory", _wsl ? "WSL virtual memory" : "System memory", "memory", new() { ["scope"] = _wsl ? "WSL VM" : "Linux OS" });
        var memory = File.ReadLines(PathAt("proc/meminfo")).Select(l => l.Split(':', 2)).ToDictionary(p => p[0], p => Number(p[1].Trim().Split(' ')[0]));
        foreach (var key in new[] { "MemTotal", "MemAvailable", "MemFree", "Cached", "Buffers", "SwapTotal", "SwapFree" })
            if (memory.TryGetValue(key, out var value)) Add(rows, "linux/memory", key, key, "GiB", value / 1048576.0, "/proc/meminfo kB uses 1024-byte units.");
        if (memory.TryGetValue("MemTotal", out var total) && memory.TryGetValue("MemAvailable", out var available)) {
            Add(rows, "linux/memory", "used", "Memory used (total minus available)", "GiB", (total - available) / 1048576.0);
            Add(rows, "linux/memory", "load", "Memory load", "%", total > 0 ? (total - available) / total * 100 : null);
        }
    }
    private void ReadNetwork(List<SensorReading> rows)
    {
        foreach (var line in File.ReadLines(PathAt("proc/net/dev")).Skip(2))
        {
            var p = line.Split(':', 2); if (p.Length != 2) continue;
            var name = p[0].Trim(); var v = p[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
            if (v.Length < 16) continue;
            var id = "linux/network/" + Uri.EscapeDataString(name); Device(id, name, "network");
            var change = Delta(id, [v[0], v[8]]);
            Add(rows, id, "rx", "Network receive rate", "B/s", change is { } rx ? rx.Delta[0] / rx.Seconds : null, "Counter difference divided by monotonic elapsed time.");
            Add(rows, id, "tx", "Network transmit rate", "B/s", change is { } tx ? tx.Delta[1] / tx.Seconds : null);
            Add(rows, id, "rx-errors", "Receive errors since interface start", "count", v[2]);
            Add(rows, id, "tx-errors", "Transmit errors since interface start", "count", v[10]);
        }
    }
    private void ReadDisks(List<SensorReading> rows)
    {
        foreach (var line in File.ReadLines(PathAt("proc/diskstats")))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); if (p.Length < 14) continue;
            var name = p[2];
            if (name.StartsWith("loop") || name.StartsWith("ram") || File.Exists(PathAt($"sys/class/block/{name}/partition"))) continue;
            var id = "linux/disk/" + name;
            Device(id, name, "storage", new() { ["scope"] = _wsl ? "WSL virtual block device; not host physical-drive SMART" : "Linux block device" });
            var change = Delta(id, [Number(p[5]), Number(p[9])]);
            Add(rows, id, "read", "Disk read rate", "B/s", change is { } rd ? rd.Delta[0] * 512 / rd.Seconds : null, "Linux diskstats sectors are 512 bytes, independent of physical sector size.");
            Add(rows, id, "write", "Disk write rate", "B/s", change is { } wr ? wr.Delta[1] * 512 / wr.Seconds : null);
            Add(rows, id, "queue", "Disk I/O in progress", "requests", Number(p[11]));
        }
    }
    private void ReadHwmon(List<SensorReading> rows)
    {
        var root = PathAt("sys/class/hwmon"); if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var chip = Text(Path.Combine(directory, "name")) ?? Path.GetFileName(directory);
            var id = "linux/hwmon/" + Uri.EscapeDataString(chip) + "/" + Path.GetFileName(directory);
            var kind = chip is "coretemp" or "k10temp" or "zenpower" ? "cpu" : chip is "amdgpu" or "nouveau" ? "gpu" : "motherboard";
            Device(id, chip, kind, new() { ["sysfsPath"] = directory, ["labeling"] = "Driver labels; board-specific resistor calibration from sensors.conf is not applied." });
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var match = HwmonName.Match(Path.GetFileName(file)); if (!match.Success) continue;
                var type = match.Groups[1].Value; var channel = type + match.Groups[2].Value; var item = match.Groups[3].Value;
                var label = Text(Path.Combine(directory, channel + "_label")) ?? channel;
                var (unit, scale) = HwmonUnit(type, item);
                var text = Text(file); double? raw = text == null ? null : Number(text);
                var note = $"Kernel hwmon {Path.GetFileName(file)}; standard ABI scaling, without board-specific userspace calibration.";
                // temp*_type=4 is a thermistor voltage requiring a userspace conversion.
                if (type == "temp" && item is not ("alarm" or "fault") && Text(Path.Combine(directory, channel + "_type")) == "4") {
                    Add(rows, id, Path.GetFileName(file), label + " " + item, "raw", null,
                        note + " Thermistor conversion is not available; raw value preserved.", "unsupported", raw); continue;
                }
                if (item is "input" or "average" && Text(Path.Combine(directory, channel + "_fault")) == "1") {
                    Add(rows, id, Path.GetFileName(file), label + " " + item, unit, null, note + " Driver fault flag is active.", "unavailable", raw); continue;
                }
                Add(rows, id, Path.GetFileName(file), label + " " + item, unit, raw * scale, note, raw: raw);
            }
        }
    }
    private void ReadBatteries(List<SensorReading> rows)
    {
        var root = PathAt("sys/class/power_supply"); if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (Text(Path.Combine(directory, "type")) != "Battery") continue;
            var name = Path.GetFileName(directory); var id = "linux/battery/" + name;
            Device(id, name, "battery", new() { ["status"] = Text(Path.Combine(directory, "status")) ?? "unknown" });
            foreach (var (file, unit, scale) in new[] { ("capacity", "%", 1d), ("voltage_now", "V", 1e-6), ("current_now", "A", 1e-6), ("power_now", "W", 1e-6), ("energy_now", "Wh", 1e-6), ("energy_full", "Wh", 1e-6), ("cycle_count", "count", 1d) })
                if (Text(Path.Combine(directory, file)) is string value) Add(rows, id, file, file, unit, Number(value) * scale);
        }
    }
    public void Dispose() { }
}
