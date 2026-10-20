using CmlLib.Core;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using FCP.Core;
using FCP.Core.Launch;

namespace FCP.Engine.CmlLib;

/// <summary>
/// 基于 CmlLib.Core 的启动引擎（见 docs/decisions.md D-003、D-015）：
/// CmlLib 负责版本元数据、继承链、规则判断、natives 解压与文件补全（依赖库、资源文件）；
/// 启动参数由我们自己组装，直接得到参数列表。
/// 游戏本体（版本 jar）只检查、从不下载（D-027）。
/// </summary>
public sealed class CmlLibLaunchEngine : ILaunchEngine
{
    private readonly HttpClient _httpClient;

    public CmlLibLaunchEngine(HttpClient httpClient) => _httpClient = httpClient;

    public async Task VerifyAndRepairFilesAsync(GameInstanceLocation instance, bool fullCheck, IProgress<FileRepairProgress>? progress, CancellationToken cancellationToken)
    {
        var path = new FcpMinecraftPath(instance.RootDirectory, instance.RootDirectory);
        var launcher = CreateLauncher(path, fullCheck);
        var version = await launcher.GetVersionAsync(instance.VersionName, cancellationToken).ConfigureAwait(false);
        EnsureGameJar(path, version);

        var stage = "";
        int completed = 0, total = 0;
        long completedBytes = 0, totalBytes = 0;
        var fileProgress = new Progress<InstallerProgressChangedEventArgs>(e =>
        {
            stage = e.Name ?? stage;
            completed = e.ProgressedTasks;
            total = e.TotalTasks;
            progress?.Report(new FileRepairProgress(stage, completed, total, completedBytes, totalBytes));
        });
        var byteProgress = new Progress<global::CmlLib.Core.ByteProgress>(e =>
        {
            completedBytes = e.ProgressedBytes;
            totalBytes = e.TotalBytes;
            progress?.Report(new FileRepairProgress(stage, completed, total, completedBytes, totalBytes));
        });
        await launcher.InstallAsync(version, fileProgress, byteProgress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LaunchPlan> BuildLaunchPlanAsync(LaunchRequest request, CancellationToken cancellationToken)
    {
        var path = new FcpMinecraftPath(request.Instance.RootDirectory, request.GameDirectory);
        var launcher = CreateLauncher(path);
        var version = await launcher.GetVersionAsync(request.Instance.VersionName, cancellationToken).ConfigureAwait(false);
        EnsureGameJar(path, version);

        // natives（只操作我们自己的 natives-fcp 目录）
        launcher.NativeLibraryExtractor.Clean(path, version);
        var nativesDirectory = launcher.NativeLibraryExtractor.Extract(path, version, launcher.RulesContext);

        var assembler = new ArgumentAssembler(launcher.RulesEvaluator, launcher.RulesContext, path, version, request, nativesDirectory);
        return assembler.Build();
    }

    /// <summary>游戏本体不存在时给出明确的说明（扫描阶段通常已经发现，这里是最后一道检查）。</summary>
    private static void EnsureGameJar(MinecraftPath path, IVersion version)
    {
        var jar = path.GetVersionJarPath(version.MainJarId);
        if (!File.Exists(jar))
            throw new UserFacingException("缺少游戏本体", $"找不到游戏本体文件：\n{jar}\nFCP 不提供 Minecraft 游戏本体的下载，请在原来的启动器（如 PCL）中补全或重新安装这个版本后再试。");
    }

    private MinecraftLauncher CreateLauncher(FcpMinecraftPath path, bool fullCheck = false)
    {
        var parameters = MinecraftLauncherParameters.CreateDefault(path, _httpClient);
        // 快速校验只比较大小；完整校验计算 SHA-1
        var installer = ParallelGameInstaller.CreateAsCoreCount(_httpClient);
        installer.CheckFileSize = true;
        installer.CheckFileChecksum = fullCheck;
        parameters.GameInstaller = installer;
        // 只读取本地版本（不在启动时联网获取 Mojang 版本清单），并先做归一化
        parameters.VersionLoader = new FcpVersionLoader(path.Versions);
        // 文件补全不包括 Java 运行时（由我们自己的 Java 模块管理，D-012），也不包括游戏本体 jar（不提供本体下载，D-027）
        var extractors = DefaultFileExtractors.CreateDefault(_httpClient, parameters.RulesEvaluator!, parameters.JavaPathResolver!);
        extractors.Java = null;
        extractors.Client = null;
        parameters.FileExtractors = extractors.ToExtractorCollection();
        return new MinecraftLauncher(parameters);
    }
}
