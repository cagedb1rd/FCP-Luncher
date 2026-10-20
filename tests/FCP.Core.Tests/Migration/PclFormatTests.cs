using FCP.Core.Migration.Pcl;

namespace FCP.Core.Tests.Migration;

public class PclFormatTests
{
    [Theory]
    [InlineData(0, 307)]       // 0.3 GB
    [InlineData(12, 1536)]     // 1.5 GB
    [InlineData(15, 3072)]     // 默认值：3 GB
    [InlineData(25, 8192)]     // 8 GB
    [InlineData(27, 10240)]    // 本机 FarmingTales：10 GB（与 LatestLaunch.bat 中的 -Xmx10240m 一致）
    [InlineData(33, 16384)]    // 16 GB
    [InlineData(34, 18432)]    // 本机全局设置：18 GB
    public void Ram_scale_converts_to_megabytes(int scale, int expectedMb)
    {
        PclRamScale.ToMegabytes(scale).ShouldBe(expectedMb);
    }

    [Fact]
    public void Ini_parses_key_value_lines_with_colons_in_values()
    {
        var ini = PclIniFile.Parse(
            "VersionServerAuthServer:https://littleskin.cn/api/yggdrasil\r\n" +
            "VersionArgumentIndieV2:True\r\n" +
            "VersionRamCustom:27\r\n" +
            "Info:正式版 1.20.1, Forge 47.4.10, Authlib 验证\r\n" +
            "LaunchFolderSelect:$.minecraft\\\r\n" +
            "无效行\r\n");

        ini.GetString("VersionServerAuthServer").ShouldBe("https://littleskin.cn/api/yggdrasil");
        ini.GetBool("VersionArgumentIndieV2").ShouldBe(true);
        ini.GetInt("VersionRamCustom").ShouldBe(27);
        ini.GetString("Info").ShouldBe("正式版 1.20.1, Forge 47.4.10, Authlib 验证");
        ini.GetString("LaunchFolderSelect").ShouldBe("$.minecraft\\");
        ini.Contains("无效行").ShouldBeFalse();
        ini.GetString("Missing").ShouldBeNull();
    }

    [Fact]
    public async Task Ini_reads_utf8_file_with_fullwidth_characters()
    {
        var file = Path.Combine(Path.GetTempPath(), $"fcp-test-{Guid.NewGuid():N}.ini");
        try
        {
            await File.WriteAllTextAsync(file, "Version:FarmingTales_ForgeⅡ v1.4.3\nCardValue1:香草纪元：食旅纪行:\n", new System.Text.UTF8Encoding(false), TestContext.Current.CancellationToken);
            var ini = await PclIniFile.LoadAsync(file, TestContext.Current.CancellationToken);
            ini.GetString("Version").ShouldBe("FarmingTales_ForgeⅡ v1.4.3");
            ini.GetString("CardValue1").ShouldBe("香草纪元：食旅纪行:");
        }
        finally
        {
            File.Delete(file);
        }
    }
}
