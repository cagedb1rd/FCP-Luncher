using System.Diagnostics;
using FCP.Core.Accounts;
using FCP.Core.Instances;
using FCP.Core.Java;
using FCP.Core.Platform;
using FCP.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FCP.Core.Launch;

/// <summary>启动进度：步骤名 + 0~100 的进度。</summary>
public sealed record LaunchProgress(string Stage, double Percent, string? Detail = null);

public sealed record LaunchOptions(bool VerifyFiles = true);

/// <summary>准备好的启动：完整的启动计划 + 各项决定的理由（界面和 CLI 显示给用户）。</summary>
public sealed record PreparedLaunch(
    InstanceEntry Entry,
    AccountRecord Account,
    JavaRuntime Java,
    string JavaExplanation,
    int MemoryMb,
    string MemoryExplanation,
    LaunchPlan Plan);

/// <summary>
/// 启动管线（docs/04 §5.1）：预检查 → 选择 Java → 账号 → 补全文件 → 内存 → 构建参数 → 启动进程 → 监控。
/// </summary>
public sealed class LaunchService(
    ILaunchEngine engine,
    JavaManager javaManager,
    JavaRuntimeDownloader javaDownloader,
    SettingsStore settingsStore,
    InstanceCatalog catalog,
    IWindowLocator windows,
    ISystemInfo system,
    AppPaths paths,
    ILogger<LaunchService> logger)
{
    /// <summary>执行到“构建参数”为止（不启动进程），用于启动前的全部准备，以及 dry-run / 导出启动脚本。</summary>
    public async Task<PreparedLaunch> PrepareAsync(InstanceEntry entry, AccountRecord account, LaunchOptions options, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        var instance = entry.Instance;
        var global = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var overrides = entry.Meta.Settings;

        // 1. 预检查
        progress?.Report(new LaunchProgress("检查版本", 2));
        Precheck(entry);

        // 2. Java
        progress?.Report(new LaunchProgress("选择 Java", 6));
        var (java, javaExplanation) = await ResolveJavaAsync(instance, overrides, entry.Pcl, progress, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("{Explanation}", javaExplanation);

        // 3. 账号
        progress?.Report(new LaunchProgress("登录", 10, account.PlayerName));
        var session = AccountService.CreateSession(account);

        // 4. 补全文件
        if (options.VerifyFiles)
        {
            progress?.Report(new LaunchProgress("补全文件", 12));
            await engine.VerifyAndRepairFilesAsync(
                new GameInstanceLocation(instance.RootDirectory, instance.Name),
                fullCheck: false,
                new Progress<FileRepairProgress>(p => progress?.Report(new LaunchProgress("补全文件",
                    12 + (p.TotalFiles > 0 ? 70.0 * p.CompletedFiles / p.TotalFiles : 0),
                    p.TotalBytes > 0 ? $"{p.CompletedFiles}/{p.TotalFiles}" : null))),
                cancellationToken).ConfigureAwait(false);
        }

        // 5. 内存：版本设置 → PCL 中的自定义值（迁移用户）→ 全局设置
        var (memoryMb, memoryExplanation) = ResolveMemory(entry, overrides, global);

        // 6. 构建参数
        progress?.Report(new LaunchProgress("构建启动参数", 85));
        var window = overrides.Window ?? global.Window;
        // 每一项：FCP 的版本设置 → PCL 的版本设置 → 全局设置
        var pcl = entry.Pcl;
        var gc = overrides.Gc ?? pcl?.Gc ?? global.Gc;
        var request = new LaunchRequest
        {
            Instance = new GameInstanceLocation(instance.RootDirectory, instance.Name),
            GameDirectory = entry.GameDirectory,
            Java = new Launch.JavaSelection(java.JavaExecutable, java.Major),
            Session = session,
            MaxMemoryMb = memoryMb,
            ExtraJvmArguments = SplitArguments(overrides.ExtraJvmArguments ?? pcl?.ExtraJvmArguments ?? global.ExtraJvmArguments).Concat(GcArguments(gc, java.Major)).ToList(),
            ExtraGameArguments = SplitArguments(overrides.ExtraGameArguments ?? pcl?.ExtraGameArguments ?? global.ExtraGameArguments),
            WindowWidth = window.FullScreen ? null : window.Width,
            WindowHeight = window.FullScreen ? null : window.Height,
            FullScreen = window.FullScreen,
            ServerAddress = overrides.AutoJoinServer ?? pcl?.AutoJoinServer,
            AddDefaultGcArguments = gc != GcPolicy.None,
            VersionTypeText = pcl?.VersionInfo ?? global.VersionTypeText,
            LauncherVersion = typeof(LaunchService).Assembly.GetName().Version?.ToString(3) ?? "0",
        };
        var plan = await engine.BuildLaunchPlanAsync(request, cancellationToken).ConfigureAwait(false);
        foreach (var warning in plan.Warnings)
            logger.LogWarning("启动参数警告：{Warning}", warning);
        return new PreparedLaunch(entry, account, java, javaExplanation, memoryMb, memoryExplanation, plan);
    }

    /// <summary>启动进程并返回游戏会话；游玩记录（次数、时长）写入版本元数据。</summary>
    public async Task<GameSession> StartAsync(PreparedLaunch prepared, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        var entry = prepared.Entry;
        var global = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (global.AutoChineseLanguage)
            EnsureChineseLanguage(entry.GameDirectory);

        progress?.Report(new LaunchProgress("启动游戏", 92));
        var startInfo = LaunchProcessFactory.Create(prepared.Plan, prepared.Java.Major, Path.Combine(paths.CacheDirectory, "args"));
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new UserFacingException("游戏进程没有启动", "Java 进程启动失败，请检查 Java 是否完整。");
        try
        {
            process.PriorityClass = global.Priority switch
            {
                GamePriority.High => ProcessPriorityClass.AboveNormal,
                GamePriority.Low => ProcessPriorityClass.BelowNormal,
                _ => ProcessPriorityClass.Normal,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        var logFile = Path.Combine(paths.LogsDirectory, "game", $"{SafeFileName(entry.Instance.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        // 游玩记录：启动时记录次数和时间，退出时累计时长（PCL 没有的功能）
        var meta = entry.Meta;
        meta.LastPlayed = DateTimeOffset.Now;
        meta.LaunchCount++;
        var session = new GameSession(entry, process, windows, logFile, async exit =>
        {
            logger.LogInformation("游戏已退出：{Instance}，退出码 {Code}，本次 {Duration}", entry.Instance.Name, exit.ExitCode, exit.Duration);
            try
            {
                meta.TotalPlaySeconds += (long)exit.Duration.TotalSeconds;
                await catalog.SaveMetaAsync(entry.Instance, meta, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "保存游玩记录失败");
            }
        });
        session.WindowShown += (_, _) => logger.LogInformation("游戏窗口已出现：{Instance}，用时 {Elapsed:F1} 秒", entry.Instance.Name, (DateTimeOffset.Now - session.StartedAt).TotalSeconds);
        logger.LogInformation("游戏已启动：{Instance}，PID {Pid}，Java {Java}，内存 {Memory} MB", entry.Instance.Name, process.Id, prepared.Java.JavaExecutable, prepared.MemoryMb);
        try
        {
            await catalog.SaveMetaAsync(entry.Instance, meta, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "保存游玩记录失败");
        }
        progress?.Report(new LaunchProgress("等待游戏窗口", 96));
        return session;
    }

    /// <summary>预览启动时会分配的内存及原因（与启动时的规则相同，版本设置页显示用）。</summary>
    public async Task<(int Megabytes, string Explanation)> PreviewMemoryAsync(InstanceEntry entry, CancellationToken cancellationToken = default)
    {
        var global = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return ResolveMemory(entry, entry.Meta.Settings, global);
    }

    /// <summary>预览启动时会使用的 Java（只看已发现的 Java，不扫描、不下载）。</summary>
    public string PreviewJava(InstanceEntry entry)
    {
        var overrides = entry.Meta.Settings;
        if (overrides.JavaMode == JavaMode.Specific && overrides.JavaPath is { } specific)
            return $"版本设置指定：{specific}";
        if (overrides.JavaMode is null && entry.Pcl?.JavaPath is { } pclJava
            && javaManager.Current.FirstOrDefault(j => string.Equals(j.JavaExecutable, pclJava, StringComparison.OrdinalIgnoreCase)) is { } known)
            return $"沿用 PCL 中为这个版本指定的 {known.DisplayName}。";
        var requirement = JavaSelector.GetRequirement(entry.Instance);
        var choice = JavaSelector.Select(requirement, javaManager.Current);
        return choice.Java is null
            ? $"电脑上还没有合适的 Java（需要 {requirement.RangeText}），启动时会自动下载 Java {requirement.RecommendedMajor}。"
            : choice.Explanation;
    }

    private static void Precheck(InstanceEntry entry)
    {
        var instance = entry.Instance;
        if (instance.Health == InstanceHealth.MissingParent)
            throw new UserFacingException("缺少前置版本", $"{instance.Problem}。FCP 不提供游戏本体下载，请在原来的启动器（如 PCL）中补全这个版本后再试。");
        if (instance.Health == InstanceHealth.MissingGameJar)
            throw new UserFacingException("缺少游戏本体", $"{instance.Problem}。FCP 不提供 Minecraft 游戏本体的下载，请在原来的启动器（如 PCL）中补全或重新安装这个版本后再试。");
        if (instance.Health != InstanceHealth.Ok)
            throw new UserFacingException("这个版本有问题", instance.Problem ?? "无法读取该版本。");
        foreach (var path in new[] { instance.RootDirectory, entry.GameDirectory })
        {
            if (path.Contains('!') || path.Contains(';'))
                throw new UserFacingException("文件夹名称不可用", $"游戏所在的路径中包含 “!” 或 “;”，Java 无法正常加载游戏文件：\n{path}\n请把相关文件夹改名后再试。");
        }
    }

    private async Task<(JavaRuntime Java, string Explanation)> ResolveJavaAsync(GameInstance instance, InstanceSettings overrides, PclInstanceInfo? pcl, IProgress<LaunchProgress>? progress, CancellationToken cancellationToken)
    {
        // PCL 中为这个版本指定了 Java：还能用就沿用；已经不存在时改为自动选择，并在说明中写明
        string? pclNote = null;
        if (overrides.JavaMode is null && pcl?.JavaPath is { } pclJava)
        {
            var known = javaManager.Current.FirstOrDefault(j => string.Equals(j.JavaExecutable, pclJava, StringComparison.OrdinalIgnoreCase));
            if (known is not null)
                return (known, $"沿用 PCL 中为这个版本指定的 {known.DisplayName}。");
            pclNote = $"PCL 中为这个版本指定的 Java 已经找不到了（{pclJava}），改为自动选择。";
        }

        if (overrides.JavaMode == JavaMode.Specific && overrides.JavaPath is { } specific)
        {
            var known = javaManager.Current.FirstOrDefault(j => string.Equals(j.JavaExecutable, specific, StringComparison.OrdinalIgnoreCase));
            if (known is not null)
                return (known, $"版本设置指定使用 {known.DisplayName}。");
            throw new UserFacingException("指定的 Java 不可用", $"版本设置中指定的 Java 已经找不到了：\n{specific}\n请在版本设置中重新选择 Java，或改为“自动选择”。");
        }

        var requirement = JavaSelector.GetRequirement(instance);
        var javas = javaManager.Current.Count > 0 ? javaManager.Current : await javaManager.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var choice = JavaSelector.Select(requirement, javas);
        if (choice.Java is null)
        {
            // 可能刚安装了新的 Java：重新扫描一次
            choice = JavaSelector.Select(requirement, await javaManager.RefreshAsync(cancellationToken).ConfigureAwait(false));
        }
        if (choice.Java is not null)
            return (choice.Java, pclNote is null ? choice.Explanation : $"{pclNote}{choice.Explanation}");

        // 电脑上没有合适的 Java：自动下载推荐版本（Mojang 官方运行时）
        var major = requirement.RecommendedMajor;
        logger.LogInformation("没有合适的 Java，自动下载 Java {Major}：{Explanation}", major, choice.Explanation);
        progress?.Report(new LaunchProgress("下载 Java", 6, $"Java {major}"));
        await javaDownloader.InstallAsync(major, requirement.MojangComponent,
            new Progress<JavaDownloadProgress>(p => progress?.Report(new LaunchProgress("下载 Java",
                6 + (p.TotalBytes > 0 ? 6.0 * p.DownloadedBytes / p.TotalBytes : 0),
                $"Java {major} · {p.DownloadedBytes / 1048576.0:0} / {p.TotalBytes / 1048576.0:0} MB"))),
            cancellationToken).ConfigureAwait(false);
        choice = JavaSelector.Select(requirement, await javaManager.RefreshAsync(cancellationToken).ConfigureAwait(false));
        if (choice.Java is null)
            throw new UserFacingException("没有合适的 Java", $"{choice.Explanation}\n自动下载的 Java 无法使用，请手动安装 {requirement.RangeText} 后在设置中添加。");
        return (choice.Java, $"{choice.Explanation}（电脑上原本没有合适的 Java，已自动下载）");
    }

    private (int Megabytes, string Explanation) ResolveMemory(InstanceEntry entry, InstanceSettings overrides, GlobalSettings global)
    {
        if (overrides.Memory is { Mode: MemoryMode.Custom } custom)
            return (custom.CustomMb, $"版本设置：{custom.CustomMb / 1024.0:0.#} GB");
        if (overrides.Memory is null && entry.Pcl is { MemoryFollowsGlobal: false, MemoryMb: { } pclMb })
            return (pclMb, $"沿用 PCL 中的版本设置：{pclMb / 1024.0:0.#} GB");
        // PCL 中这个版本设为“自动”：即使全局是自定义，也按自动分配（与 PCL 一致）
        var setting = overrides.Memory ?? (entry.Pcl is { MemoryAuto: true } ? global.Memory with { Mode = MemoryMode.Auto } : global.Memory);
        if (setting.Mode == MemoryMode.Custom)
            return (setting.CustomMb, $"全局设置：{setting.CustomMb / 1024.0:0.#} GB");
        var (auto, reason) = MemoryCalculator.Auto(MemoryCalculator.CountMods(entry.GameDirectory), system.AvailablePhysicalMemoryMb, system.TotalPhysicalMemoryMb);
        return (auto, $"自动分配 {auto / 1024.0:0.#} GB（{reason}）");
    }

    /// <summary>分代 ZGC 需要 Java 21+（Java 23 起默认就是分代模式）；其他情况交给引擎的默认 G1 或不添加。</summary>
    private static IEnumerable<string> GcArguments(GcPolicy policy, int javaMajor) => policy switch
    {
        GcPolicy.GenerationalZgc when javaMajor >= 23 => ["-XX:+UseZGC"],
        GcPolicy.GenerationalZgc when javaMajor >= 21 => ["-XX:+UseZGC", "-XX:+ZGenerational"],
        _ => [],
    };

    private static List<string> SplitArguments(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : WindowsCommandLine.Split(text.ReplaceLineEndings(" "));

    /// <summary>新版本第一次启动时（还没有 options.txt）把游戏语言设为简体中文。已有 options.txt 时不做任何修改。</summary>
    private static void EnsureChineseLanguage(string gameDirectory)
    {
        var options = Path.Combine(gameDirectory, "options.txt");
        if (File.Exists(options))
            return;
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(options, "lang:zh_cn\n");
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
