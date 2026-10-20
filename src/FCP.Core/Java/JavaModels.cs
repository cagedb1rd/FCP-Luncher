namespace FCP.Core.Java;

/// <summary>Java 是从哪里发现的（显示在设置页的 Java 管理中）。</summary>
public enum JavaSource
{
    Registry,
    KnownFolder,
    MinecraftRuntime,
    OtherLauncher,
    PathVariable,
    JavaHome,
    Pcl,
    Downloaded,
    User,
}

/// <summary>一个可用的 Java。</summary>
public sealed record JavaRuntime
{
    /// <summary>java.exe 的完整路径。</summary>
    public required string JavaExecutable { get; init; }
    public required Version Version { get; init; }
    public string? Vendor { get; init; }
    public bool Is64Bit { get; init; } = true;
    public JavaSource Source { get; init; }

    public int Major => Version.Major;

    /// <summary>Java 主目录（bin 的上一级）。</summary>
    public string Home => Path.GetDirectoryName(Path.GetDirectoryName(JavaExecutable)!)!;

    /// <summary>javaw.exe（启动游戏时不弹出控制台窗口）。</summary>
    public string JavawExecutable => Path.Combine(Path.GetDirectoryName(JavaExecutable)!, "javaw.exe");

    public string DisplayName => $"Java {Version.ToString(Version.Build >= 0 ? 3 : 2)}" + (Vendor is null ? "" : $" · {Vendor}");
}

/// <summary>版本对 Java 的需求。</summary>
public sealed record JavaRequirement(int RecommendedMajor, int MinMajor, int? MaxMajor, string? MojangComponent, IReadOnlyList<string> Reasons)
{
    public bool Accepts(int major) => major >= MinMajor && (MaxMajor is null || major <= MaxMajor);

    public string RangeText => MaxMajor switch
    {
        null => $"Java {MinMajor} 或更高",
        var max when max == MinMajor => $"Java {MinMajor}",
        var max => $"Java {MinMajor} ~ {max}",
    };
}

/// <summary>选择结果：选中的 Java（可能为空）+ 给用户看的理由。</summary>
public sealed record JavaChoice(JavaRuntime? Java, JavaRequirement Requirement, string Explanation);
