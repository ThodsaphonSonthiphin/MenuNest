using System.Reflection;

namespace MenuNest.WebApi;

public readonly record struct VersionInfo(string Version, string Commit, string? BuildTime);

public static class BuildVersion
{
    public static VersionInfo Read(Assembly? asm)
    {
        var info = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var buildTime = asm?.GetCustomAttributes<AssemblyMetadataAttribute>()
                            .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value;
        return Parse(info, buildTime);
    }

    public static VersionInfo Parse(string? informational, string? buildTime)
    {
        var version = string.IsNullOrEmpty(informational) ? "0.0.0" : informational;
        var plus = version.IndexOf('+');
        var commit = plus >= 0 ? version[(plus + 1)..] : "local";
        return new VersionInfo(version, commit, string.IsNullOrEmpty(buildTime) ? null : buildTime);
    }
}
