namespace Hwmon;

public sealed class TelemetryRules
{
    private sealed class Stats
    {
        public double Min = double.PositiveInfinity, Max = double.NegativeInfinity, Mean;
        public long Count;
        public void Add(double value) { Min = Math.Min(Min, value); Max = Math.Max(Max, value); Mean += (value - Mean) / ++Count; }
    }
    private readonly Dictionary<string, Stats> _stats = new();

    public List<SensorReading> Apply(List<SensorReading> input, bool trackStatistics = true)
    {
        var limits = input.Where(s => s.Name == "GPU power limit (maximum)" && s.Status == "ok" && s.Value > 0)
            .GroupBy(s => s.DeviceId).ToDictionary(g => g.Key, g => g.Max(s => s.Value!.Value));
        var output = new List<SensorReading>();
        foreach (var initial in input)
        {
            var sensor = initial;
            if (sensor.Value is double value)
            {
                string? problem = null;
                if (!double.IsFinite(value)) problem = "Non-finite native reading.";
                if (sensor.DeviceId.StartsWith("nvidia/") && sensor.Unit == "°C" && value == 0 &&
                    !sensor.Name.Contains("threshold", StringComparison.OrdinalIgnoreCase) &&
                    !sensor.Name.Contains("limit", StringComparison.OrdinalIgnoreCase))
                    problem = "Zero GPU temperature is unavailable/sleep data on this machine; it is not included in statistics.";
                if (sensor.Unit == "W" && sensor.DeviceId.StartsWith("nvidia/") &&
                    !sensor.Name.Contains("limit", StringComparison.OrdinalIgnoreCase) &&
                    limits.TryGetValue(sensor.DeviceId, out var maximum) && (value < 0 || value > maximum * 1.5))
                    problem = $"Native power {value:0.###} W exceeds the reported maximum {maximum:0.###} W by >50%; retained as raw data.";
                if (sensor.Unit == "%" && !sensor.Name.Contains("topology", StringComparison.OrdinalIgnoreCase) &&
                    !sensor.Name.Contains("Power Target", StringComparison.OrdinalIgnoreCase) &&
                    !sensor.Name.Contains("intended", StringComparison.OrdinalIgnoreCase) && (value < 0 || value > 105))
                    problem = "Percentage outside the expected range.";
                if (problem != null) sensor = sensor with { Value = null, RawValue = double.IsFinite(value) ? value : null, Status = "invalid", Note = problem };
            }
            if (!_stats.TryGetValue(sensor.Id, out var stats)) _stats[sensor.Id] = stats = new();
            if (trackStatistics && sensor.Status == "ok" && sensor.Value is double good) stats.Add(good);
            output.Add(sensor with { Minimum = stats.Count > 0 ? stats.Min : null,
                Maximum = stats.Count > 0 ? stats.Max : null, Average = stats.Count > 0 ? stats.Mean : null, Samples = stats.Count });
        }
        return output;
    }
    public static List<Diagnostic> Diagnose(List<SensorReading> sensors, Dictionary<string, string?> backends)
    {
        var results = new List<Diagnostic>();
        foreach (var backend in backends.Where(b => b.Value != null))
            results.Add(new("warning", "backend-unavailable", backend.Key, backend.Value!));
        foreach (var device in sensors.GroupBy(s => s.DeviceId))
        {
            var rows = device.ToList(); var name = rows[0].Device;
            foreach (var alarm in rows.Where(s => s.Source == "Linux kernel" && s.Status == "ok" && s.Value == 1 && s.Unit == "bool"))
                results.Add(new("warning", "kernel-sensor-alarm", name, $"Kernel sensor {alarm.Name} is active. Check its driver-reported fault/alarm state."));
            var invalid = rows.Count(s => s.Status == "invalid");
            var failed = rows.Count(s => s.Status is "error" or "unavailable");
            if (invalid > 0) results.Add(new("warning", "invalid-readings", name, $"{invalid} implausible readings were excluded from statistics. Raw values are in JSON/CSV."));
            if (failed > 0) results.Add(new("info", "unavailable-readings", name, $"{failed} readings are currently unavailable or failed; they are not zero."));
            if (rows.Any(s => s.Status == "permission")) results.Add(new("warning", "hardware-access", name,
                device.Key.StartsWith("windows/disk/") ? "Windows denied storage reliability/SMART access; disk activity counters remain available. An elevated terminal may expose more data." :
                "Low-level readings require the hardware driver and/or administrator access. See hwmon doctor."));
            foreach (var thermal in rows.Where(s => s.Status == "ok" && s.Value == 1 &&
                (s.Name.Contains("thermal slowdown", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Performance Limit - Thermal", StringComparison.OrdinalIgnoreCase))))
                results.Add(new("warning", "thermal-limit-active", name, $"{thermal.Name} is active ({thermal.Source})."));
            foreach (var power in rows.Where(s => s.Status == "ok" && s.Value == 1 &&
                (s.Name.Contains("software power cap", StringComparison.OrdinalIgnoreCase) || s.Name == "Performance Limit - Power")))
                results.Add(new("info", "power-limit-active", name, $"{power.Name} is active ({power.Source}). This can be normal under load."));
            var core = rows.FirstOrDefault(s => s.Status == "ok" && s.Name == "GPU Core temperature");
            var target = rows.FirstOrDefault(s => s.Status == "ok" && s.Name == "GPU Thermal Target 0");
            if (core?.Value is double temperature && target?.Value is double thermalTarget && temperature >= thermalTarget)
                results.Add(new("warning", "temperature-at-target", name, $"Core temperature {temperature:0.#}°C is at or above the driver thermal target {thermalTarget:0.#}°C."));
        }
        return results;
    }
}
