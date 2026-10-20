using System.CommandLine;
using System.Diagnostics;
using System.Text;
using FCP.Core.Accounts;
using FCP.Core.Java;
using FCP.Core.Launch;
using FCP.Engine.CmlLib;

Console.OutputEncoding = Encoding.UTF8;

var root = new RootCommand("FCP 命令行工具：在没有界面的情况下验证核心功能");
root.Subcommands.Add(UuidCommand());
root.Subcommands.Add(LaunchCommand());
return await root.Parse(args).InvokeAsync();

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

static Command LaunchCommand()
{
    var instance = new Option<DirectoryInfo>("--instance") { Description = "版本文件夹（...\\.minecraft\\versions\\<名称>）", Required = true };
    var java = new Option<FileInfo>("--java") { Description = "java.exe 的路径", Required = true };
    var offline = new Option<string>("--offline") { Description = "离线玩家名", DefaultValueFactory = _ => "Player" };
    var scheme = new Option<UuidScheme>("--uuid-scheme") { Description = "离线 UUID 算法", DefaultValueFactory = _ => UuidScheme.Standard };
    var memory = new Option<int>("--memory") { Description = "最大内存（MB）", DefaultValueFactory = _ => 4096 };
    var isolated = new Option<bool?>("--isolated") { Description = "是否版本隔离（默认：版本文件夹中有 mods 或 saves 时开启）" };
    var dryRun = new Option<bool>("--dry-run") { Description = "只输出启动参数，不启动游戏" };
    var verify = new Option<bool>("--verify-files") { Description = "启动前校验并补全文件" };
    var argsOut = new Option<FileInfo?>("--args-out") { Description = "把参数逐行写入文件（用于对比测试）" };
    var timeout = new Option<int>("--timeout") { Description = "实际启动时最多等待的秒数，超时后结束游戏（0 = 一直等到游戏退出）", DefaultValueFactory = _ => 0 };
    var exitOnMenu = new Option<bool>("--exit-on-menu") { Description = "检测到进入主菜单 10 秒后自动结束游戏（用于自动化验证）" };

    var command = new Command("launch", "构建启动参数并启动游戏") { instance, java, offline, scheme, memory, isolated, dryRun, verify, argsOut, timeout, exitOnMenu };
    command.SetAction(async (result, cancellationToken) =>
    {
        var location = GameInstanceLocation.FromVersionDirectory(result.GetValue(instance)!.FullName);
        var javaExe = result.GetValue(java)!.FullName;
        var javaInfo = JavaReleaseFile.TryRead(javaExe);
        if (javaInfo is null)
        {
            Console.Error.WriteLine($"无法读取 Java 版本（找不到 release 文件）：{javaExe}");
            return 2;
        }

        var playerName = result.GetValue(offline)!;
        var uuid = result.GetValue(scheme) == UuidScheme.PclLegacy ? OfflineUuid.PclLegacy(playerName) : OfflineUuid.Standard(playerName);
        var isIsolated = result.GetValue(isolated) ?? HasContent(Path.Combine(location.VersionDirectory, "mods")) || HasContent(Path.Combine(location.VersionDirectory, "saves"));

        var request = new LaunchRequest
        {
            Instance = location,
            GameDirectory = isIsolated ? location.VersionDirectory : location.RootDirectory,
            Java = new JavaSelection(javaExe, javaInfo.Version.Major),
            Session = LaunchSession.Offline(playerName, uuid),
            MaxMemoryMb = result.GetValue(memory),
            WindowWidth = 854,
            WindowHeight = 480,
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FCP/0.0.1");
        var engine = new CmlLibLaunchEngine(http);

        if (result.GetValue(verify))
        {
            Console.WriteLine("正在校验文件…");
            var lastReport = Stopwatch.StartNew();
            await engine.VerifyAndRepairFilesAsync(location, new Progress<FileRepairProgress>(p =>
            {
                if (lastReport.ElapsedMilliseconds < 500)
                    return;
                lastReport.Restart();
                Console.WriteLine($"  {p.Stage} {p.CompletedFiles}/{p.TotalFiles}");
            }), cancellationToken);
            Console.WriteLine("文件校验完成。");
        }

        var stopwatch = Stopwatch.StartNew();
        var plan = await engine.BuildLaunchPlanAsync(request, cancellationToken);
        Console.WriteLine($"参数构建用时 {stopwatch.ElapsedMilliseconds} ms，Java {javaInfo.Version}（{javaInfo.Vendor}），游戏目录：{plan.WorkingDirectory}");
        foreach (var warning in plan.Warnings)
            Console.WriteLine($"[警告] {warning}");

        var masked = plan.Arguments.Select(a => a.Replace(request.Session.AccessToken, "***", StringComparison.Ordinal)).ToList();
        if (result.GetValue(argsOut) is { } outFile)
        {
            await File.WriteAllLinesAsync(outFile.FullName, masked, new UTF8Encoding(false), cancellationToken);
            Console.WriteLine($"参数已写入：{outFile.FullName}（共 {masked.Count} 项）");
        }

        if (result.GetValue(dryRun))
        {
            Console.WriteLine($"\"{plan.JavaExecutable}\" {WindowsCommandLine.Join(masked)}");
            return 0;
        }

        return await RunGameAsync(plan, javaInfo.Version.Major, location, result.GetValue(timeout), result.GetValue(exitOnMenu), cancellationToken);
    });
    return command;
}

static async Task<int> RunGameAsync(LaunchPlan plan, int javaMajor, GameInstanceLocation location, int timeoutSeconds, bool exitOnMenu, CancellationToken cancellationToken)
{
    using var menuCts = new CancellationTokenSource();
    var argFileDir = Path.Combine(Path.GetTempPath(), "fcp-args");
    var startInfo = LaunchProcessFactory.Create(plan, javaMajor, argFileDir);
    var logPath = Path.Combine(location.VersionDirectory, "logs", $"fcp-cli-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    await using var log = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };

    using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    var mainMenuReached = false;
    void OnLine(string? line, bool isError)
    {
        if (line is null)
            return;
        lock (log)
            log.WriteLine(line);
        // 声音引擎在资源重载的最后阶段启动，随后即进入主菜单
        if (!mainMenuReached && line.Contains("Sound engine started", StringComparison.Ordinal))
        {
            mainMenuReached = true;
            Console.WriteLine($">>> 检测到游戏已进入主菜单（{line.Trim()}）");
            if (exitOnMenu)
                menuCts.CancelAfter(TimeSpan.FromSeconds(10));
        }
        if (isError || line.Contains("ERROR", StringComparison.Ordinal) || line.Contains("FATAL", StringComparison.Ordinal))
            Console.WriteLine((isError ? "[stderr] " : "") + line);
    }
    process.OutputDataReceived += (_, e) => OnLine(e.Data, false);
    process.ErrorDataReceived += (_, e) => OnLine(e.Data, true);

    var stopwatch = Stopwatch.StartNew();
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    Console.WriteLine($"游戏进程已启动（PID {process.Id}），日志：{logPath}");

    using var timeoutCts = timeoutSeconds > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)) : new CancellationTokenSource();
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token, menuCts.Token);
    try
    {
        await process.WaitForExitAsync(linked.Token);
        Console.WriteLine($"游戏已退出，退出码 {process.ExitCode}，运行 {stopwatch.Elapsed:mm\\:ss}，是否进入过主菜单：{mainMenuReached}");
        return mainMenuReached && process.ExitCode == 0 ? 0 : 1;
    }
    catch (OperationCanceledException)
    {
        var reason = menuCts.IsCancellationRequested ? "已验证进入主菜单" : timeoutCts.IsCancellationRequested ? "已到达等待上限" : "已取消";
        Console.WriteLine($"{reason}（{stopwatch.Elapsed:mm\\:ss}），结束游戏进程。是否进入过主菜单：{mainMenuReached}");
        process.Kill(entireProcessTree: true);
        return mainMenuReached ? 0 : 1;
    }
}

static bool HasContent(string directory) =>
    Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
