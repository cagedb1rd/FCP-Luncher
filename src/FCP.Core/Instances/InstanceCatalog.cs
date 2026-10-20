using FCP.Core.Migration.Pcl;
using FCP.Core.Settings;
using FCP.Core.Storage;

namespace FCP.Core.Instances;

/// <summary>版本的元数据与独立设置，保存在 &lt;版本目录&gt;\.fcp\instance.json（跟随版本移动）。</summary>
public sealed class InstanceMeta
{
    public bool? IsFavorite { get; set; }
    public bool? IsHidden { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
    public long TotalPlaySeconds { get; set; }
    public int LaunchCount { get; set; }
    public InstanceSettings Settings { get; set; } = new();
}

/// <summary>界面使用的版本条目：版本本身 + 元数据 + 分组 + 实际生效的隔离设置。</summary>
public sealed record InstanceEntry(
    GameInstance Instance,
    InstanceMeta Meta,
    InstanceGroup Group,
    bool IsFavorite,
    bool IsIsolated,
    string? Description,
    DateTimeOffset? LastPlayed,
    PclInstanceInfo? Pcl)
{
    /// <summary>游戏目录（--gameDir）。</summary>
    public string GameDirectory => IsIsolated ? Instance.VersionDirectory : Instance.RootDirectory;
}

/// <summary>从 PCL 的版本设置中读到的信息（只读）。</summary>
/// <summary>
/// PCL 的版本设置（docs/02 §3）。启动时作为“我们的版本设置”之后、全局设置之前的一层：
/// 用户在 FCP 中没有单独设置过的项，沿用 PCL 中的设置（产品原则 4：不需要重新配置）。
/// </summary>
public sealed record PclInstanceInfo
{
    public bool? Isolated { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsHidden { get; init; }
    public string? CustomInfo { get; init; }

    /// <summary>VersionRamType=1 时的自定义内存（MB）。</summary>
    public int? MemoryMb { get; init; }

    /// <summary>VersionRamType=2（默认）：跟随全局。</summary>
    public bool MemoryFollowsGlobal { get; init; } = true;

    /// <summary>VersionRamType=0：这个版本使用自动内存（即使全局是自定义）。</summary>
    public bool MemoryAuto { get; init; }

    public int LoginRequirement { get; init; }
    public string? AuthServer { get; init; }
    public string? AuthServerName { get; init; }

    /// <summary>空值表示跟随全局。</summary>
    public string? ExtraJvmArguments { get; init; }
    public string? ExtraGameArguments { get; init; }
    public GcPolicy? Gc { get; init; }
    public string? AutoJoinServer { get; init; }
    public string? VersionInfo { get; init; }

    /// <summary>VersionArgumentJavaV2=3 时指定的 java.exe。</summary>
    public string? JavaPath { get; init; }
}

/// <summary>读写 instance.json，并合并 PCL 的信息得到 <see cref="InstanceEntry"/>。</summary>
public sealed class InstanceCatalog(SettingsStore settingsStore)
{
    public const string MetaFolder = ".fcp";

    public static string MetaPath(GameInstance instance) => Path.Combine(instance.VersionDirectory, MetaFolder, "instance.json");

    public async Task<InstanceMeta> LoadMetaAsync(GameInstance instance, CancellationToken cancellationToken = default) =>
        await JsonFile.ReadAsync<InstanceMeta>(MetaPath(instance), cancellationToken).ConfigureAwait(false) ?? new InstanceMeta();

    public Task SaveMetaAsync(GameInstance instance, InstanceMeta meta, CancellationToken cancellationToken = default) =>
        JsonFile.WriteAsync(MetaPath(instance), meta, cancellationToken);

    public async Task<IReadOnlyList<InstanceEntry>> BuildEntriesAsync(IEnumerable<GameInstance> instances, CancellationToken cancellationToken = default)
    {
        var global = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<InstanceEntry>();
        foreach (var instance in instances)
        {
            var meta = await LoadMetaAsync(instance, cancellationToken).ConfigureAwait(false);
            var pcl = await ReadPclAsync(instance, cancellationToken).ConfigureAwait(false);
            var isolated = ResolveIsolation(instance, meta.Settings.Isolated, pcl?.Isolated, global.Isolation);
            var favorite = meta.IsFavorite ?? pcl?.IsFavorite ?? false;
            var hidden = meta.IsHidden ?? pcl?.IsHidden ?? false;
            var lastPlayed = meta.LastPlayed ?? GuessLastPlayed(isolated ? instance.VersionDirectory : instance.RootDirectory);
            var group = Classify(instance, favorite, hidden);
            entries.Add(new InstanceEntry(instance, meta, group, favorite, isolated, meta.Description ?? pcl?.CustomInfo, lastPlayed, pcl));
        }
        return entries
            .OrderBy(e => e.Group)
            .ThenByDescending(e => e.Instance.ReleaseTime ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.Instance.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public static InstanceGroup Classify(GameInstance instance, bool favorite, bool hidden)
    {
        if (hidden)
            return InstanceGroup.Hidden;
        if (instance.Health != InstanceHealth.Ok)
            return InstanceGroup.Broken;
        if (favorite)
            return InstanceGroup.Favorite;
        if (instance.VersionType == GameVersionType.AprilFools)
            return InstanceGroup.AprilFools;
        if (instance.IsModded)
            return InstanceGroup.Modded;
        return instance.VersionType == GameVersionType.Release ? InstanceGroup.Regular : InstanceGroup.Rare;
    }

    /// <summary>
    /// 版本隔离（决定游戏目录，必须与 PCL 一致，否则玩家会觉得存档和 Mod “不见了”，docs/02 §5）：
    /// 我们的设置 → PCL 已保存的值 → 版本目录中已有 mods / saves → 全局默认策略。
    /// </summary>
    public static bool ResolveIsolation(GameInstance instance, bool? ours, bool? pcl, IsolationPolicy policy)
    {
        if (ours is { } o)
            return o;
        if (pcl is { } p)
            return p;
        if (HasContent(Path.Combine(instance.VersionDirectory, "mods")) || HasSubdirectories(Path.Combine(instance.VersionDirectory, "saves")))
            return true;
        var nonRelease = instance.VersionType is not GameVersionType.Release;
        return policy switch
        {
            IsolationPolicy.Off => false,
            IsolationPolicy.Modded => instance.IsModded,
            IsolationPolicy.NonRelease => nonRelease,
            IsolationPolicy.ModdedAndNonRelease => instance.IsModded || nonRelease,
            _ => true,
        };
    }

    /// <summary>读取 PCL 的版本设置（versions\X\PCL\Setup.ini），不存在时返回 null。</summary>
    public static async Task<PclInstanceInfo?> ReadPclAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(instance.VersionDirectory, "PCL", "Setup.ini");
        if (!File.Exists(path))
            return null;
        var ini = await PclIniFile.LoadAsync(path, cancellationToken).ConfigureAwait(false);

        bool? isolated = ini.GetBool("VersionArgumentIndieV2");
        if (isolated is null && ini.GetInt("VersionArgumentIndie") is { } legacy and > 0)
            isolated = legacy == 1;

        var ramType = ini.GetInt("VersionRamType") ?? 2;
        int? memory = ramType == 1 && ini.GetInt("VersionRamCustom") is { } scale ? PclRamScale.ToMegabytes(scale) : null;
        string? Text(string key) => ini.GetString(key) is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        // VersionAdvanceGC：0=跟随全局，其余 = 全局取值 + 1
        var gc = ini.GetInt("VersionAdvanceGC") is { } gcValue and > 0 ? PclGlobalSettingsMapper.MapGc(gcValue - 1) : (GcPolicy?)null;

        return new PclInstanceInfo
        {
            Isolated = isolated,
            IsFavorite = ini.GetBool("IsStar") ?? false,
            IsHidden = ini.GetInt("DisplayType") == 1,
            CustomInfo = Text("CustomInfo"),
            MemoryMb = memory,
            MemoryFollowsGlobal = ramType == 2,
            MemoryAuto = ramType == 0,
            LoginRequirement = ini.GetInt("VersionServerLogin") ?? 0,
            AuthServer = Text("VersionServerAuthServer"),
            AuthServerName = Text("VersionServerAuthName"),
            ExtraJvmArguments = Text("VersionAdvanceJvm"),
            ExtraGameArguments = Text("VersionAdvanceGame"),
            Gc = gc,
            AutoJoinServer = Text("VersionServerEnter"),
            VersionInfo = Text("VersionArgumentInfo"),
            JavaPath = ini.GetInt("VersionArgumentJavaV2") == 3 ? ParsePclJavaSelection(Text("VersionArgumentJavaSelect")) : null,
        };
    }

    /// <summary>PCL 保存的 Java 选择是 JSON（<c>{"Path":"...\bin\", ...}</c>），也可能是“使用全局设置”之类的文字。</summary>
    internal static string? ParsePclJavaSelection(string? value)
    {
        if (value is null || !value.TrimStart().StartsWith('{'))
            return null;
        try
        {
            var path = System.Text.Json.Nodes.JsonNode.Parse(value)?["Path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path))
                return null;
            return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path, "java.exe");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>没有我们自己的游玩记录时，用游戏日志的修改时间估计上次游玩时间。</summary>
    private static DateTimeOffset? GuessLastPlayed(string gameDirectory)
    {
        var log = Path.Combine(gameDirectory, "logs", "latest.log");
        return File.Exists(log) ? File.GetLastWriteTime(log) : null;
    }

    private static bool HasContent(string dir) => Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any();

    private static bool HasSubdirectories(string dir) => Directory.Exists(dir) && Directory.EnumerateDirectories(dir).Any();
}
