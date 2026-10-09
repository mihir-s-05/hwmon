using System.Text;

namespace Hwmon;

public sealed record CoverageRow(int Column, string Hardware, string HwinfoMetric, string Match,
    string? SourceMetric, string? SourceId, string Status, string Note);

public static class Coverage
{
    // Mappings were verified against this laptop's HWiNFO 8.16 log. Similar
    // labels alone do not establish general hardware or unit equivalence.
    private static readonly Dictionary<string, string> Names = new() {
        ["GPU Temperature"] = "GPU Core temperature",
        ["GPU Memory Junction Temperature"] = "GPU Memory Junction temperature",
        ["GPU Hot Spot Temperature"] = "GPU Hot Spot temperature",
        ["GPU Thermal Limit"] = "GPU Thermal Target 0",
        ["GPU Core Voltage"] = "GPU Core Voltage",
        ["GPU Rail Voltages (avg)"] = "GPU Rail Voltages (avg)",
        ["GPU FBVDD Input Voltage"] = "FBVDD Input Channel 3 Voltage",
        ["GPU 16-pin HVPWR Voltage"] = "16-pin HVPWR Channel 5 Voltage",
        ["GPU Power"] = "Total Board Input Channel 0 Power",
        ["GPU Rail Powers (avg)"] = "GPU Rail Powers (avg)",
        ["GPU Core (NVVDD) Input Power (sum)"] = "NVVDD Input Channel 1 Power",
        ["GPU FBVDD Input Power"] = "FBVDD Input Channel 3 Power",
        ["GPU 16-pin HVPWR Power"] = "16-pin HVPWR Channel 5 Power",
        ["GPU PCIe +3.3V Input Power (est)"] = "PCIe 3.3V Input Channel 10 Power",
        ["GPU Input PP Source Power (sum)"] = "PP Source Input Channel 12 Power",
        ["GPU Clock"] = "GPU Core clock",
        ["GPU Memory Clock"] = "GPU memory clock (HWiNFO convention)",
        ["GPU Video Clock"] = "GPU video clock",
        ["GPU Crossbar Clock"] = "GPU Crossbar Clock",
        ["GPU Core Load"] = "GPU Core load",
        ["GPU Memory Controller Load"] = "GPU Memory Controller load",
        ["GPU Video Engine Load"] = "GPU Video Engine load",
        ["GPU Bus Load"] = "GPU Bus load",
        ["GPU Memory Usage"] = "GPU Memory",
        ["GPU D3D Usage"] = "D3D 3D load",
        ["GPU Video Decode 0 Usage"] = "D3D Video Decode load",
        ["GPU Video Encode 0 Usage"] = "D3D Video Encode load",
        ["GPU VR Usage"] = "D3D VR load",
        ["GPU Memory Available"] = "GPU Memory Free",
        ["GPU Memory Allocated"] = "GPU Memory Used",
        ["GPU D3D Memory Dedicated"] = "D3D Dedicated Memory Used",
        ["GPU D3D Memory Dynamic"] = "D3D Shared Memory Used",
        ["PCIe Link Speed"] = "PCIe Link Speed",
        ["GPU D3D Usages (avg)"] = "GPU D3D engine average (all exposed engines)",
        ["GPU Performance Limiters (avg)"] = "GPU Performance Limiters (any active)"
    };
    public static List<CoverageRow> Compare(string path, Snapshot snapshot)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(bytes); }
        var records = ParseCsv(text);
        var header = records.LastOrDefault(r => r.Length >= 2 && r[0] == "Date" && r[1] == "Time")
            ?? throw new ArgumentException("Reference must be an HWiNFO CSV with Date,Time headers.");
        var hardware = records.LastOrDefault(r => r.Length == header.Length && r[0] == "" && r[1] == "") ?? new string[header.Length];
        var result = new List<CoverageRow>(); var outputChannels = new Dictionary<string, int>();
        for (var i = 2; i < header.Length; i++)
        {
            var deviceName = hardware[i] ?? "";
            if (!deviceName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) continue;
            var label = header[i]; var bracket = label.LastIndexOf(" [", StringComparison.Ordinal);
            var name = bracket < 0 ? label : label[..bracket];
            var mapped = Names.GetValueOrDefault(name) ?? (name.StartsWith("Performance Limit - ") ? name : null);
            if (name == "GPU Core (NVVDD) Output Power") {
                var channel = outputChannels.GetValueOrDefault(deviceName);
                mapped = $"NVVDD Output Channel {18 + channel} Power"; outputChannels[deviceName] = channel + 1;
            }
            var candidates = snapshot.Sensors.Where(s => s.DeviceId.StartsWith("nvidia/") &&
                s.Name.Equals(mapped, StringComparison.OrdinalIgnoreCase)).ToList();
            // This supplied log has one discrete GPU. Match model for future logs.
            var model = deviceName[(deviceName.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase))..].Replace("NVIDIA ", "").Trim();
            var matching = candidates.Where(s => s.Device.Replace("NVIDIA ", "").Replace(" GPU", "").Trim() == model).ToList();
            candidates = matching;
            var sensor = candidates.FirstOrDefault();
            bool aggregate = name is "GPU D3D Usages (avg)" or "GPU Performance Limiters (avg)";
            var note = sensor == null ? "No corresponding channel exposed in this session." :
                aggregate ? "Comparable derived aggregate; exact HWiNFO grouping/boolean aggregate semantics are not established." :
                "Corresponding direct or derived channel on the verified RTX 4070 Laptop. Availability is reported separately.";
            result.Add(new(i, deviceName, label, sensor == null ? "missing" : aggregate ? "comparable" : "mapped", sensor?.Name, sensor?.Id,
                sensor?.Status ?? "unavailable", note));
        }
        if (result.Count == 0) throw new ArgumentException("No NVIDIA GPU columns found in reference hardware footer.");
        return result;
    }
    public static List<string[]> ParseCsv(string text)
    {
        var records = new List<string[]>(); var fields = new List<string>(); var field = new StringBuilder(); bool quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else if ((c == '\r' || c == '\n') && !quoted) {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString()); field.Clear(); records.Add(fields.ToArray()); fields.Clear();
            }
            else field.Append(c);
        }
        if (quoted) throw new ArgumentException("Unterminated quoted field in CSV.");
        if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); records.Add(fields.ToArray()); }
        return records;
    }
}
