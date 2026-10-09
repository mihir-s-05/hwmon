using System.Reflection;

namespace Hwmon;

public static class VersionInfo
{
    public static string Value => typeof(VersionInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(VersionInfo).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
