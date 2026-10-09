using System.Globalization;

namespace Hwmon;

public sealed class CliOptions
{
    public string Command = "show", Device = "all", Format = "csv";
    public string? Output, Reference, WindowsHelper;
    public bool Json, All, NoRails, Force, Wide, NativeOnly;
    public double Interval = 1;
    public double? Duration;
    public long? Samples;
    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        var i = 0;
        if (args.Length > 0 && !args[0].StartsWith('-')) o.Command = args[i++].ToLowerInvariant();
        if (o.Command == "gpu") { o.Command = "show"; o.Device = "gpu"; }
        if (o.Command == "cpu") { o.Command = "show"; o.Device = "cpu"; }
        while (i < args.Length)
        {
            var arg = args[i++];
            string Value() => i < args.Length ? args[i++] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg)
            {
                case "--json": o.Json = true; break;
                case "--all": o.All = true; break;
                case "--no-rails": o.NoRails = true; break;
                case "--windows-helper": o.WindowsHelper = Value(); break;
                case "--native": o.NativeOnly = true; break;
                case "--gpu": o.Device = "gpu"; break;
                case "--cpu": o.Device = "cpu"; break;
                case "--device": o.Device = Value(); break;
                case "--format": o.Format = Value().ToLowerInvariant(); break;
                case "--out": case "--output": o.Output = Value(); break;
                case "--reference": o.Reference = Value(); break;
                case "--interval": o.Interval = Seconds(Value()); break;
                case "--duration": o.Duration = Seconds(Value()); break;
                case "--samples": o.Samples = long.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--force": o.Force = true; break;
                case "--wide": o.Wide = true; break;
                case "--help": case "-h": o.Command = "help"; break;
                case "--version": o.Command = "version"; break;
                default:
                    if (!arg.StartsWith('-') && o.Output == null && o.Command is "log" or "export") o.Output = arg;
                    else throw new ArgumentException($"Unknown argument: {arg}");
                    break;
            }
        }
        if (o.Interval < 0.1 || !double.IsFinite(o.Interval)) throw new ArgumentException("Interval must be at least 100ms.");
        if (o.Duration is <= 0 || o.Duration is double d && !double.IsFinite(d)) throw new ArgumentException("Duration must be positive.");
        if (o.Samples is <= 0) throw new ArgumentException("Samples must be positive.");
        if (o.Format is not ("csv" or "jsonl")) throw new ArgumentException("Log format must be csv or jsonl.");
        if (o.Wide && o.Format != "csv") throw new ArgumentException("--wide requires CSV.");
        if (o.NativeOnly && o.WindowsHelper != null) throw new ArgumentException("Choose --native or --windows-helper, not both.");
        return o;
    }
    private static double Seconds(string text)
    {
        var s = text.ToLowerInvariant(); double factor = 1;
        if (s.EndsWith("ms")) { factor = .001; s = s[..^2]; }
        else if (s.EndsWith('s')) s = s[..^1];
        else if (s.EndsWith('m')) { factor = 60; s = s[..^1]; }
        else if (s.EndsWith('h')) { factor = 3600; s = s[..^1]; }
        return double.Parse(s, CultureInfo.InvariantCulture) * factor;
    }
}
