using FCP.Core.Instances;
using FCP.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FCP.Core.Tests.Instances;

public sealed class InstanceScannerTests : IDisposable
{
    private readonly TempMinecraft _mc = new();
    private readonly InstanceScanner _scanner = new(NullLogger<InstanceScanner>.Instance);

    public void Dispose() => _mc.Dispose();

    private GameInstance Scan(string name) => _scanner.ScanOne(_mc.Root, name)!;

    [Fact]
    public void Vanilla_release_is_recognized()
    {
        _mc.Version("1.20.1", TempMinecraft.Vanilla("1.20.1"));
        var i = Scan("1.20.1");
        i.Health.ShouldBe(InstanceHealth.Ok);
        i.MinecraftVersion.ShouldBe("1.20.1");
        i.VersionType.ShouldBe(GameVersionType.Release);
        i.Loaders.ShouldBeEmpty();
        i.RecommendedJavaMajor.ShouldBe(17);
        i.Description.ShouldBe("1.20.1");
    }

    [Fact]
    public void Official_forge_child_inherits_from_vanilla()
    {
        _mc.Version("1.20.1", TempMinecraft.Vanilla("1.20.1"))
           .Version("1.20.1-forge-47.4.10", """
           {
             "id": "1.20.1-forge-47.4.10", "inheritsFrom": "1.20.1", "type": "release", "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
             "arguments": { "game": ["--launchTarget", "forgeclient", "--fml.forgeVersion", "47.4.10", "--fml.mcVersion", "1.20.1"] },
             "libraries": [ { "name": "net.minecraftforge:fmlloader:1.20.1-47.4.10" } ]
           }
           """);
        var i = Scan("1.20.1-forge-47.4.10");
        i.Health.ShouldBe(InstanceHealth.Ok);
        i.MinecraftVersion.ShouldBe("1.20.1");
        i.Loaders.ShouldBe([new LoaderInfo(LoaderType.Forge, "47.4.10")]);
        i.RecommendedJavaMajor.ShouldBe(17); // 继承自原版
        i.IsModded.ShouldBeTrue();
        i.Description.ShouldBe("1.20.1 · Forge 47.4.10");
    }

    [Fact]
    public void Child_without_own_jar_uses_parent_game_jar()
    {
        _mc.Version("1.20.1", TempMinecraft.Vanilla("1.20.1"))
           .Version("Forge 子版本", """{ "id": "x", "inheritsFrom": "1.20.1", "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher" }""", withJar: false);
        Scan("Forge 子版本").Health.ShouldBe(InstanceHealth.Ok);
    }

    [Fact]
    public void Missing_game_jar_is_reported_not_downloaded()
    {
        // FCP 不提供游戏本体下载（D-027）：本体缺失时归入“有问题的版本”，并说明缺的是哪个文件
        _mc.Version("1.20.1", TempMinecraft.Vanilla("1.20.1"), withJar: false)
           .Version("Forge 子版本", """{ "id": "x", "inheritsFrom": "1.20.1", "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher" }""", withJar: false);

        var vanilla = Scan("1.20.1");
        vanilla.Health.ShouldBe(InstanceHealth.MissingGameJar);
        vanilla.MinecraftVersion.ShouldBe("1.20.1");
        var child = Scan("Forge 子版本");
        child.Health.ShouldBe(InstanceHealth.MissingGameJar);
        child.Problem.ShouldBe(@"缺少游戏本体文件（versions\1.20.1\1.20.1.jar）");
        InstanceCatalog.Classify(child, favorite: false, hidden: false).ShouldBe(InstanceGroup.Broken);
    }

    [Fact]
    public void Missing_parent_is_reported()
    {
        _mc.Version("fabric-loader-0.16.10-1.21.4", """
           { "id": "fabric-loader-0.16.10-1.21.4", "inheritsFrom": "1.21.4", "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
             "libraries": [ { "name": "net.fabricmc:fabric-loader:0.16.10" } ] }
           """);
        var i = Scan("fabric-loader-0.16.10-1.21.4");
        i.Health.ShouldBe(InstanceHealth.MissingParent);
        i.Problem.ShouldBe("缺少前置版本 1.21.4");
        i.Loaders.ShouldBe([new LoaderInfo(LoaderType.Fabric, "0.16.10")]);
    }

    [Fact]
    public void Merged_forge_json_uses_fml_arguments_for_versions()
    {
        // PCL / HMCL 安装的整合包：单个合并后的 JSON，没有 inheritsFrom，版本名是中文
        _mc.Version("香草纪元：食旅纪行", """
           { "id": "香草纪元：食旅纪行", "type": "release", "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
             "javaVersion": { "majorVersion": 17 },
             "arguments": { "game": ["--launchTarget", "forgeclient", "--fml.forgeVersion", "47.3.33", "--fml.mcVersion", "1.20.1"] },
             "libraries": [ { "name": "net.minecraftforge:forge:1.20.1-47.3.33:universal" }, { "name": "net.minecraftforge:fmlloader:1.20.1-47.3.33" } ] }
           """);
        var i = Scan("香草纪元：食旅纪行");
        i.MinecraftVersion.ShouldBe("1.20.1");
        i.Loaders.ShouldBe([new LoaderInfo(LoaderType.Forge, "47.3.33")]);
    }

    [Fact]
    public void Neoforge_is_recognized_and_not_reported_as_forge()
    {
        _mc.Version("1.21.1", TempMinecraft.Vanilla("1.21.1", java: 21))
           .Version("neoforge-21.1.90", """
           { "id": "neoforge-21.1.90", "inheritsFrom": "1.21.1", "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
             "arguments": { "game": ["--fml.neoForgeVersion", "21.1.90", "--fml.mcVersion", "1.21.1"] },
             "libraries": [ { "name": "net.neoforged.fancymodloader:loader:4.0.31" }, { "name": "net.neoforged:neoforge:21.1.90:universal" } ] }
           """);
        Scan("neoforge-21.1.90").Loaders.ShouldBe([new LoaderInfo(LoaderType.NeoForge, "21.1.90")]);
    }

    [Fact]
    public void Neoforge_for_1_20_1_uses_forge_artifact_name()
    {
        _mc.Version("1.20.1", TempMinecraft.Vanilla("1.20.1"))
           .Version("neoforge-1.20.1", """
           { "id": "neoforge-1.20.1", "inheritsFrom": "1.20.1", "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
             "arguments": { "game": ["--fml.forgeVersion", "47.1.106", "--fml.mcVersion", "1.20.1"] },
             "libraries": [ { "name": "net.neoforged:forge:1.20.1-47.1.106:universal" }, { "name": "net.minecraftforge:fmlloader:1.20.1-47.1.106" } ] }
           """);
        Scan("neoforge-1.20.1").Loaders.ShouldBe([new LoaderInfo(LoaderType.NeoForge, "47.1.106")]);
    }

    [Fact]
    public void Optifine_with_forge_lists_both()
    {
        _mc.Version("1.12.2", TempMinecraft.Vanilla("1.12.2", java: 8, releaseTime: "2017-09-18T08:39:46+00:00"))
           .Version("1.12.2-OptiFine-Forge", """
           { "id": "1.12.2-OptiFine-Forge", "inheritsFrom": "1.12.2", "mainClass": "net.minecraft.launchwrapper.Launch",
             "minecraftArguments": "--username ${auth_player_name} --tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --tweakClass optifine.OptiFineForgeTweaker",
             "libraries": [ { "name": "net.minecraftforge:forge:1.12.2-14.23.5.2860" }, { "name": "optifine:OptiFine:1.12.2_HD_U_G5" } ] }
           """);
        var i = Scan("1.12.2-OptiFine-Forge");
        i.Loaders.ShouldBe([new LoaderInfo(LoaderType.Forge, "14.23.5.2860"), new LoaderInfo(LoaderType.OptiFine, "HD U G5")]);
        i.PrimaryLoader!.Type.ShouldBe(LoaderType.Forge);
        i.RecommendedJavaMajor.ShouldBe(8);
    }

    [Fact]
    public void Hmcl_patches_format_is_merged()
    {
        _mc.Version("HMCL版本", """
           { "id": "HMCL版本", "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
             "patches": [
               { "id": "game", "version": "1.21.4", "priority": 0, "mainClass": "net.minecraft.client.main.Main", "type": "release",
                 "releaseTime": "2024-12-03T10:12:57+00:00", "javaVersion": { "majorVersion": 21 },
                 "libraries": [ { "name": "com.mojang:brigadier:1.3.10" } ] },
               { "id": "fabric", "version": "0.16.10", "priority": 30000, "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
                 "libraries": [ { "name": "net.fabricmc:fabric-loader:0.16.10" } ] }
             ] }
           """);
        var i = Scan("HMCL版本");
        i.IsHmclFormat.ShouldBeTrue();
        i.Health.ShouldBe(InstanceHealth.Ok);
        i.Loaders.ShouldBe([new LoaderInfo(LoaderType.Fabric, "0.16.10")]);
        i.RecommendedJavaMajor.ShouldBe(21);
        i.VersionType.ShouldBe(GameVersionType.Release);
    }

    [Fact]
    public void Json_with_different_file_name_is_found()
    {
        _mc.Version("改过名的版本", TempMinecraft.Vanilla("1.21.4", java: 21), fileName: "1.21.4.json");
        var i = Scan("改过名的版本");
        i.Health.ShouldBe(InstanceHealth.Ok);
        i.JsonPath.ShouldEndWith("1.21.4.json");
        i.MinecraftVersion.ShouldBe("1.21.4");
    }

    [Fact]
    public void Broken_json_and_missing_main_class_are_reported()
    {
        _mc.Version("坏的", "{ not json")
           .Version("没有主类", """{ "id": "没有主类", "libraries": [] }""");
        Scan("坏的").Health.ShouldBe(InstanceHealth.Broken);
        Scan("坏的").Problem.ShouldBe("版本 JSON 文件已损坏");
        // 同名 JSON 缺少 mainClass：显示为“有问题的版本”，让用户知道它坏了
        Scan("没有主类").Health.ShouldBe(InstanceHealth.Broken);
        Scan("没有主类").Problem.ShouldBe("版本 JSON 中没有 mainClass");
    }

    [Fact]
    public void Folders_being_installed_by_pcl_are_skipped()
    {
        _mc.Version("安装中", TempMinecraft.Vanilla("1.20.1")).File_(@"versions\安装中\.pclignore");
        _scanner.ScanOne(_mc.Root, "安装中").ShouldBeNull();
    }

    [Theory]
    [InlineData("24w14potato", "snapshot", GameVersionType.AprilFools)]
    [InlineData("24w14a", null, GameVersionType.Snapshot)]
    [InlineData("26.4-snapshot-5", null, GameVersionType.Snapshot)]
    [InlineData("1.21-pre1", null, GameVersionType.Snapshot)]
    [InlineData("26.1", "release", GameVersionType.Release)]
    [InlineData("b1.7.3", null, GameVersionType.Old)]
    [InlineData("1.8.9", null, GameVersionType.Release)]
    public void Version_types(string version, string? jsonType, GameVersionType expected)
    {
        InstanceScanner.DetectVersionType(version, jsonType).ShouldBe(expected);
    }

    [Fact]
    public async Task Scan_lists_all_versions_in_parallel()
    {
        _mc.Version("1.20.1", TempMinecraft.Vanilla("1.20.1")).Version("1.21.4", TempMinecraft.Vanilla("1.21.4", java: 21));
        Directory.CreateDirectory(_mc.VersionDir("空目录"));
        var all = await _scanner.ScanAsync(_mc.Root, TestContext.Current.CancellationToken);
        all.Select(i => i.Name).Order().ShouldBe(["1.20.1", "1.21.4"]);
    }
}

public sealed class InstanceCatalogTests : IDisposable
{
    private readonly TempMinecraft _mc = new();
    private readonly InstanceScanner _scanner = new(NullLogger<InstanceScanner>.Instance);

    public void Dispose() => _mc.Dispose();

    [Fact]
    public async Task Pcl_settings_are_read_without_modification()
    {
        _mc.Version("整合包", TempMinecraft.Vanilla("1.20.1"))
           .File_(@"versions\整合包\PCL\Setup.ini",
                  "VersionArgumentIndieV2:True\nVersionRamType:1\nVersionRamCustom:27\nIsStar:True\nVersionServerLogin:4\n" +
                  "VersionServerAuthServer:https://littleskin.cn/api/yggdrasil\nVersionServerAuthName:LittleSkin 登录\nCustomInfo:我的整合包\n");
        var iniPath = Path.Combine(_mc.VersionDir("整合包"), "PCL", "Setup.ini");
        var before = await File.ReadAllBytesAsync(iniPath, TestContext.Current.CancellationToken);

        var instance = _scanner.ScanOne(_mc.Root, "整合包")!;
        var pcl = (await InstanceCatalog.ReadPclAsync(instance, TestContext.Current.CancellationToken))!;

        pcl.Isolated.ShouldBe(true);
        pcl.MemoryMb.ShouldBe(10240);
        pcl.IsFavorite.ShouldBeTrue();
        pcl.LoginRequirement.ShouldBe(4);
        pcl.AuthServer.ShouldBe("https://littleskin.cn/api/yggdrasil");
        pcl.CustomInfo.ShouldBe("我的整合包");
        (await File.ReadAllBytesAsync(iniPath, TestContext.Current.CancellationToken)).ShouldBe(before);
    }

    [Fact]
    public void Isolation_follows_ours_then_pcl_then_content_then_policy()
    {
        _mc.Version("原版", TempMinecraft.Vanilla("1.20.1"));
        var vanilla = _scanner.ScanOne(_mc.Root, "原版")!;

        InstanceCatalog.ResolveIsolation(vanilla, ours: false, pcl: true, IsolationPolicy.All).ShouldBeFalse();
        InstanceCatalog.ResolveIsolation(vanilla, ours: null, pcl: true, IsolationPolicy.Off).ShouldBeTrue();
        InstanceCatalog.ResolveIsolation(vanilla, ours: null, pcl: null, IsolationPolicy.ModdedAndNonRelease).ShouldBeFalse();
        InstanceCatalog.ResolveIsolation(vanilla, ours: null, pcl: null, IsolationPolicy.All).ShouldBeTrue();

        // 版本目录里已经有存档 → 隔离（与 PCL 的推断规则一致）
        Directory.CreateDirectory(Path.Combine(_mc.VersionDir("原版"), "saves", "新的世界"));
        InstanceCatalog.ResolveIsolation(vanilla, ours: null, pcl: null, IsolationPolicy.Off).ShouldBeTrue();
    }

    [Fact]
    public void Classification_order_matches_pcl()
    {
        var ok = new GameInstance { RootDirectory = _mc.Root, Name = "a", VersionType = GameVersionType.Release };
        var modded = ok with { Loaders = [new LoaderInfo(LoaderType.Fabric, "0.16.10")] };
        var optifine = ok with { Loaders = [new LoaderInfo(LoaderType.OptiFine, "HD U I6")] };
        var broken = ok with { Health = InstanceHealth.Broken };

        InstanceCatalog.Classify(ok, favorite: false, hidden: false).ShouldBe(InstanceGroup.Regular);
        InstanceCatalog.Classify(optifine, false, false).ShouldBe(InstanceGroup.Regular);
        InstanceCatalog.Classify(modded, false, false).ShouldBe(InstanceGroup.Modded);
        InstanceCatalog.Classify(modded, favorite: true, false).ShouldBe(InstanceGroup.Favorite);
        InstanceCatalog.Classify(ok with { VersionType = GameVersionType.Snapshot }, false, false).ShouldBe(InstanceGroup.Rare);
        InstanceCatalog.Classify(broken, true, false).ShouldBe(InstanceGroup.Broken);
        InstanceCatalog.Classify(modded, true, hidden: true).ShouldBe(InstanceGroup.Hidden);
    }
}
