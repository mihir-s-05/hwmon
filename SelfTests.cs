namespace Hwmon;

public static class SelfTests
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool yes, string message) { checks++; if (!yes) throw new InvalidOperationException("Self-test failed: " + message); }
        SensorReading Row(string id, string name, string unit, double? value, string status = "ok") =>
            new("nvidia/0/" + id, "nvidia/0", "NVIDIA GeForce RTX 4070 Laptop GPU", name, unit, "Test", value, status);
        var rules = new TelemetryRules();
        var limit = Row("limit", "GPU power limit (maximum)", "W", 140);
        var good = rules.Apply([limit, Row("power", "GPU Power", "W", 14.3)]).Last();
        Check(good.Samples == 1 && good.Average == 14.3, "normal sample stats");
        var bad = rules.Apply([limit, Row("power", "GPU Power", "W", 590.006)]).Last();
        Check(bad.Value == null && bad.RawValue == 590.006 && bad.Status == "invalid", "raw power anomaly preserved");
        Check(bad.Samples == 1 && bad.Maximum == 14.3, "anomaly excluded from statistics");
        var failed = rules.Apply([Row("power", "GPU Power", "W", null, "error")]).Single();
        Check(failed.Value == null && failed.Average == 14.3 && failed.Samples == 1, "failure is not zero or sample");
        var asleep = rules.Apply([Row("temp", "GPU core temperature", "°C", 0)]).Single();
        Check(asleep.Status == "invalid" && asleep.Value == null && asleep.Samples == 0, "sleep zero temperature rejected");
        var rows = new List<SensorReading> { Row("rails/3.voltage", "FBVDD", "V", 20), Row("rails/5.voltage", "HVPWR", "V", 22) };
        var derived = Engine.Derive(rows).Single(s => s.Id.EndsWith("rail-voltage-average"));
        Check(derived.Value == 21 && derived.Status == "ok", "verified voltage mean");
        rows[1] = rows[1] with { Value = null, Status = "unavailable" };
        derived = Engine.Derive(rows).Single(s => s.Id.EndsWith("rail-voltage-average"));
        Check(derived.Value == null && derived.Status == "unavailable", "missing aggregate input not averaged away");
        var csv = Coverage.ParseCsv("a,\"b,c\",\"line\nnext\",\"a\"\"b\"\r\n");
        Check(csv.Count == 1 && csv[0].Length == 4 && csv[0][1] == "b,c" && csv[0][2] == "line\nnext" && csv[0][3] == "a\"b", "quoted multiline CSV parser");
        Check(LogWriter.Escape("a,\"b") == "\"a,\"\"b\"", "CSV writer escaping");
        Check(CliOptions.Parse(["log", "a.csv", "--duration", "2m", "--interval", "100ms"]).Duration == 120, "duration units");
        Check(CliOptions.Parse(["gpu"]).Device == "gpu", "GPU command alias");
        Check(CliOptions.Parse(["gpu", "--windows-helper", "/mnt/c/hwmon/hwmon.exe"]).WindowsHelper == "/mnt/c/hwmon/hwmon.exe", "explicit WSL helper path");
        var fixture = Path.Combine(Path.GetTempPath(), "hwmon-linux-fixture-" + Guid.NewGuid());
        try {
            void Write(string path, string contents) { var p = Path.Combine(fixture, path); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, contents); }
            Write("proc/stat", "cpu 100 0 100 800 0 0 0 0\ncpu0 100 0 100 800 0 0 0 0\n");
            Write("proc/cpuinfo", "processor : 0\nmodel name : Fixture CPU\ncpu MHz : 2500\n");
            Write("proc/meminfo", "MemTotal: 8388608 kB\nMemAvailable: 6291456 kB\n");
            Write("proc/net/dev", "header\nheader\neth0: 1000 0 0 0 0 0 0 0 2000 0 0 0 0 0 0 0\n");
            Write("proc/diskstats", "8 0 sda 1 0 100 0 1 0 200 0 0 0 0\n");
            Write("sys/class/hwmon/hwmon0/name", "coretemp");
            Write("sys/class/hwmon/hwmon0/temp1_input", "45000");
            Write("sys/class/hwmon/hwmon0/temp1_label", "Package");
            Write("sys/class/hwmon/hwmon0/power1_input", "40000000");
            using var linux = new Hwmon.Backends.LinuxBackend(fixture);
            var first = linux.Read();
            Check(first.Single(s => s.Name == "Package input").Value == 45, "millidegree temperature ABI");
            Check(first.Single(s => s.Id.EndsWith("power1_input")).Value == 40, "microwatt power ABI");
            Check(first.Single(s => s.Name == "Memory load").Value == 25, "memory available accounting");
            Write("proc/stat", "cpu 150 0 100 850 0 0 0 0\ncpu0 150 0 100 850 0 0 0 0\n");
            Write("proc/net/dev", "header\nheader\neth0: 500 0 0 0 0 0 0 0 1000 0 0 0 0 0 0 0\n");
            Write("sys/class/hwmon/hwmon0/temp1_fault", "1");
            var second = linux.Read();
            Check(second.Single(s => s.Name == "CPU Total").Value == 50, "CPU utilization excludes idle");
            Check(second.Single(s => s.Name == "Network receive rate").Value == null, "reset kernel counter is not negative throughput");
            Check(second.Single(s => s.Name == "Package input") is { Value: null, RawValue: 45000, Status: "unavailable" }, "faulted hwmon input preserves raw reading");
        } finally {
            var resolved = Path.GetFullPath(fixture);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("hwmon-linux-fixture-"))
                throw new InvalidOperationException("Test cleanup path outside its temporary directory.");
            Directory.Delete(resolved, recursive: true);
        }
        var tmp = Path.Combine(Path.GetTempPath(), "hwmon-test-" + Guid.NewGuid() + ".csv");
        try {
            var snapshot = new Snapshot(DateTimeOffset.Now, [], [failed, bad], [], []);
            using (var writer = new LogWriter(tmp, false)) writer.Write(snapshot);
            var data = Coverage.ParseCsv(File.ReadAllText(tmp));
            Check(data.Count == 3 && data[1][5] == "" && double.Parse(data[2][11], System.Globalization.CultureInfo.InvariantCulture) == 590.006, "CSV retains blanks and raw anomalies");
            bool collision = false;
            try { using var writer = new LogWriter(tmp, false); } catch (IOException) { collision = true; }
            Check(collision, "logs do not overwrite existing files");
        } finally { if (File.Exists(tmp)) File.Delete(tmp); }
        Console.WriteLine($"Passed {checks} offline checks."); return 0;
    }
}
