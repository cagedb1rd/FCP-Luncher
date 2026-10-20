namespace FCP.Core.Instances;

public enum LoaderType
{
    Forge,
    NeoForge,
    Fabric,
    Quilt,
    OptiFine,
    LiteLoader,
}

public sealed record LoaderInfo(LoaderType Type, string? Version)
{
    public string DisplayName => Type switch
    {
        LoaderType.NeoForge => "NeoForge",
        LoaderType.OptiFine => "OptiFine",
        LoaderType.LiteLoader => "LiteLoader",
        _ => Type.ToString(),
    } + (string.IsNullOrEmpty(Version) ? "" : " " + Version);
}

/// <summary>原版的版本类型。</summary>
public enum GameVersionType
{
    Release,
    Snapshot,
    Old,
    AprilFools,
    Unknown,
}

public enum InstanceHealth
{
    Ok,
    /// <summary>JSON 缺失、损坏或没有 mainClass。</summary>
    Broken,
    /// <summary>继承的前置版本不存在。</summary>
    MissingParent,
    /// <summary>缺少游戏本体（版本 jar）。FCP 不提供游戏本体下载（D-027）。</summary>
    MissingGameJar,
}

/// <summary>版本列表中的分组（顺序与 PCL 一致）。</summary>
public enum InstanceGroup
{
    Favorite,
    Modded,
    Regular,
    Rare,
    AprilFools,
    Broken,
    Hidden,
}

/// <summary>一个已安装的版本（实例）。</summary>
public sealed record GameInstance
{
    public required string RootDirectory { get; init; }
    public required string Name { get; init; }
    public string VersionDirectory => Path.Combine(RootDirectory, "versions", Name);

    /// <summary>实际使用的版本 JSON 路径（可能不是同名文件）。</summary>
    public string? JsonPath { get; init; }

    /// <summary>原版版本号，例如 1.20.1、26.1、24w14a；无法识别时为 null。</summary>
    public string? MinecraftVersion { get; init; }

    public GameVersionType VersionType { get; init; } = GameVersionType.Unknown;
    public DateTimeOffset? ReleaseTime { get; init; }
    public IReadOnlyList<LoaderInfo> Loaders { get; init; } = [];
    public string? InheritsFrom { get; init; }

    /// <summary>JSON 中声明的推荐 Java 主版本（javaVersion.majorVersion，沿继承链取值）。</summary>
    public int? RecommendedJavaMajor { get; init; }

    public bool IsHmclFormat { get; init; }
    public InstanceHealth Health { get; init; } = InstanceHealth.Ok;

    /// <summary>不健康时的原因（中文，可以直接显示给用户）。</summary>
    public string? Problem { get; init; }

    public bool IsModded => Loaders.Any(l => l.Type is not LoaderType.OptiFine);

    /// <summary>主要的加载器（用于图标和分组）。</summary>
    public LoaderInfo? PrimaryLoader =>
        Loaders.FirstOrDefault(l => l.Type is LoaderType.NeoForge) ??
        Loaders.FirstOrDefault(l => l.Type is LoaderType.Forge) ??
        Loaders.FirstOrDefault(l => l.Type is LoaderType.Fabric) ??
        Loaders.FirstOrDefault(l => l.Type is LoaderType.Quilt) ??
        Loaders.FirstOrDefault(l => l.Type is LoaderType.LiteLoader) ??
        Loaders.FirstOrDefault(l => l.Type is LoaderType.OptiFine);

    /// <summary>简短描述，例如“1.20.1 · Forge 47.4.10”。</summary>
    public string Description
    {
        get
        {
            if (Health != InstanceHealth.Ok)
                return Problem ?? "无法读取该版本";
            var parts = new List<string> { MinecraftVersion ?? "未知版本" };
            parts.AddRange(Loaders.Select(l => l.DisplayName));
            return string.Join(" · ", parts);
        }
    }
}
