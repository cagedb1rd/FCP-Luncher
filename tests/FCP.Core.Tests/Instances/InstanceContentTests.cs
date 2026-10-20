using System.IO.Compression;
using System.Text;
using FCP.Core.Instances;

namespace FCP.Core.Tests.Instances;

public class InstanceContentTests : IDisposable
{
    private readonly TempMinecraft _mc = new();

    public void Dispose() => _mc.Dispose();

    private string Jar(string fileName, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_mc.Root, "mods", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        return path;
    }

    [Fact]
    public void ReadsFabricForgeNeoForgeAndLegacyMetadata()
    {
        var fabric = Jar("sodium.jar", ("fabric.mod.json", """{ "id": "sodium", "name": "Sodium", "version": "0.6.0", "description": "渲染优化" }"""));
        var forge = Jar("jei.jar",
            ("META-INF/mods.toml", "modLoader=\"javafml\"\n[[mods]]\nmodId=\"jei\"\nversion=\"${file.jarVersion}\"\ndisplayName=\"Just Enough Items\"\ndescription='''物品管理'''\n[[dependencies.jei]]\nmodId=\"forge\"\n"),
            ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\r\nImplementation-Version: 15.20.0.106\r\n"));
        var neo = Jar("neo.jar", ("META-INF/neoforge.mods.toml", "[[mods]]\nmodId = \"neo_mod\"\nversion = \"1.2.3\"\ndisplayName = \"新 Mod\"\n"));
        var legacy = Jar("old.jar", ("mcmod.info", """[ { "modid": "oldmod", "name": "Old Mod", "version": "1.7.10-1.0" } ]"""));

        InstanceContent.ReadMetadata(fabric).ShouldBe(new ModMetadata("sodium", "Sodium", "0.6.0", "Fabric", "渲染优化"));
        InstanceContent.ReadMetadata(forge).ShouldBe(new ModMetadata("jei", "Just Enough Items", "15.20.0.106", "Forge", null));
        InstanceContent.ReadMetadata(neo).ShouldBe(new ModMetadata("neo_mod", "新 Mod", "1.2.3", "NeoForge", null));
        InstanceContent.ReadMetadata(legacy)!.Name.ShouldBe("Old Mod");
        InstanceContent.ReadMetadata(Jar("plain.jar", ("a.class", ""))).ShouldBeNull();
        InstanceContent.ReadMetadata(Path.Combine(_mc.Root, "mods", "missing.jar")).ShouldBeNull();
    }

    [Fact]
    public void ListsAndTogglesMods()
    {
        Jar("b.jar");
        Jar("a.jar.disabled");
        _mc.File_(@"mods\readme.txt").File_(@"mods\config\x.json");

        var mods = InstanceContent.ListMods(_mc.Root);
        mods.Select(m => (m.BaseName, m.IsEnabled)).ShouldBe([("a.jar", false), ("b.jar", true)]);

        var enabled = InstanceContent.SetEnabled(mods[0], true);
        enabled.FileName.ShouldBe("a.jar");
        File.Exists(enabled.Path).ShouldBeTrue();
        InstanceContent.SetEnabled(InstanceContent.SetEnabled(enabled, false), true).IsEnabled.ShouldBeTrue();

        // 同名文件已存在时不覆盖
        Jar("b.jar.disabled");
        Should.Throw<UserFacingException>(() => InstanceContent.SetEnabled(InstanceContent.ListMods(_mc.Root).First(m => m.FileName == "b.jar"), false));
    }

    [Fact]
    public void ListsSavesAndPacks()
    {
        _mc.File_(@"saves\生存世界\level.dat").File_(@"saves\生存世界\icon.png").File_(@"saves\不是存档\readme.txt")
           .File_(@"resourcepacks\材质包.zip").File_(@"resourcepacks\文件夹包\pack.mcmeta").File_(@"resourcepacks\notes.txt")
           .File_(@"shaderpacks\BSL.zip");

        var saves = InstanceContent.ListSaves(_mc.Root);
        saves.Select(s => s.Name).ShouldBe(["生存世界"]);
        saves[0].IconPath.ShouldNotBeNull();
        // 按当前语言排序（中文系统按拼音），期望顺序用同样的规则计算，测试在英文系统（CI）上也成立
        var expected = new[] { ("材质包", false), ("文件夹包", true) }.OrderBy(p => p.Item1, StringComparer.CurrentCultureIgnoreCase);
        InstanceContent.ListPacks(_mc.Root, "resourcepacks").Select(p => (p.Name, p.IsFolder)).ShouldBe(expected);
        InstanceContent.ListPacks(_mc.Root, "shaderpacks").Single().Name.ShouldBe("BSL");
        InstanceContent.ListSaves(Path.Combine(_mc.Root, "nothing")).ShouldBeEmpty();
    }
}
