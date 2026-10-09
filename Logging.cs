using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Hwmon;

public static class JsonOutput
{
    public static readonly JsonSerializerOptions Compact = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static readonly JsonSerializerOptions Pretty = new(Compact) { WriteIndented = true };
}

public sealed class LogWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly bool _json, _wide;
    private List<string>? _ids;
    public LogWriter(string path, bool force, bool json = false, bool wide = false)
    {
        _writer = new(new FileStream(Path.GetFullPath(path), force ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        _json = json; _wide = wide;
        if (!_json && !_wide) _writer.WriteLine("timestamp,deviceId,device,metric,unit,value,minimum,maximum,average,samples,status,rawValue,source,id,note,sampledAt");
    }
    public static string Escape(string? s)
    {
        s ??= "";
        return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
    private static string Number(double? v) => v?.ToString("G17", CultureInfo.InvariantCulture) ?? "";
    public void Write(Snapshot snapshot)
    {
        if (_json) _writer.WriteLine(JsonSerializer.Serialize(snapshot, JsonOutput.Compact));
        else if (_wide)
        {
            if (_ids == null)
            {
                _ids = snapshot.Sensors.Select(s => s.Id).ToList();
                var header = new List<string> { "timestamp" };
                foreach (var s in snapshot.Sensors)
                {
                    header.Add($"{s.Device} | {s.Name} [{s.Unit}] | {s.Id}");
                    header.Add(s.Id + ".status"); header.Add(s.Id + ".raw");
                }
                _writer.WriteLine(string.Join(',', header.Select(Escape)));
            }
            var map = snapshot.Sensors.ToDictionary(s => s.Id);
            var fields = new List<string> { snapshot.Timestamp.ToString("O") };
            foreach (var id in _ids)
            {
                map.TryGetValue(id, out var s);
                fields.Add(Number(s?.Value)); fields.Add(s?.Status ?? "unavailable"); fields.Add(Number(s?.RawValue));
            }
            _writer.WriteLine(string.Join(',', fields.Select(Escape)));
            // Dynamic sensors must never disappear silently from a fixed header.
            if (snapshot.Sensors.Any(s => !_ids.Contains(s.Id)))
                throw new InvalidOperationException("Sensor inventory changed during wide logging. Use long CSV or JSONL for dynamic devices; the completed rows remain saved.");
        }
        else foreach (var s in snapshot.Sensors)
            _writer.WriteLine(string.Join(',', new[] { snapshot.Timestamp.ToString("O"), s.DeviceId, s.Device, s.Name, s.Unit,
                Number(s.Value), Number(s.Minimum), Number(s.Maximum), Number(s.Average), s.Samples.ToString(CultureInfo.InvariantCulture),
                s.Status, Number(s.RawValue), s.Source, s.Id, s.Note, s.SampledAt?.ToString("O") }.Select(Escape)));
        _writer.Flush();
    }
    public void Dispose() => _writer.Dispose();
}
