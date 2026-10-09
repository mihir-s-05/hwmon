namespace Hwmon;

public sealed record SensorReading(
    string Id, string DeviceId, string Device, string Name, string Unit,
    string Source, double? Value, string Status = "ok", string? Note = null,
    double? RawValue = null)
{
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? Average { get; init; }
    public long Samples { get; init; }
    public DateTimeOffset? SampledAt { get; init; }
}

public sealed record DeviceInfo(string Id, string Name, string Kind, string Source,
    Dictionary<string, string>? Metadata = null);

public interface ISensorBackend : IDisposable
{
    string Name { get; }
    IReadOnlyList<DeviceInfo> Devices { get; }
    string? InitializationError { get; }
    List<SensorReading> Read();
}

public sealed record Diagnostic(string Severity, string Code, string Device, string Message);
public sealed record Snapshot(DateTimeOffset Timestamp, List<DeviceInfo> Devices,
    List<SensorReading> Sensors, List<Diagnostic> Diagnostics,
    Dictionary<string, string?> Backends);

public static class DeviceIdentity
{
    public static string Nvidia(uint index) => $"nvidia/{index}";
}
