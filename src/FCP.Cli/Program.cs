using System.CommandLine;
using System.Diagnostics;
using System.Text;
using FCP.Core;
using FCP.Core.Accounts;
using FCP.Core.Folders;
using FCP.Core.Instances;
using FCP.Core.Java;
using FCP.Core.Launch;
using FCP.Hosting;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = Encoding.UTF8;

var root = new RootCommand("FCP 命令行工具：在没有界面的情况下验证核心功能");
root.Subcommands.Add(UuidCommand());
root.Subcommands.Add(ScanCommand());
root.Subcommands.Add(JavaCommand());
root.Subcommands.Add(DownloadJavaCommand());
root.Subcommands.Add(LaunchCommand());
return await root.Parse(args).InvokeAsync();

static Task<ServiceProvider> Services(CancellationToken cancellationToken) => FcpServices.CreateAsync(AppContext.BaseDirectory, cancellationToken);

static Command UuidCommand()
{
    var name = new Argument<string>("name") { Description = "离线玩家名" };
    var command = new Command("uuid", "计算离线 UUID（标准算法与 PCL 兼容算法）") { name };
    command.SetAction(result =>
    {
        var playerName = result.GetValue(name)!;
        Console.WriteLine($"标准算法     {OfflineUuid.Standard(playerName):N}");
        Console.WriteLine($"PCL 兼容     {OfflineUuid.PclLegacy(playerName):N}");
        Console.WriteLine($"PCL + Steve  {OfflineUuid.PclLegacy(playerName, SkinModel.Steve):N}");
        Console.WriteLine($"PCL + Alex   {OfflineUuid.PclLegacy(playerName, SkinModel.Alex):N}");
        return 0;
    });
    return command;
}

static Command ScanCommand()
{
    var command = new Command("scan", "发现游戏文件夹并列出所有版本（只读）");
    command.SetAction(async (result, cancellationToken) =>
    {
        await using var services = await Services(cancellationToken);
        var discovery = services.GetRequiredService<GameFolderDiscovery>();
        var folders = await discovery.DiscoverAsync(AppContext.BaseDirectory, await services.GetRequiredService<FolderSettingsStore>().LoadAsync(cancellationToken), cancellationToken);
        Console.WriteLine($"PCL 所在目录：{string.Join("；", folders.PclExecutableDirectories)}");
        Console.WriteLine($"PCL 上次选择的文件夹：{folders.PclSelectedFolder ?? "（无）"}");
        var scanner = services.GetRequiredService<InstanceScanner>();
        var catalog = services.GetRequiredService<InstanceCatalog>();
        foreach (var folder in folders.Folders)
        {
            Console.WriteLine();
            Console.WriteLine($"■ {folder.DisplayName}（{folder.Source}）{folder.Path}");
            var stopwatch = Stopwatch.StartNew();
            var entries = await catalog.BuildEntriesAsync(await scanner.ScanAsync(folder.Path, cancellationToken), cancellationToken);
            foreach (var e in entries)
            {
                var i = e.Instance;
                Console.WriteLine($"  [{e.Group}] {i.Name}");
                Console.WriteLine($"      {i.Description} | 类型 {i.VersionType} | 推荐 Java {i.RecommendedJavaMajor?.ToString() ?? "-"} | 隔离 {e.IsIsolated} | 收藏 {e.IsFavorite} | 上次游玩 {e.LastPlayed:yyyy-MM-dd HH:mm} | 游玩 {e.Meta.TotalPlaySeconds / 60} 分钟");
                if (e.Pcl is { } pcl)
                    Console.WriteLine($"      PCL：内存 {(pcl.MemoryFollowsGlobal ? "跟随全局" : pcl.MemoryAuto ? "自动" : pcl.MemoryMb + " MB")}，登录要求 {pcl.LoginRequirement}，认证服务器 {pcl.AuthServerName ?? "-"} {pcl.AuthServer ?? ""}，JVM 参数 {pcl.ExtraJvmArguments ?? "跟随全局"}，Java {pcl.JavaPath ?? "自动"}");
            }
            Console.WriteLine($"  （{entries.Count} 个版本，用时 {stopwatch.ElapsedMilliseconds} ms）");
        }
        return 0;
    });
    return command;
}

static Command JavaCommand()
{
    var command = new Command("java", "列出电脑上的 Java，并为每个版本选择 Java（只读）");
    command.SetAction(async (result, cancellationToken) =>
    {
        await using var services = await Services(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var javas = await services.GetRequiredService<JavaManager>().RefreshAsync(cancellationToken);
        Console.WriteLine($"发现 {javas.Count} 个 Java（{stopwatch.ElapsedMilliseconds} ms）：");
        foreach (var j in javas)
            Console.WriteLine($"  {j.DisplayName,-36} {(j.Is64Bit ? "64 位" : "32 位")}  [{j.Source}]  {j.JavaExecutable}");

        var folders = await services.GetRequiredService<GameFolderDiscovery>().DiscoverAsync(AppContext.BaseDirectory, new FolderSettings(), cancellationToken);
        foreach (var folder in folders.Folders)
        {
            foreach (var instance in await services.GetRequiredService<InstanceScanner>().ScanAsync(folder.Path, cancellationToken))
            {
                if (instance.Health != InstanceHealth.Ok)
                    continue;
                var choice = JavaSelector.Select(JavaSelector.GetRequirement(instance), javas);
                Console.WriteLine();
                Console.WriteLine($"■ {instance.Name}（{instance.Description}）");
                Console.WriteLine($"  需要：{choice.Requirement.RangeText}；{choice.Explanation}");
            }
        }
        return 0;
    });
    return command;
}

static Command DownloadJavaCommand()
{
    var major = new Argument<int>("major") { Description = "Java 主版本，例如 8、17、21" };
    var command = new Command("download-java", "下载 Mojang 官方 Java 运行时到数据目录（测试自动下载与镜像回退）") { major };
    command.SetAction(async (result, cancellationToken) =>
    {
        await using var services = await Services(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var lastPercent = -1;
        var path = await services.GetRequiredService<JavaRuntimeDownloader>().InstallAsync(result.GetValue(major), JavaSelector.ComponentFor(result.GetValue(major)),
            new Progress<JavaDownloadProgress>(p =>
            {
                var percent = p.TotalBytes > 0 ? (int)(100 * p.DownloadedBytes / p.TotalBytes) : 0;
                if (percent / 10 != lastPercent / 10)
                    Console.WriteLine($"  {percent,3}%  {p.DownloadedFiles}/{p.TotalFiles} 个文件  {p.DownloadedBytes / 1048576.0:0.0}/{p.TotalBytes / 1048576.0:0.0} MB");
                lastPercent = percent;
            }), cancellationToken);
        Console.WriteLine($"完成（{stopwatch.Elapsed.TotalSeconds:F1} 秒）：{path}");
        var info = JavaReleaseFile.TryRead(path);
        Console.WriteLine($"release 文件：{info?.Version} {info?.Vendor} {info?.Architecture}");
        return 0;
    });
    return command;
}

static Command LaunchCommand()
{
    var instance = new Option<DirectoryInfo>("--instance") { Description = "版本文件夹（...\\.minecraft\\versions\\<名称>）", Required = true };
    var offline = new Option<string>("--offline") { Description = "离线玩家名", DefaultValueFactory = _ => "Player" };
    var scheme = new Option<UuidScheme>("--uuid-scheme") { Description = "离线 UUID 算法", DefaultValueFactory = _ => UuidScheme.Standard };
    var dryRun = new Option<bool>("--dry-run") { Description = "只输出启动参数，不启动游戏" };
    var noVerify = new Option<bool>("--no-verify") { Description = "跳过文件校验" };
    var argsOut = new Option<FileInfo?>("--args-out") { Description = "把参数逐行写入文件（用于对比测试）" };
    var timeout = new Option<int>("--timeout") { Description = "实际启动时最多等待的秒数（0 = 一直等到游戏退出）", DefaultValueFactory = _ => 0 };
    var exitOnMenu = new Option<bool>("--exit-on-menu") { Description = "检测到进入主菜单 10 秒后自动结束游戏（用于自动化验证）" };

    var command = new Command("launch", "通过完整的启动管线启动游戏") { instance, offline, scheme, dryRun, noVerify, argsOut, timeout, exitOnMenu };
    command.SetAction(async (result, cancellationToken) =>
    {
        await using var services = await Services(cancellationToken);
        var location = GameInstanceLocation.FromVersionDirectory(result.GetValue(instance)!.FullName);
        var scanned = services.GetRequiredService<InstanceScanner>().ScanOne(location.RootDirectory, location.VersionName)
            ?? throw new InvalidOperationException("不是有效的版本文件夹");
        var entry = (await services.GetRequiredService<InstanceCatalog>().BuildEntriesAsync([scanned], cancellationToken))[0];

        var name = result.GetValue(offline)!;
        var uuidScheme = result.GetValue(scheme);
        var account = new AccountRecord
        {
            Id = "cli", Kind = AccountKind.Offline, PlayerName = name, Scheme = uuidScheme,
            Uuid = (uuidScheme == UuidScheme.PclLegacy ? OfflineUuid.PclLegacy(name) : OfflineUuid.Standard(name)).ToString("N"),
        };

        var launcher = services.GetRequiredService<LaunchService>();
        var lastStage = "";
        var progress = new Progress<LaunchProgress>(p =>
        {
            if (p.Stage == lastStage)
                return;
            lastStage = p.Stage;
            Console.WriteLine($"[{p.Percent,3:0}%] {p.Stage}");
        });
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var prepared = await launcher.PrepareAsync(entry, account, new LaunchOptions(VerifyFiles: !result.GetValue(noVerify)), progress, cancellationToken);
            Console.WriteLine($"准备用时 {stopwatch.ElapsedMilliseconds} ms；游戏目录：{prepared.Plan.WorkingDirectory}（{(entry.IsIsolated ? "版本隔离" : "共享")}）");
            Console.WriteLine($"Java：{prepared.JavaExplanation}");
            Console.WriteLine($"内存：{prepared.MemoryExplanation}");
            foreach (var warning in prepared.Plan.Warnings)
                Console.WriteLine($"[警告] {warning}");

            if (result.GetValue(argsOut) is { } outFile)
            {
                await File.WriteAllLinesAsync(outFile.FullName, prepared.Plan.Arguments, new UTF8Encoding(false), cancellationToken);
                Console.WriteLine($"参数已写入：{outFile.FullName}（共 {prepared.Plan.Arguments.Count} 项）");
            }
            if (result.GetValue(dryRun))
            {
                Console.WriteLine($"\"{prepared.Plan.JavaExecutable}\" {WindowsCommandLine.Join(prepared.Plan.Arguments)}");
                return 0;
            }

            using var session = await launcher.StartAsync(prepared, progress, cancellationToken);
            return await WatchAsync(session, result.GetValue(timeout), result.GetValue(exitOnMenu), cancellationToken);
        }
        catch (UserFacingException ex)
        {
            Console.Error.WriteLine($"【{ex.Title}】{ex.Message}");
            return 3;
        }
    });
    return command;
}

static async Task<int> WatchAsync(GameSession session, int timeoutSeconds, bool exitOnMenu, CancellationToken cancellationToken)
{
    var menuReached = false;
    var exited = new TaskCompletionSource<GameExit>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var menuCts = new CancellationTokenSource();
    session.WindowShown += (_, _) => Console.WriteLine(">>> 游戏窗口已出现");
    session.OutputReceived += (_, line) =>
    {
        if (!menuReached && line.Contains("Sound engine started", StringComparison.Ordinal))
        {
            menuReached = true;
            Console.WriteLine(">>> 检测到游戏已进入主菜单");
            if (exitOnMenu)
                menuCts.CancelAfter(TimeSpan.FromSeconds(10));
        }
    };
    _ = session.Completion.ContinueWith(t => exited.TrySetResult(t.Result), TaskScheduler.Default);
    Console.WriteLine($"游戏进程已启动（PID {session.ProcessId}），日志：{session.LogFile}");

    using var timeoutCts = timeoutSeconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)) : new CancellationTokenSource();
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token, menuCts.Token);
    try
    {
        var exit = await exited.Task.WaitAsync(linked.Token);
        Console.WriteLine($"游戏已退出，退出码 {exit.ExitCode}，运行 {exit.Duration:mm\\:ss}，{(exit.IsCrash ? "异常退出" : "正常退出")}");
        return menuReached && !exit.IsCrash ? 0 : 1;
    }
    catch (OperationCanceledException)
    {
        var reason = menuCts.IsCancellationRequested ? "已验证进入主菜单" : timeoutCts.IsCancellationRequested ? "已到达等待上限" : "已取消";
        session.Kill();
        var exit = await exited.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        Console.WriteLine($"{reason}，已结束游戏（运行 {exit.Duration:mm\\:ss}）。是否进入过主菜单：{menuReached}");
        return menuReached ? 0 : 1;
    }
}
