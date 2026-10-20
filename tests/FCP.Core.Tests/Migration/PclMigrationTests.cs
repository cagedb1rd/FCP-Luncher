using FCP.Core.Accounts;
using FCP.Core.Folders;
using FCP.Core.Instances;
using FCP.Core.Migration;
using FCP.Core.Migration.Pcl;
using FCP.Core.Platform;
using FCP.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FCP.Core.Tests.Migration;

/// <summary>只读的假注册表。</summary>
internal sealed class FakeRegistry(Dictionary<string, string>? values = null) : IRegistryReader
{
    public string? ReadString(RegistryHive hive, string subKey, string valueName, bool use32BitView = false) =>
        subKey == @"Software\PCL" && values is not null && values.TryGetValue(valueName, out var v) ? v : null;

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKey, bool use32BitView = false) => [];
}

public class PclGlobalSettingsTests
{
    [Fact]
    public void MapsTheSettingsOfThisMachine()
    {
        // 本机 PCL：LaunchRamType:1、LaunchRamCustom:34（= 18 GB），优先级 0（高），其余没有保存过（取 PCL 默认值）
        var pcl = new PclGlobalSettings { SourceDirectory = @"E:\MC\PCL", RamType = 1, RamCustomScale = 34, Priority = 0 };
        var target = new GlobalSettings();

        var items = PclGlobalSettingsMapper.Apply(pcl, target);

        target.Memory.ShouldBe(new MemorySetting(MemoryMode.Custom, 18 * 1024));
        target.Priority.ShouldBe(GamePriority.High);
        target.Visibility.ShouldBe(LauncherVisibility.KeepOpen); // PCL 默认“保持不变”
        target.Isolation.ShouldBe(IsolationPolicy.All);          // PCL 默认 4 = 全部隔离
        target.Gc.ShouldBe(GcPolicy.G1);
        target.ExtraJvmArguments.ShouldBe("");                    // PCL 默认参数不导入（已内置）
        target.VersionTypeText.ShouldBe("FCP");                   // “PCL”是 PCL 的默认值，不沿用
        target.DownloadSource.ShouldBe(DownloadSource.Auto);
        items.ShouldContain("游戏内存：自定义 18 GB");
        items.ShouldContain("游戏启动后启动器：保持不变");
    }

    [Fact]
    public void CustomValuesAreKept()
    {
        var pcl = new PclGlobalSettings
        {
            SourceDirectory = "x", Visibility = 3, IsolationLevel = 1, WindowType = 3, WindowWidth = 1600, WindowHeight = 900,
            JvmArguments = "-XX:+UseZGC", GameArguments = "--demo", Gc = 3, VersionInfo = "我的服务器", DownloadSource = 0,
        };
        var target = new GlobalSettings();
        PclGlobalSettingsMapper.Apply(pcl, target);

        target.Memory.Mode.ShouldBe(MemoryMode.Auto);
        target.Visibility.ShouldBe(LauncherVisibility.HideAndRestore);
        target.Isolation.ShouldBe(IsolationPolicy.Modded);
        target.Window.ShouldBe(new WindowSetting(false, 1600, 900));
        target.ExtraJvmArguments.ShouldBe("-XX:+UseZGC");
        target.ExtraGameArguments.ShouldBe("--demo");
        target.Gc.ShouldBe(GcPolicy.None);
        target.VersionTypeText.ShouldBe("我的服务器");
        target.DownloadSource.ShouldBe(DownloadSource.Mirror);
    }

    [Fact]
    public async Task ReaderUsesSetupIniRegistryAndDefaults()
    {
        using var mc = new TempMinecraft();
        var pclDir = Path.GetDirectoryName(mc.Root)!;
        mc.File_(@"..\PCL\Setup.ini", "LaunchRamType:1\nLaunchRamCustom:34\nLaunchFolderSelect:$.minecraft\\\n");
        var registry = new FakeRegistry(new() { ["LaunchArgumentPriority"] = "0" });

        var pcl = (await PclGlobalSettingsReader.ReadAsync(pclDir, registry, TestContext.Current.CancellationToken))!;

        pcl.RamType.ShouldBe(1);
        pcl.RamCustomScale.ShouldBe(34);
        pcl.Priority.ShouldBe(0);
        pcl.Visibility.ShouldBe(5);
        pcl.IsolationLevel.ShouldBe(4);
        (await PclGlobalSettingsReader.ReadAsync(mc.Root, registry, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"Path":"C:\\Program Files\\Java\\jdk-17\\bin\\","VersionString":"17.0.10","Is64Bit":true}""", @"C:\Program Files\Java\jdk-17\bin\java.exe")]
    [InlineData("""{"Path":"D:\\jdk\\bin\\javaw.exe"}""", @"D:\jdk\bin\javaw.exe")]
    [InlineData("使用全局设置", null)]
    [InlineData("{坏掉的 JSON", null)]
    public void ParsesPclJavaSelection(string value, string? expected) => InstanceCatalog.ParsePclJavaSelection(value).ShouldBe(expected);
}

public class AccountClueTests : IDisposable
{
    private readonly TempMinecraft _mc = new();

    public void Dispose() => _mc.Dispose();

    private InstanceEntry Entry(PclInstanceInfo? pcl = null)
    {
        _mc.Version("整合包", TempMinecraft.Vanilla("1.20.1"));
        var instance = new InstanceScanner(NullLogger<InstanceScanner>.Instance).ScanOne(_mc.Root, "整合包")!;
        return new InstanceEntry(instance, new InstanceMeta(), InstanceGroup.Regular, false, true, null, null, pcl);
    }

    [Fact]
    public void ClassifiesEachKindOfIdentity()
    {
        var entry = Entry();
        AccountClues.Classify(entry, "Steve", OfflineUuid.PclLegacy("Steve"), null).Kind.ShouldBe(AccountClueKind.PclOffline);
        AccountClues.Classify(entry, "Steve", OfflineUuid.PclLegacy("Steve", SkinModel.Alex), null).Kind.ShouldBe(AccountClueKind.PclOffline);
        AccountClues.Classify(entry, "Steve", OfflineUuid.Standard("Steve"), null).Kind.ShouldBe(AccountClueKind.StandardOffline);
        AccountClues.Classify(entry, "Notch", Guid.Parse("069a79f4-44e9-4726-a5be-fca90e38aaf5"), null).Kind.ShouldBe(AccountClueKind.LikelyPremium);

        var littleSkin = Entry(new PclInstanceInfo { LoginRequirement = 4, AuthServer = "https://littleskin.cn/api/yggdrasil", AuthServerName = "LittleSkin 登录" });
        var clue = AccountClues.Classify(littleSkin, "BlockMaster66", Guid.Parse("3f2b9c1e-0000-4000-8000-000000000000"), null);
        clue.Kind.ShouldBe(AccountClueKind.SkinSite);
        clue.ServerName.ShouldBe("LittleSkin");
    }

    [Fact]
    public void ReadsUsercacheNewestFirst()
    {
        var entry = Entry();
        _mc.File_("usercache.json", $$"""
            [ {"name":"Old","uuid":"{{OfflineUuid.PclLegacy("Old")}}","expiresOn":"2026-01-01 10:00:00 +0800"},
              {"name":"New","uuid":"{{OfflineUuid.Standard("New")}}","expiresOn":"2026-11-01 10:00:00 +0800"},
              {"name":"","uuid":"bad"} ]
            """);
        var clues = AccountClues.Collect(entry with { IsIsolated = false });
        clues.Select(c => c.PlayerName).ShouldBe(["New", "Old"]);
    }
}

public class PclMigrationServiceTests : IDisposable
{
    private readonly TempMinecraft _mc = new();
    private readonly string _config = Path.Combine(Path.GetTempPath(), "fcp-tests", Guid.NewGuid().ToString("N"), "config");

    public void Dispose()
    {
        _mc.Dispose();
        if (Directory.Exists(_config))
            Directory.Delete(_config, recursive: true);
    }

    [Fact]
    public async Task ImportsOnce_CreatesSameUuidOfflineAccount_AndNeverWritesToPcl()
    {
        var ct = TestContext.Current.CancellationToken;
        var pclDir = Path.GetDirectoryName(_mc.Root)!;
        _mc.File_(@"..\PCL\Setup.ini", "LaunchRamType:1\nLaunchRamCustom:34\n")
           .Version("整合包", TempMinecraft.Vanilla("1.20.1"))
           .File_(@"versions\整合包\mods\a.jar")
           .File_(@"versions\整合包\usercache.json", $$"""[ {"name":"方块人","uuid":"{{OfflineUuid.PclLegacy("方块人", SkinModel.Alex)}}","expiresOn":"2026-11-01 10:00:00 +0800"} ]""");
        var pclFiles = Directory.EnumerateFiles(pclDir, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

        var paths = new AppPaths(_config, _config, _config, false);
        var store = new SettingsStore(paths);
        var accounts = new AccountService(paths, store);
        var service = new PclMigrationService(store, new FakeRegistry(), new InstanceScanner(NullLogger<InstanceScanner>.Instance),
            new InstanceCatalog(store), accounts, NullLogger<PclMigrationService>.Instance);
        var discovery = new GameFolderDiscoveryResult([new GameFolder(_mc.Root, "当前文件夹", GameFolderSource.Pcl)], _mc.Root, [pclDir]);

        var record = (await service.RunOnceAsync(discovery, ct))!;

        (await store.LoadAsync(ct)).Memory.CustomMb.ShouldBe(18 * 1024);
        var account = (await accounts.GetSelectedAsync(ct)).ShouldNotBeNull();
        account.PlayerName.ShouldBe("方块人");
        account.Uuid.ShouldBe(OfflineUuid.PclLegacy("方块人", SkinModel.Alex).ToString("N")); // 存档中的 UUID，而不是重新计算的
        record.Items.ShouldContain(i => i.StartsWith("离线账号 方块人"));

        // 第二次不再导入：用户之后在 FCP 中的修改不会被覆盖
        (await store.LoadAsync(ct)).Memory = new MemorySetting(MemoryMode.Auto, 4096);
        (await service.RunOnceAsync(discovery, ct)).ShouldBe(record);
        (await store.LoadAsync(ct)).Memory.Mode.ShouldBe(MemoryMode.Auto);

        // PCL 的文件一个字节都没有变
        foreach (var (file, bytes) in pclFiles)
            File.ReadAllBytes(file).ShouldBe(bytes);
    }
}
