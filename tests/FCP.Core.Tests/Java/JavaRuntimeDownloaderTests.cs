using System.Text.Json.Nodes;
using FCP.Core.Java;

namespace FCP.Core.Tests.Java;

public class JavaRuntimeDownloaderTests
{
    // 与 Mojang all.json 结构相同的精简样本
    private static readonly JsonObject All = JsonNode.Parse("""
    {
      "windows-x64": {
        "java-runtime-alpha": [ { "manifest": { "url": "https://piston-meta.mojang.com/alpha.json" }, "version": { "name": "16.0.1+9" } } ],
        "java-runtime-gamma": [ { "manifest": { "url": "https://piston-meta.mojang.com/gamma.json" }, "version": { "name": "17.0.8" } } ],
        "java-runtime-delta": [ { "manifest": { "url": "https://piston-meta.mojang.com/delta.json" }, "version": { "name": "21.0.7" } } ],
        "java-runtime-epsilon": [ { "manifest": { "url": "https://piston-meta.mojang.com/epsilon.json" }, "version": { "name": "25.0.1" } } ],
        "jre-legacy": [ { "manifest": { "url": "https://piston-meta.mojang.com/legacy.json" }, "version": { "name": "8u51-cacert462b08" } } ],
        "minecraft-java-exe": [ { "manifest": { "url": "https://piston-meta.mojang.com/exe.json" }, "version": { "name": "1.0.0" } } ],
        "java-runtime-beta": []
      }
    }
    """)!.AsObject();

    [Theory]
    [InlineData(8, "jre-legacy")]
    [InlineData(16, "java-runtime-alpha")]
    [InlineData(17, "java-runtime-gamma")]
    [InlineData(21, "java-runtime-delta")]
    [InlineData(25, "java-runtime-epsilon")]
    public void ChoosesComponentByMajorVersion(int major, string expected) =>
        JavaRuntimeDownloader.ChooseComponent(All, "windows-x64", major, null)!.Name.ShouldBe(expected);

    [Fact]
    public void UnknownMajorOrPlatform_ReturnsNull()
    {
        JavaRuntimeDownloader.ChooseComponent(All, "windows-x64", 11, null).ShouldBeNull();
        JavaRuntimeDownloader.ChooseComponent(All, "windows-arm64", 17, "java-runtime-gamma").ShouldBeNull();
    }

    [Theory]
    [InlineData("17.0.8", 17)]
    [InlineData("8u51-cacert462b08", 8)]
    [InlineData("1.8.0_202", 8)]
    [InlineData("21.0.7", 21)]
    public void ParsesManifestVersionNames(string name, int major) => JavaRuntimeDownloader.ParseMajor(name).ShouldBe(major);

    [Theory]
    [InlineData("bin/java.exe", true)]
    [InlineData("lib/security/cacerts", true)]
    [InlineData("../evil.exe", false)]
    [InlineData("bin/../../evil.exe", false)]
    [InlineData(@"C:\Windows\evil.exe", false)]
    [InlineData("", false)]
    public void RejectsPathsOutsideTheInstallFolder(string path, bool safe) => JavaRuntimeDownloader.IsSafeRelativePath(path).ShouldBe(safe);
}
