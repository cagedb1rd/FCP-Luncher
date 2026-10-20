using FCP.Core.Instances;

namespace FCP.Core.Java;

/// <summary>
/// 计算版本对 Java 的需求，并从已有的 Java 中选择最合适的一个（docs/04 §5.1）。
/// 原则：以版本 JSON 声明的 javaVersion（Mojang 官方推荐）为准；只有确知不兼容的组合才限制上限。
/// 规则集中在本文件，每条都附带理由，便于审阅和测试（D-009）。
/// </summary>
public static class JavaSelector
{
    public static JavaRequirement GetRequirement(GameInstance instance)
    {
        var reasons = new List<string>();
        int recommended;
        if (instance.RecommendedJavaMajor is { } declared)
        {
            recommended = declared;
            reasons.Add($"版本文件声明需要 Java {declared}");
        }
        else
        {
            recommended = GuessFromMinecraftVersion(instance.MinecraftVersion, instance.ReleaseTime);
            reasons.Add($"根据游戏版本推断需要 Java {recommended}");
        }

        var min = recommended;
        int? max;
        if (recommended <= 8)
        {
            // 1.16.5 及更早的版本（以及 LaunchWrapper、LiteLoader、老版本 Forge）只能可靠地运行在 Java 8 上
            max = 8;
            reasons.Add("1.16.5 及更早的版本只能使用 Java 8");
        }
        else if (instance.IsModded)
        {
            // 加载器（尤其是 Forge 系）对过新的 Java 经常不兼容，限制在推荐版本之后的几个大版本以内
            max = recommended + 4;
            reasons.Add($"安装了 Mod 加载器，使用 Java {recommended} ~ {max} 最稳妥");
        }
        else
        {
            max = null;
        }

        // Fabric Loader 0.16 及更早版本的 Mixin/ASM 不支持 Java 25 的 class 文件
        if (instance.Loaders.FirstOrDefault(l => l.Type == LoaderType.Fabric)?.Version is { } fabric &&
            CompareVersion(fabric, "0.17.0") < 0 && (max is null || max > 24))
        {
            max = 24;
            reasons.Add($"Fabric Loader {fabric} 不支持 Java 25 及更高版本");
        }

        return new JavaRequirement(recommended, min, max, ComponentFor(recommended), reasons);
    }

    /// <summary>
    /// 从候选中选择：先选主版本与推荐完全一致的（同一主版本中优先 JDK 来源可靠、版本号最新的），
    /// 否则选允许范围内最低的主版本。都没有时返回 null（由调用方决定是否下载）。
    /// </summary>
    public static JavaChoice Select(JavaRequirement requirement, IEnumerable<JavaRuntime> candidates)
    {
        var usable = candidates.Where(j => j.Is64Bit && requirement.Accepts(j.Major)).ToList();
        var chosen = usable
            .OrderBy(j => j.Major == requirement.RecommendedMajor ? 0 : 1)
            .ThenBy(j => j.Major)
            .ThenBy(j => SourceRank(j.Source))
            .ThenByDescending(j => j.Version)
            .FirstOrDefault();

        var why = string.Join("；", requirement.Reasons);
        var explanation = chosen is null
            ? $"{why}。电脑上没有找到合适的 Java（需要 {requirement.RangeText}）。"
            : $"{why}。已选择 {chosen.DisplayName}（{chosen.JavaExecutable}）。";
        return new JavaChoice(chosen, requirement, explanation);
    }

    /// <summary>没有 javaVersion 字段的老 JSON：按游戏版本推断。</summary>
    internal static int GuessFromMinecraftVersion(string? version, DateTimeOffset? releaseTime)
    {
        if (version is not null && Version.TryParse(version, out var v))
        {
            if (v.Major >= 26) return 25;           // 26.x（2026 年起的新版本号）
            if (v.Major == 1 && v.Minor >= 21) return 21;
            if (v.Major == 1 && v.Minor == 20 && v.Build >= 5) return 21;
            if (v.Major == 1 && v.Minor >= 18) return 17;
            if (v.Major == 1 && v.Minor == 17) return 16;
            return 8;
        }
        if (releaseTime is { } time)
        {
            if (time >= new DateTimeOffset(2024, 4, 23, 0, 0, 0, TimeSpan.Zero)) return 21;
            if (time >= new DateTimeOffset(2021, 11, 30, 0, 0, 0, TimeSpan.Zero)) return 17;
            if (time >= new DateTimeOffset(2021, 6, 8, 0, 0, 0, TimeSpan.Zero)) return 16;
        }
        return 8;
    }

    /// <summary>Mojang 运行时组件名（用于自动下载）。</summary>
    public static string? ComponentFor(int major) => major switch
    {
        <= 8 => "jre-legacy",
        16 => "java-runtime-alpha",
        17 => "java-runtime-gamma",
        21 => "java-runtime-delta",
        _ => null, // 其他版本由下载模块按清单中的版本号匹配
    };

    private static int SourceRank(JavaSource source) => source switch
    {
        JavaSource.User => 0,
        JavaSource.Downloaded => 1,
        JavaSource.MinecraftRuntime => 2,
        JavaSource.Registry or JavaSource.KnownFolder => 3,
        JavaSource.JavaHome or JavaSource.PathVariable => 4,
        _ => 5, // 其他启动器、PCL 记录（例如 IDE 自带的 Java）排在后面
    };

    /// <summary>比较 "0.16.10" 与 "0.17.0" 这样的版本号（非数字部分忽略）。</summary>
    internal static int CompareVersion(string a, string b)
    {
        int[] Parse(string s) => s.Split('.', '-', '+').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var (x, y) = (Parse(a), Parse(b));
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0)
                return c;
        }
        return 0;
    }
}
