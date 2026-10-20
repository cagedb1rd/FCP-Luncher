namespace FCP.Core.Launch;

/// <summary>一个版本（实例）在磁盘上的位置。</summary>
public sealed record GameInstanceLocation
{
    public GameInstanceLocation(string rootDirectory, string versionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionName);
        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        VersionName = versionName;
    }

    /// <summary>.minecraft 根目录（不以分隔符结尾）。</summary>
    public string RootDirectory { get; }

    /// <summary>版本文件夹名，可能包含中文、全角字符和空格。</summary>
    public string VersionName { get; }

    public string VersionDirectory => Path.Combine(RootDirectory, "versions", VersionName);

    /// <summary>从 <c>...\.minecraft\versions\&lt;名称&gt;</c> 形式的路径创建。</summary>
    public static GameInstanceLocation FromVersionDirectory(string versionDirectory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(versionDirectory));
        var versionsDir = Path.GetDirectoryName(full);
        if (versionsDir is null || !string.Equals(Path.GetFileName(versionsDir), "versions", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"不是版本文件夹（应位于 versions 目录下）：{versionDirectory}", nameof(versionDirectory));
        var root = Path.GetDirectoryName(versionsDir)!;
        return new GameInstanceLocation(root, Path.GetFileName(full));
    }
}

/// <summary>登录后得到的、启动游戏所需的身份信息。</summary>
public sealed record LaunchSession(
    string PlayerName,
    Guid Uuid,
    string AccessToken,
    string UserType,
    string? Xuid = null,
    string? ClientId = null)
{
    /// <summary>
    /// 离线会话。userType 使用 "msa"：与 PCL 的行为一致，迁移用户的游戏体验不变；
    /// 离线令牌直接使用 UUID 字符串。
    /// </summary>
    public static LaunchSession Offline(string playerName, Guid uuid) =>
        new(playerName, uuid, uuid.ToString("N"), "msa");
}

/// <summary>选定的 Java。</summary>
public sealed record JavaSelection(string JavaExecutable, int MajorVersion);

/// <summary>启动请求：调用方（启动管线）已经决定好的所有参数。</summary>
public sealed record LaunchRequest
{
    public required GameInstanceLocation Instance { get; init; }

    /// <summary>游戏目录（--gameDir）。开启版本隔离时为版本目录，否则为根目录。</summary>
    public required string GameDirectory { get; init; }

    public required JavaSelection Java { get; init; }
    public required LaunchSession Session { get; init; }

    /// <summary>最大内存（MB），0 表示不指定。</summary>
    public int MaxMemoryMb { get; init; }

    /// <summary>用户附加的 JVM 参数（已拆分）。</summary>
    public IReadOnlyList<string> ExtraJvmArguments { get; init; } = [];

    /// <summary>用户附加的游戏参数（已拆分）。</summary>
    public IReadOnlyList<string> ExtraGameArguments { get; init; } = [];

    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }
    public bool FullScreen { get; init; }

    /// <summary>自动进入的服务器（host 或 host:port）。</summary>
    public string? ServerAddress { get; init; }

    /// <summary>显示在游戏主界面左下角的版本类型文字（--versionType）。</summary>
    public string? VersionTypeText { get; init; }

    /// <summary>
    /// 是否使用版本 JSON 中 Mojang 提供的 log4j 配置（-Dlog4j.configurationFile）。
    /// 默认关闭：它会把控制台输出变成 XML 事件，并覆盖 Forge 等加载器自带的日志配置（导致不生成 debug.log），
    /// 而 debug.log 是崩溃分析和玩家求助最重要的材料。
    /// </summary>
    public bool UseMojangLogConfiguration { get; init; }

    public string LauncherName { get; init; } = "FCP";
    public string LauncherVersion { get; init; } = "0";
}

/// <summary>构建完成、可以直接启动的进程描述。</summary>
public sealed record LaunchPlan(
    string JavaExecutable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    string MainClass,
    string NativesDirectory)
{
    /// <summary>构建过程中发现的问题（缺少文件、未识别的占位符等），不一定阻止启动。</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>文件补全进度。</summary>
public sealed record FileRepairProgress(string Stage, int CompletedFiles, int TotalFiles, long CompletedBytes, long TotalBytes);

/// <summary>
/// 启动引擎：读取版本元数据、补全文件、构建启动参数。
/// Core 只依赖这个接口，具体实现在 FCP.Engine.CmlLib。
/// </summary>
public interface ILaunchEngine
{
    /// <summary>校验并补全 libraries / assets / 客户端 jar。</summary>
    Task VerifyAndRepairFilesAsync(GameInstanceLocation instance, IProgress<FileRepairProgress>? progress, CancellationToken cancellationToken);

    /// <summary>构建启动计划（不启动进程）。</summary>
    Task<LaunchPlan> BuildLaunchPlanAsync(LaunchRequest request, CancellationToken cancellationToken);
}
