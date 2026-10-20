using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace FCP.Core.Instances;

/// <summary>
/// 扫描 .minecraft\versions 下的所有版本，识别版本号、加载器和健康状态（docs/01 §4.2 描述的行为，独立实现）。
/// 只读，不修改任何文件。
/// </summary>
public sealed partial class InstanceScanner(ILogger<InstanceScanner> logger)
{
    // 已知的愚人节版本 id（公开信息）
    private static readonly HashSet<string> AprilFoolsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "2.0_blue", "2.0_red", "2.0_purple", "15w14a", "1.RV-Pre1", "3D Shareware v1.34", "20w14infinite", "20w14∞",
        "22w13oneblockatatime", "23w13a_or_b", "24w14potato", "25w14craftmine",
    };

    public async Task<IReadOnlyList<GameInstance>> ScanAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        var versionsDir = Path.Combine(rootDirectory, "versions");
        if (!Directory.Exists(versionsDir))
            return [];
        List<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(versionsDir).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(ex, "无法读取版本文件夹 {Dir}", versionsDir);
            return [];
        }

        var results = new GameInstance?[folders.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, folders.Count), new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 4 }, (i, _) =>
        {
            results[i] = ScanOne(rootDirectory, Path.GetFileName(folders[i]));
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
        return results.OfType<GameInstance>().ToList();
    }

    /// <summary>扫描单个版本；不像版本文件夹的目录返回 null。</summary>
    public GameInstance? ScanOne(string rootDirectory, string name)
    {
        var versionDir = Path.Combine(rootDirectory, "versions", name);
        try
        {
            // PCL 安装中的版本会放一个 .pclignore，安装完成前跳过
            if (File.Exists(Path.Combine(versionDir, ".pclignore")))
                return null;
            var jsonPath = VersionJson.Locate(versionDir);
            if (jsonPath is null)
            {
                // 没有 JSON 也没有 jar 的目录不是版本（例如空目录、缓存目录）
                var hasJar = Directory.EnumerateFiles(versionDir, "*.jar").Any();
                return hasJar ? Broken(rootDirectory, name, null, "找不到版本 JSON 文件") : null;
            }

            JsonObject json;
            try
            {
                json = VersionJson.Read(jsonPath);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                return Broken(rootDirectory, name, jsonPath, "版本 JSON 文件已损坏");
            }

            var isHmcl = VersionJson.IsHmclFormat(json);
            if (isHmcl)
                json = VersionJson.MergeHmclPatches(json);

            // 沿继承链收集各层 JSON（子 → 父）
            var chain = new List<JsonObject> { json };
            var chainNames = new List<string> { name };
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
            var inherits = json["inheritsFrom"]?.GetValue<string>();
            var parentName = inherits;
            while (!string.IsNullOrEmpty(parentName))
            {
                if (!visited.Add(parentName))
                    return Broken(rootDirectory, name, jsonPath, "版本的继承关系出现循环");
                var parentJson = VersionJson.Locate(Path.Combine(rootDirectory, "versions", parentName));
                if (parentJson is null)
                {
                    return new GameInstance
                    {
                        RootDirectory = rootDirectory, Name = name, JsonPath = jsonPath, InheritsFrom = inherits,
                        Health = InstanceHealth.MissingParent, Problem = $"缺少前置版本 {parentName}",
                        Loaders = DetectLoaders(chain), MinecraftVersion = parentName,
                    };
                }
                var parent = VersionJson.Read(parentJson);
                chain.Add(parent);
                chainNames.Add(parentName);
                parentName = parent["inheritsFrom"]?.GetValue<string>();
            }

            if (chain.All(j => string.IsNullOrEmpty(j["mainClass"]?.GetValue<string>())))
                return Broken(rootDirectory, name, jsonPath, "版本 JSON 中没有 mainClass");

            var root = chain[^1];
            var loaders = DetectLoaders(chain);
            if (FindGameJar(rootDirectory, chainNames, chain) is null)
            {
                return new GameInstance
                {
                    RootDirectory = rootDirectory, Name = name, JsonPath = jsonPath, InheritsFrom = inherits,
                    Health = InstanceHealth.MissingGameJar, Loaders = loaders,
                    MinecraftVersion = DetectMinecraftVersion(rootDirectory, name, chain, loaders),
                    Problem = $"缺少游戏本体文件（versions\\{chainNames[^1]}\\{chainNames[^1]}.jar）",
                };
            }
            var mcVersion = DetectMinecraftVersion(rootDirectory, name, chain, loaders);
            var releaseTime = chain.Select(j => TryDate(j["releaseTime"])).LastOrDefault(d => d is not null);
            var javaMajor = chain.Select(j => j["javaVersion"]?["majorVersion"]?.GetValue<int>()).FirstOrDefault(v => v is > 0);

            return new GameInstance
            {
                RootDirectory = rootDirectory,
                Name = name,
                JsonPath = jsonPath,
                MinecraftVersion = mcVersion,
                VersionType = DetectVersionType(mcVersion ?? root["id"]?.GetValue<string>(), root["type"]?.GetValue<string>()),
                ReleaseTime = releaseTime,
                Loaders = loaders,
                InheritsFrom = inherits,
                RecommendedJavaMajor = javaMajor,
                IsHmclFormat = isHmcl,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException)
        {
            logger.LogWarning(ex, "读取版本失败：{Dir}", versionDir);
            return Broken(rootDirectory, name, null, "读取版本时出错：" + ex.Message);
        }
    }

    /// <summary>
    /// 找到实际使用的游戏本体 jar：自己的 jar → JSON 中 jar 字段指定的版本 → 沿继承链向上的各个版本。
    /// 与启动时的规则一致（子版本没有自己的 jar 时使用父版本的 jar，见 VersionJson.Normalize）。
    /// </summary>
    internal static string? FindGameJar(string rootDirectory, IReadOnlyList<string> chainNames, IReadOnlyList<JsonObject> chain)
    {
        var candidates = new List<string>();
        for (var i = 0; i < chainNames.Count; i++)
        {
            candidates.Add(chainNames[i]);
            if (chain[i]["jar"]?.GetValue<string>() is { Length: > 0 } jar)
                candidates.Add(jar);
        }
        return candidates
            .Select(id => Path.Combine(rootDirectory, "versions", id, id + ".jar"))
            .FirstOrDefault(File.Exists);
    }

    private static GameInstance Broken(string root, string name, string? jsonPath, string problem) => new()
    {
        RootDirectory = root, Name = name, JsonPath = jsonPath, Health = InstanceHealth.Broken, Problem = problem,
    };

    private static DateTimeOffset? TryDate(JsonNode? node) =>
        node is not null && DateTimeOffset.TryParse(node.GetValue<string>(), out var value) ? value : null;

    // ---------------- 加载器识别 ----------------

    internal static IReadOnlyList<LoaderInfo> DetectLoaders(IReadOnlyList<JsonObject> chain)
    {
        var libraries = chain
            .SelectMany(j => j["libraries"] as JsonArray ?? [])
            .Select(l => l?["name"]?.GetValue<string>())
            .OfType<string>()
            .ToList();
        var gameArgs = chain
            .SelectMany(j => (j["arguments"]?["game"] as JsonArray ?? []).OfType<JsonValue>().Select(v => v.ToString()))
            .Concat(chain.Select(j => j["minecraftArguments"]?.GetValue<string>()).OfType<string>().SelectMany(s => s.Split(' ')))
            .ToList();
        string? ArgValue(string key)
        {
            var index = gameArgs.IndexOf(key);
            return index >= 0 && index + 1 < gameArgs.Count ? gameArgs[index + 1] : null;
        }

        var result = new List<LoaderInfo>();
        void Add(LoaderType type, string? version)
        {
            if (result.Any(l => l.Type == type))
                return;
            result.Add(new LoaderInfo(type, string.IsNullOrWhiteSpace(version) ? null : version));
        }

        foreach (var lib in libraries)
        {
            var parts = lib.Split(':');
            if (parts.Length < 3)
                continue;
            var (group, artifact, version) = (parts[0], parts[1], parts[2]);
            switch (group)
            {
                case "net.neoforged" when artifact is "neoforge":
                    Add(LoaderType.NeoForge, ArgValue("--fml.neoForgeVersion") ?? version);
                    break;
                case "net.neoforged" when artifact is "forge":
                    // 1.20.1 的 NeoForge 沿用了 forge 这个名字
                    Add(LoaderType.NeoForge, ArgValue("--fml.forgeVersion") ?? StripMinecraftPrefix(version));
                    break;
                case "net.neoforged.fancymodloader":
                    Add(LoaderType.NeoForge, ArgValue("--fml.neoForgeVersion"));
                    break;
                case "net.minecraftforge" when artifact is "forge" or "fmlloader" or "minecraftforge":
                    Add(LoaderType.Forge, ArgValue("--fml.forgeVersion") ?? StripMinecraftPrefix(version));
                    break;
                case "net.fabricmc" when artifact is "fabric-loader":
                    Add(LoaderType.Fabric, version);
                    break;
                case "org.quiltmc" when artifact is "quilt-loader":
                    Add(LoaderType.Quilt, version);
                    break;
                case "optifine" when artifact.Equals("OptiFine", StringComparison.OrdinalIgnoreCase):
                    Add(LoaderType.OptiFine, version.Contains('_') ? version[(version.IndexOf('_') + 1)..].Replace('_', ' ') : version);
                    break;
                case "com.mumfrey" when artifact is "liteloader":
                    Add(LoaderType.LiteLoader, null);
                    break;
            }
        }
        // NeoForge 存在时，fmlloader 等 Forge 兼容库不应再识别为 Forge
        if (result.Any(l => l.Type == LoaderType.NeoForge))
            result.RemoveAll(l => l.Type == LoaderType.Forge);
        return result;
    }

    /// <summary>"1.20.1-47.4.10" → "47.4.10"；"1.7.10-10.13.4.1614-1.7.10" → "10.13.4.1614"。</summary>
    internal static string StripMinecraftPrefix(string version)
    {
        var parts = version.Split('-');
        if (parts.Length >= 2 && MinecraftVersionRegex().IsMatch(parts[0]))
            return parts[1];
        return version;
    }

    // ---------------- 版本号识别 ----------------

    internal static string? DetectMinecraftVersion(string rootDirectory, string name, IReadOnlyList<JsonObject> chain, IReadOnlyList<LoaderInfo> loaders)
    {
        // 1. 继承链的根就是原版
        if (chain.Count > 1 && chain[^1]["id"]?.GetValue<string>() is { Length: > 0 } rootId)
            return rootId;

        // 2. Forge / NeoForge 的启动参数中带有 MC 版本
        var gameArgs = chain.SelectMany(j => (j["arguments"]?["game"] as JsonArray ?? []).OfType<JsonValue>().Select(v => v.ToString())).ToList();
        var mcIndex = gameArgs.IndexOf("--fml.mcVersion");
        if (mcIndex >= 0 && mcIndex + 1 < gameArgs.Count)
            return gameArgs[mcIndex + 1];

        // 3. 部分安装器写入的 clientVersion 字段
        if (chain[0]["clientVersion"]?.GetValue<string>() is { Length: > 0 } clientVersion)
            return clientVersion;

        // 4. 原版 jar 内的 version.json
        var jarId = chain[0]["jar"]?.GetValue<string>() ?? name;
        if (VersionJson.ReadVersionFromJar(Path.Combine(rootDirectory, "versions", jarId, jarId + ".jar")) is { } fromJar)
            return fromJar;

        // 5. Forge / OptiFine 库的版本号前缀
        foreach (var lib in chain.SelectMany(j => j["libraries"] as JsonArray ?? []).Select(l => l?["name"]?.GetValue<string>()).OfType<string>())
        {
            var parts = lib.Split(':');
            if (parts.Length >= 3 && (parts[0] is "net.minecraftforge" or "net.neoforged" && parts[1] is "forge" or "fmlloader"))
            {
                var prefix = parts[2].Split('-')[0];
                if (MinecraftVersionRegex().IsMatch(prefix))
                    return prefix;
            }
            if (parts.Length >= 3 && parts[0] == "optifine" && parts[2].Contains('_'))
                return parts[2][..parts[2].IndexOf('_')];
        }

        // 6. JSON 的 id 本身就像版本号
        var id = chain[0]["id"]?.GetValue<string>();
        return id is not null && LooksLikeVersion(id) ? id : null;
    }

    internal static GameVersionType DetectVersionType(string? version, string? jsonType)
    {
        if (version is not null && AprilFoolsIds.Contains(version))
            return GameVersionType.AprilFools;
        switch (jsonType)
        {
            case "release": return GameVersionType.Release;
            case "snapshot": return GameVersionType.Snapshot;
            case "old_alpha" or "old_beta": return GameVersionType.Old;
        }
        if (version is null)
            return GameVersionType.Unknown;
        if (SnapshotRegex().IsMatch(version) || version.Contains("-pre", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("-rc", StringComparison.OrdinalIgnoreCase) || version.Contains("-snapshot", StringComparison.OrdinalIgnoreCase))
            return GameVersionType.Snapshot;
        if (OldRegex().IsMatch(version))
            return GameVersionType.Old;
        return MinecraftVersionRegex().IsMatch(version) ? GameVersionType.Release : GameVersionType.Unknown;
    }

    private static bool LooksLikeVersion(string id) =>
        MinecraftVersionRegex().IsMatch(id) || SnapshotRegex().IsMatch(id) || OldRegex().IsMatch(id);

    // 1.20.1、1.8、26.1、26.1.2（MC 从 2026 年起使用 26.x 版本号）
    [GeneratedRegex(@"^\d+\.\d+(\.\d+)?$")]
    private static partial Regex MinecraftVersionRegex();

    // 24w14a、26.4-snapshot-5
    [GeneratedRegex(@"^\d{2}w\d{2}[a-z~]+$|-snapshot-\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex SnapshotRegex();

    [GeneratedRegex(@"^(a|b|c|rd-|inf-)\d", RegexOptions.IgnoreCase)]
    private static partial Regex OldRegex();
}
