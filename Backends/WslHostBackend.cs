using System.Diagnostics;
using System.Text.Json;

namespace Hwmon.Backends;

/// <summary>Optional WSL interop to the same read-only Windows CLI, using direct driver APIs.</summary>
public sealed class WslHostBackend : ISensorBackend
{
    public string Name => "Windows host";
    public string? InitializationError { get; private set; }
    public IReadOnlyList<DeviceInfo> Devices => _devices;
    private List<DeviceInfo> _devices = [];
    private readonly string _path;
    private readonly bool _noRails;
    private bool _quarantined;
    public WslHostBackend(string path, bool noRails)
    {
        _path = Path.GetFullPath(path); _noRails = noRails;
        if (!PlatformInfo.IsWsl) { InitializationError = "--windows-helper requires WSL interop."; _quarantined = true; }
        else if (!File.Exists(_path)) { InitializationError = "Windows helper not found: " + _path; _quarantined = true; }
    }
    public List<SensorReading> Read()
    {
        if (_quarantined) return [];
        try {
            var start = new ProcessStartInfo(_path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "sensors", "--gpu", "--json" }) start.ArgumentList.Add(arg);
            if (_noRails) start.ArgumentList.Add("--no-rails");
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot launch Windows helper.");
            var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(25000)) { child.Kill(entireProcessTree: true); child.WaitForExit(); throw new TimeoutException("Windows helper exceeded 25 seconds; disabled for this session."); }
            var json = output.GetAwaiter().GetResult(); var stderr = errors.GetAwaiter().GetResult();
            if (child.ExitCode != 0) throw new InvalidOperationException($"Windows helper exit {child.ExitCode}: {stderr.Trim()}");
            var snapshot = JsonSerializer.Deserialize<Snapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty Windows host snapshot.");
            string Id(string id) => id.StartsWith("nvidia/") ? id : "host/" + id;
            _devices = snapshot.Devices.Select(d => d with { Id = Id(d.Id), Source = Name }).ToList();
            InitializationError = string.Join("; ", snapshot.Backends.Where(b => b.Value != null).Select(b => b.Key + ": " + b.Value));
            if (InitializationError.Length == 0) InitializationError = null;
            return snapshot.Sensors.Select(s => s with {
                Id = Id(s.DeviceId) + "/host/" + s.Id, DeviceId = Id(s.DeviceId), Source = "Windows host / " + s.Source,
                Note = (s.Note == null ? "" : s.Note + " ") + $"Windows host sampled {snapshot.Timestamp:O} via direct APIs.",
                Minimum = null, Maximum = null, Average = null, Samples = 0,
                SampledAt = s.SampledAt ?? snapshot.Timestamp
            }).ToList();
        } catch (Exception e) { _quarantined = true; InitializationError = e.Message; return []; }
    }
    public void Dispose() { }
}
