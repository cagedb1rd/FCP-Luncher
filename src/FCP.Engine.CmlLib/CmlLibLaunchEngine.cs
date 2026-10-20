using CmlLib.Core;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Rules;
using CmlLib.Core.Version;
using CmlLib.Core.VersionLoader;
using FCP.Core.Launch;

namespace FCP.Engine.CmlLib;

/// <summary>
/// 基于 CmlLib.Core 的启动引擎（见 docs/decisions.md D-003、D-015）：
/// CmlLib 负责版本元数据、继承链、规则判断、natives 解压与文件补全；
/// 启动参数由我们自己组装，直接得到参数列表。
/// </summary>
public sealed class CmlLibLaunchEngine : ILaunchEngine
{
    private readonly HttpClient _httpClient;

    public CmlLibLaunchEngine(HttpClient httpClient) => _httpClient = httpClient;

    public async Task VerifyAndRepairFilesAsync(GameInstanceLocation instance, IProgress<FileRepairProgress>? progress, CancellationToken cancellationToken)
    {
        var path = new FcpMinecraftPath(instance.RootDirectory, instance.RootDirectory);
        var launcher = CreateLauncher(path);
        var version = await launcher.GetVersionAsync(instance.VersionName, cancellationToken).ConfigureAwait(false);

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

        // natives（只操作我们自己的 natives-fcp 目录）
        launcher.NativeLibraryExtractor.Clean(path, version);
        var nativesDirectory = launcher.NativeLibraryExtractor.Extract(path, version, launcher.RulesContext);

        var assembler = new ArgumentAssembler(launcher.RulesEvaluator, launcher.RulesContext, path, version, request, nativesDirectory);
        return assembler.Build();
    }

    private MinecraftLauncher CreateLauncher(FcpMinecraftPath path)
    {
        var parameters = MinecraftLauncherParameters.CreateDefault(path, _httpClient);
        // 只读取本地版本，不在启动时联网获取 Mojang 版本清单
        parameters.VersionLoader = new LocalJsonVersionLoader(path);
        // 文件补全不包括 Java 运行时：Java 由我们自己的 Java 模块管理（D-012）
        var extractors = DefaultFileExtractors.CreateDefault(_httpClient, parameters.RulesEvaluator!, parameters.JavaPathResolver!);
        extractors.Java = null;
        parameters.FileExtractors = extractors.ToExtractorCollection();
        return new MinecraftLauncher(parameters);
    }
}
