using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Hwmon.Backends;

public sealed class RailsBackend : ISensorBackend
{
    public string Name => "NVAPI isolated worker";
    public string? InitializationError { get; private set; }
    public IReadOnlyList<DeviceInfo> Devices => _devices;
    private List<DeviceInfo> _devices = [];
    private string? _quarantined;

    public List<SensorReading> Read()
    {
        if (_quarantined != null) return [];
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate CLI executable.");
            var start = new ProcessStartInfo(executable) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
            start.ArgumentList.Add("--internal-rails");
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start native sensor worker.");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(8000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                throw new TimeoutException("Native worker exceeded 8 seconds; disabled for this CLI session.");
            }
            var output = stdout.GetAwaiter().GetResult();
            var error = stderr.GetAwaiter().GetResult();
            if (child.ExitCode != 0) throw new InvalidOperationException($"Native worker exited {child.ExitCode}; disabled for this session. {error.Trim()}");
            var rows = JsonSerializer.Deserialize<List<SensorReading>>(output) ?? throw new InvalidDataException("Empty native worker response.");
            _devices = rows.Where(s => s.DeviceId.StartsWith("nvidia/")).GroupBy(s => s.DeviceId)
                .Select(g => new DeviceInfo(g.Key, g.First().Device, "gpu", Name)).ToList();
            return rows;
        }
        catch (Exception e) { _quarantined = InitializationError = e.Message; return []; }
    }
    public void Dispose() { }
}
