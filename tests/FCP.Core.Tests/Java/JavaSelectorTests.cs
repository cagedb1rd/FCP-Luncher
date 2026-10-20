using FCP.Core.Instances;
using FCP.Core.Java;

namespace FCP.Core.Tests.Java;

public class JavaSelectorTests
{
    private static GameInstance Instance(string mc, int? java, params LoaderInfo[] loaders) => new()
    {
        RootDirectory = @"C:\mc", Name = mc, MinecraftVersion = mc, RecommendedJavaMajor = java,
        VersionType = GameVersionType.Release, Loaders = loaders,
    };

    private static JavaRuntime Java(string version, JavaSource source = JavaSource.Registry, bool is64 = true) => new()
    {
        JavaExecutable = $@"C:\Java\{version}\{source}\bin\java.exe", Version = Version.Parse(version), Source = source, Is64Bit = is64,
    };

    private static readonly JavaRuntime[] Installed = [Java("17.0.10"), Java("21.0.6", JavaSource.Pcl), Java("25.0.2")];

    [Fact]
    public void Forge_1_20_1_prefers_exact_java_17()
    {
        var requirement = JavaSelector.GetRequirement(Instance("1.20.1", 17, new LoaderInfo(LoaderType.Forge, "47.4.10")));
        requirement.MinMajor.ShouldBe(17);
        requirement.MaxMajor.ShouldBe(21);
        JavaSelector.Select(requirement, Installed).Java!.Major.ShouldBe(17);
    }

    [Fact]
    public void Modded_never_picks_far_newer_java()
    {
        var requirement = JavaSelector.GetRequirement(Instance("1.20.1", 17, new LoaderInfo(LoaderType.Forge, "47.4.10")));
        JavaSelector.Select(requirement, [Java("25.0.2")]).Java.ShouldBeNull();
        JavaSelector.Select(requirement, [Java("25.0.2"), Java("21.0.6")]).Java!.Major.ShouldBe(21);
    }

    [Fact]
    public void Vanilla_new_versions_accept_newer_java()
    {
        var requirement = JavaSelector.GetRequirement(Instance("1.21.4", 21));
        requirement.MaxMajor.ShouldBeNull();
        JavaSelector.Select(requirement, [Java("25.0.2")]).Java!.Major.ShouldBe(25);
        JavaSelector.Select(requirement, Installed).Java!.Major.ShouldBe(21);
    }

    [Fact]
    public void Old_versions_require_java_8()
    {
        var requirement = JavaSelector.GetRequirement(Instance("1.12.2", 8, new LoaderInfo(LoaderType.Forge, "14.23.5.2860")));
        requirement.MaxMajor.ShouldBe(8);
        requirement.MojangComponent.ShouldBe("jre-legacy");
        JavaSelector.Select(requirement, Installed).Java.ShouldBeNull();
        JavaSelector.Select(requirement, [.. Installed, Java("8.0.392")]).Java!.Major.ShouldBe(8);
    }

    [Fact]
    public void Old_fabric_loader_rejects_java_25()
    {
        var requirement = JavaSelector.GetRequirement(Instance("1.21.4", 21, new LoaderInfo(LoaderType.Fabric, "0.16.10")));
        requirement.MaxMajor.ShouldBe(24);
        requirement.Reasons.ShouldContain(r => r.Contains("Fabric Loader 0.16.10"));
    }

    [Fact]
    public void Missing_java_version_is_guessed_from_game_version()
    {
        JavaSelector.GetRequirement(Instance("1.18.2", null)).RecommendedMajor.ShouldBe(17);
        JavaSelector.GetRequirement(Instance("1.20.6", null)).RecommendedMajor.ShouldBe(21);
        JavaSelector.GetRequirement(Instance("1.7.10", null)).RecommendedMajor.ShouldBe(8);
        JavaSelector.GetRequirement(Instance("26.1", null)).RecommendedMajor.ShouldBe(25);
    }

    [Fact]
    public void Thirty_two_bit_java_is_never_chosen()
    {
        var requirement = JavaSelector.GetRequirement(Instance("1.12.2", 8));
        JavaSelector.Select(requirement, [Java("8.0.392", is64: false)]).Java.ShouldBeNull();
    }

    [Fact]
    public void Same_major_prefers_reliable_source_over_ide_bundled_java()
    {
        var requirement = JavaSelector.GetRequirement(Instance("1.21.4", 21));
        JavaSelector.Select(requirement, [Java("21.0.6", JavaSource.Pcl), Java("21.0.2", JavaSource.Registry)]).Java!.Source.ShouldBe(JavaSource.Registry);
    }

    [Fact]
    public void Explanation_mentions_reason_and_choice()
    {
        var choice = JavaSelector.Select(JavaSelector.GetRequirement(Instance("1.20.1", 17)), Installed);
        choice.Explanation.ShouldContain("版本文件声明需要 Java 17");
        choice.Explanation.ShouldContain("已选择 Java 17.0.10");
    }
}
