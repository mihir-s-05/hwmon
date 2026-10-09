using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Hwmon;
using Hwmon.Backends;
#if WINDOWS
if (args.Length == 1 && args[0] == "--internal-rails") return NvidiaRailsWorker.Run();
#endif
Console.OutputEncoding = new UTF8Encoding(false);
try
{
    var o = CliOptions.Parse(args);
    if (o.Command == "help") { Help(); return 0; }
    if (o.Command == "version") { Console.WriteLine("hwmon " + VersionInfo.Value); return 0; }
    if (o.Command == "self-test") return SelfTests.Run();
    if (o.Command == "doctor")
    {
        var report = PlatformInfo.Doctor();
        if (o.Json) Console.WriteLine(JsonSerializer.Serialize(report, JsonOutput.Pretty));
        else foreach (var field in report) Console.WriteLine($"{field.Key}: {field.Value}");
        return 0;
    }
    if (o.Command is not ("show" or "sensors" or "devices" or "diag" or "watch" or "log" or "export" or "coverage"))
        throw new ArgumentException($"Unknown command: {o.Command}. Run hwmon --help.");
    if (o.Command is "log" or "export" && o.Output == null) throw new ArgumentException($"{o.Command} requires an output filename.");
    if (o.Command == "coverage" && o.Reference == null) throw new ArgumentException("coverage requires --reference HWiNFO.csv.");
    // Validate output paths before opening hardware or starting native workers.
    if (o.Output != null && File.Exists(o.Output) && !o.Force) throw new IOException("Output already exists. Choose a new filename or use --force.");
    var helperPath = o.WindowsHelper;
#if !WINDOWS
    if (!o.NativeOnly && helperPath == null && PlatformInfo.IsWsl) {
        var bundled = Path.Combine(AppContext.BaseDirectory, "windows", "hwmon.exe");
        if (File.Exists(bundled)) helperPath = bundled;
    }
#endif
    using var engine = new Engine(o.Device == "gpu", o.NoRails, helperPath);
    // LHM utilization uses differences between updates. Discard the warmup poll.
    engine.Read(trackStatistics: false); Thread.Sleep(500);
    Snapshot Read() => Filter(engine.Read(), o);
    if (o.Command == "devices")
    {
        var snapshot = Read();
        if (o.Json) Console.WriteLine(JsonSerializer.Serialize(snapshot.Devices, JsonOutput.Pretty));
        else foreach (var d in snapshot.Devices) {
            Console.WriteLine($"{d.Id}  {d.Name} ({d.Kind})\n  {d.Source}");
            if (d.Metadata != null) foreach (var field in d.Metadata) Console.WriteLine($"  {field.Key}: {field.Value}");
        }
    }
    else if (o.Command == "coverage")
    {
        var coverage = Coverage.Compare(o.Reference!, Read());
        if (o.Json) Console.WriteLine(JsonSerializer.Serialize(coverage, JsonOutput.Pretty));
        else {
            foreach (var row in coverage) Console.WriteLine($"{row.Match,-11} {row.HwinfoMetric}\n  {row.SourceMetric ?? row.Note} ({row.Status})");
            Console.WriteLine($"\n{coverage.Count} reference GPU columns. " + string.Join("; ", coverage.GroupBy(r => r.Match).Select(g => $"{g.Key}: {g.Count()}")));
        }
    }
    else if (o.Command == "export")
    {
        var snapshot = Read();
        using var writer = new StreamWriter(new FileStream(Path.GetFullPath(o.Output!), o.Force ? FileMode.Create : FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(snapshot, JsonOutput.Pretty));
        Console.Error.WriteLine($"Saved {snapshot.Sensors.Count} readings to {Path.GetFullPath(o.Output!)}");
    }
    else if (o.Command is "watch" or "log")
    {
        using var logger = o.Command == "log" ? new LogWriter(o.Output!, o.Force, o.Format == "jsonl", o.Wide) : null;
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try {
            var watch = Stopwatch.StartNew(); long count = 0; double next = 0;
            while (!cancel.IsCancellationRequested && (!o.Samples.HasValue || count < o.Samples) && (!o.Duration.HasValue || watch.Elapsed.TotalSeconds < o.Duration))
            {
                var snapshot = Read(); count++;
                if (logger != null) logger.Write(snapshot);
                else if (o.Json) Console.WriteLine(JsonSerializer.Serialize(snapshot, JsonOutput.Compact));
                else Display(snapshot, o.All, false);
                next += o.Interval;
                var delay = next - watch.Elapsed.TotalSeconds;
                if (delay < 0) next = watch.Elapsed.TotalSeconds;
                else {
                    if (o.Duration.HasValue) delay = Math.Min(delay, Math.Max(0, o.Duration.Value - watch.Elapsed.TotalSeconds));
                    cancel.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(delay));
                }
            }
            if (logger != null) Console.Error.WriteLine($"Saved {count} samples to {Path.GetFullPath(o.Output!)}");
        } finally { Console.CancelKeyPress -= handler; }
    }
    else
    {
        var snapshot = Read();
        if (o.Json) Console.WriteLine(JsonSerializer.Serialize(o.Command == "diag" ? (object)snapshot.Diagnostics : snapshot, JsonOutput.Pretty));
        else if (o.Command == "diag") Diagnostics(snapshot);
        else Display(snapshot, o.All, o.Command == "sensors");
    }
    return 0;
}
catch (Exception e) when (e is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException or InvalidOperationException)
{
    Console.Error.WriteLine("hwmon: " + e.Message); return 2;
}

static Snapshot Filter(Snapshot s, CliOptions o)
{
    if (o.Device == "all") return s;
    var ids = s.Devices.Where(d => d.Kind.Equals(o.Device, StringComparison.OrdinalIgnoreCase) || d.Id == o.Device).Select(d => d.Id).ToHashSet();
    if (ids.Count == 0) throw new ArgumentException($"No device matches '{o.Device}'. Use hwmon devices for IDs.");
    return s with { Devices = s.Devices.Where(d => ids.Contains(d.Id)).ToList(), Sensors = s.Sensors.Where(r => ids.Contains(r.DeviceId)).ToList(),
        Diagnostics = s.Diagnostics.Where(d => s.Devices.Any(v => ids.Contains(v.Id) && v.Name == d.Device) || d.Code == "backend-unavailable").ToList() };
}
static void Display(Snapshot s, bool all, bool full)
{
    Console.WriteLine($"\nhwmon  {s.Timestamp:yyyy-MM-dd HH:mm:ss zzz}");
    foreach (var group in s.Sensors.GroupBy(r => r.DeviceId))
    {
        var rows = group.Where(r => all || r.Status == "ok" || r.Status == "invalid").ToList();
        if (!full && !all)
        {
            var preferred = new[] { "GPU Core temperature", "GPU Hot Spot temperature", "GPU Memory Junction temperature", "GPU Core Voltage",
                "GPU core temperature (NVML)", "GPU power (NVML reported)", "GPU core utilization (NVML)",
                "GPU memory clock (driver reported)", "GPU memory total", "GPU memory free", "GPU memory used (excluding reserved)", "GPU memory used (v1 native accounting)",
                "GPU core clock (NVML)", "GPU memory clock (HWiNFO convention)", "GPU video clock", "GPU Crossbar Clock",
                "GPU Package power", "Total Board Input Channel 0 Power", "GPU power limit (maximum)",
                "GPU Core load", "GPU Memory Controller load", "GPU Memory Free", "GPU Memory Used", "GPU Memory",
                "CPU Total", "CPU Package", "Memory", "Battery Level" };
            var kind = s.Devices.FirstOrDefault(d => d.Id == group.Key)?.Kind;
            rows = rows.Where(r => preferred.Contains(r.Name, StringComparer.Ordinal) ||
                r.Name.StartsWith("Performance Limit - ") ||
                (kind == "memory" && r.Unit is "GiB" or "%") ||
                (kind == "network" && r.Unit == "B/s") ||
                (kind == "battery" && r.Unit is "%" or "W" or "s") ||
                (kind == "cpu" && (r.Name.Contains("Average") || r.Name == "CPU Total" || r.Unit == "°C")) ||
                (kind == "storage" && r.Unit is "B/s" or "°C")).Take(kind == "gpu" ? 32 : 16).ToList();
        }
        if (rows.Count == 0) continue;
        Console.WriteLine($"\n{group.First().Device} [{group.Key}]");
        Console.WriteLine($"  {"Metric",-48} {"Current",12} {"Min",11} {"Max",11} {"Average",11}  Source / status");
        foreach (var row in rows)
            Console.WriteLine($"  {row.Name,-48} {Value(row.Value, row.Unit),12} {Value(row.Minimum, ""),11} {Value(row.Maximum, ""),11} {Value(row.Average, ""),11}  {row.Source} / {row.Status}");
    }
    if (!full && !all) Console.WriteLine("\nUse sensors for all available channels; --all includes unavailable/unsupported channels; --json includes IDs, raw values, and notes.");
    Diagnostics(s);
}
static string Value(double? value, string unit) => value.HasValue ? unit == "bool" ? value.Value == 0 ? "No" : "Yes" :
    value.Value.ToString("0.###", CultureInfo.InvariantCulture) + (unit.Length > 0 ? " " + unit : "") : "--";
static void Diagnostics(Snapshot s)
{
    if (s.Diagnostics.Count == 0) Console.WriteLine("No active diagnostic findings in this sample.");
    foreach (var d in s.Diagnostics) Console.WriteLine($"[{d.Severity}] {d.Device}: {d.Message}");
}
static void Help() => Console.WriteLine("""
hwmon - direct hardware sensors, logging, and diagnostics

  hwmon                         Quick overview
  hwmon gpu                     GPU overview
  hwmon cpu                     CPU overview
  hwmon sensors --gpu --all      All GPU channels, including unsupported status
  hwmon devices                 Hardware inventory, driver, VBIOS, PCI metadata
  hwmon diag --gpu               Data-quality and active limiter diagnostics
  hwmon doctor                  Driver/permission setup checks
  hwmon watch --gpu --interval 1s
  hwmon log run.csv --gpu --interval 1s --duration 30s
  hwmon log run.jsonl --format jsonl --samples 10
  hwmon log wide.csv --gpu --wide --samples 10
  hwmon export snapshot.json
  hwmon coverage --reference HWiNFO.csv --json
  hwmon self-test                Run offline data-quality and format checks

Options: --json, --gpu, --cpu, --device ID, --all, --no-rails, --force
WSL: --windows-helper /path/to/windows/hwmon.exe adds direct host GPU sensors.
The WSL bundle detects windows/hwmon.exe automatically; --native disables it.
Interval: 100ms minimum; duration accepts ms/s/m/h. Ctrl+C stops and flushes logs.
Logging runs continuously when --duration/--samples is omitted; there is no 12h timer.
Default CSV uses one row per sensor with IDs, units, source, status, raw values,
and min/max/average. --wide freezes the initial channel list for charting.
Unsupported, failed, and implausible readings have null/blank current values.
Windows CPU register readings need PawnIO/elevation; Linux uses /proc and /sys.
WSL CPU/RAM/disk counters describe the VM; physical sensor coverage is limited.
""");
