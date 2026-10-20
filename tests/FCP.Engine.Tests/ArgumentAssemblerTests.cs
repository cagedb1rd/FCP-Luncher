using FCP.Core.Accounts;
using FCP.Core.Launch;
using FCP.Engine.CmlLib;

namespace FCP.Engine.Tests;

/// <summary>
/// 使用合成夹具 tests/fixtures/minecraft-basic：
/// 子版本 “测试版本 Ⅱ”（中文 + 全角字符 + 空格）继承 “1.20.1-mini”。
/// </summary>
public sealed class ArgumentAssemblerTests : IDisposable
{
    private const string ChildName = "测试版本 Ⅱ";
    private readonly string _root;
    private readonly HttpClient _http = new();

    public ArgumentAssemblerTests()
    {
        // 复制到临时目录：构建参数时会解压 natives，不能写进仓库里的夹具
        _root = Path.Combine(Path.GetTempPath(), "fcp-tests", Guid.NewGuid().ToString("N"), ".minecraft");
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "fixtures", "minecraft-basic"), _root);
    }

    public void Dispose()
    {
        _http.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true); } catch (IOException) { }
    }

    private LaunchRequest Request(Action<LaunchRequestBuilder>? configure = null)
    {
        var builder = new LaunchRequestBuilder(new GameInstanceLocation(_root, ChildName));
        configure?.Invoke(builder);
        return builder.Build();
    }

    private Task<LaunchPlan> BuildAsync(LaunchRequest request) =>
        new CmlLibLaunchEngine(_http).BuildLaunchPlanAsync(request, TestContext.Current.CancellationToken);

    private static string ValueAfter(IReadOnlyList<string> args, string key) => args[args.ToList().IndexOf(key) + 1];

    [Fact]
    public async Task Classpath_is_child_first_and_deduplicated_by_artifact()
    {
        var plan = await BuildAsync(Request());
        var entries = ValueAfter(plan.Arguments, "-cp").Split(';');
        var names = entries.Select(Path.GetFileName).ToList();

        names.Take(3).ShouldBe(["loader-1.0.jar", "shared-2.0.jar", "alpha-1.0.jar"]);
        names.ShouldNotContain("shared-1.0.jar");   // 子版本的 shared:2.0 优先
        names.ShouldNotContain("osxonly-1.0.jar");  // 规则不满足
        Path.GetExtension(entries[^1]).ShouldBe(".jar"); // 最后一项是游戏主 jar
    }

    [Fact]
    public async Task Inherited_arguments_are_merged_parent_first()
    {
        var plan = await BuildAsync(Request());
        var args = plan.Arguments;

        plan.MainClass.ShouldBe("cpw.mods.bootstraplauncher.BootstrapLauncher");
        args.ShouldContain($"-DlibraryDirectory={Path.Combine(_root, "libraries")}");
        args.ShouldContain($"-DignoreList=client-extra,{ChildName}.jar");
        ValueAfter(args, "--version").ShouldBe(ChildName);
        ValueAfter(args, "--launchTarget").ShouldBe("forgeclient");
        args.ShouldContain("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
        args.ShouldNotContain("-XstartOnFirstThread");
    }

    [Fact]
    public async Task Placeholders_without_values_are_removed_with_their_keys()
    {
        var plan = await BuildAsync(Request());

        plan.Arguments.ShouldNotContain("--clientId");
        plan.Arguments.ShouldNotContain("--xuid");
        plan.Arguments.ShouldNotContain("--quickPlayMultiplayer");
        plan.Arguments.ShouldAllBe(a => !a.Contains("${"));
        plan.Arguments.ShouldAllBe(a => !a.Contains('\0'));
    }

    [Fact]
    public async Task Isolated_instance_uses_version_directory_as_game_directory()
    {
        var location = new GameInstanceLocation(_root, ChildName);
        var plan = await BuildAsync(Request(b => b.GameDirectory = location.VersionDirectory));

        ValueAfter(plan.Arguments, "--gameDir").ShouldBe(location.VersionDirectory);
        plan.WorkingDirectory.ShouldBe(location.VersionDirectory);
        ValueAfter(plan.Arguments, "--assetsDir").ShouldBe(Path.Combine(_root, "assets"));
    }

    [Fact]
    public async Task Natives_go_to_our_own_directory()
    {
        var plan = await BuildAsync(Request());
        Path.GetFileName(plan.NativesDirectory).ShouldBe("natives-fcp");
        plan.Arguments.ShouldContain($"-Djava.library.path={plan.NativesDirectory}");
    }

    [Fact]
    public async Task Session_window_and_memory_are_applied()
    {
        var uuid = OfflineUuid.PclLegacy("Steve");
        var plan = await BuildAsync(Request(b =>
        {
            b.Session = LaunchSession.Offline("Steve", uuid);
            b.MaxMemoryMb = 2048;
        }));

        ValueAfter(plan.Arguments, "--username").ShouldBe("Steve");
        ValueAfter(plan.Arguments, "--uuid").ShouldBe(uuid.ToString("N"));
        ValueAfter(plan.Arguments, "--width").ShouldBe("854");
        ValueAfter(plan.Arguments, "--height").ShouldBe("480");
        plan.Arguments.ShouldContain("-Xmx2048m");
        plan.Arguments.ShouldContain("-Dminecraft.launcher.brand=FCP");
    }

    [Fact]
    public async Task Server_address_uses_quick_play_when_supported()
    {
        var plan = await BuildAsync(Request(b => b.ServerAddress = "mc.example.com:25566"));
        ValueAfter(plan.Arguments, "--quickPlayMultiplayer").ShouldBe("mc.example.com:25566");
        plan.Arguments.ShouldNotContain("--server");
    }

    [Fact]
    public async Task User_gc_flags_replace_default_gc()
    {
        var plan = await BuildAsync(Request(b => b.ExtraJvm = ["-XX:+UseZGC"]));
        plan.Arguments.ShouldContain("-XX:+UseZGC");
        plan.Arguments.ShouldNotContain("-XX:+UseG1GC");
    }

    [Fact]
    public async Task Arguments_survive_windows_quoting_round_trip()
    {
        var plan = await BuildAsync(Request());
        WindowsCommandLine.Split(WindowsCommandLine.Join(plan.Arguments)).ShouldBe(plan.Arguments);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private sealed class LaunchRequestBuilder(GameInstanceLocation location)
    {
        public string GameDirectory { get; set; } = location.RootDirectory;
        public LaunchSession Session { get; set; } = LaunchSession.Offline("Player", OfflineUuid.Standard("Player"));
        public int MaxMemoryMb { get; set; } = 1024;
        public string? ServerAddress { get; set; }
        public IReadOnlyList<string> ExtraJvm { get; set; } = [];

        public LaunchRequest Build() => new()
        {
            Instance = location,
            GameDirectory = GameDirectory,
            Java = new JavaSelection(@"C:\Java\bin\java.exe", 17),
            Session = Session,
            MaxMemoryMb = MaxMemoryMb,
            WindowWidth = 854,
            WindowHeight = 480,
            ServerAddress = ServerAddress,
            ExtraJvmArguments = ExtraJvm,
        };
    }
}
