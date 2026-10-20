using FCP.Core.Java;
using FCP.Core.Launch;

namespace FCP.Core.Tests.Launch;

public class WindowsCommandLineTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("", "\"\"")]
    [InlineData("C:\\a b\\", "\"C:\\a b\\\\\"")]          // 结尾反斜杠在闭合引号前需要翻倍
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("C:\\no\\space", "C:\\no\\space")]          // 不含空格时反斜杠原样保留
    public void Quote_follows_msvcrt_rules(string input, string expected)
    {
        WindowsCommandLine.Quote(input).ShouldBe(expected);
    }

    [Fact]
    public void Split_is_inverse_of_join()
    {
        string[] args =
        [
            "-Djava.library.path=E:\\MC\\PCL\\.minecraft\\versions\\FarmingTales_ForgeⅡ v1.4.3\\natives-fcp",
            "--version", "香草纪元：食旅纪行", "C:\\trailing slash\\", "quote\"inside", "", "-Dx=a\\\\\"b",
        ];
        WindowsCommandLine.Split(WindowsCommandLine.Join(args)).ShouldBe(args);
    }
}

public class ArgumentDeduplicatorTests
{
    [Fact]
    public void Jvm_keeps_first_identical_and_distinct_pairs()
    {
        var result = ArgumentDeduplicator.DeduplicateJvm(
        [
            "-Xss1M", "--add-opens", "java.base/java.util.jar=x", "--add-opens", "java.base/java.lang.invoke=x",
            "--add-opens", "java.base/java.util.jar=x", "-Xss1M", "-Dfoo=1",
        ]);
        result.ShouldBe(["-Xss1M", "--add-opens", "java.base/java.util.jar=x", "--add-opens", "java.base/java.lang.invoke=x", "-Dfoo=1"]);
    }

    [Fact]
    public void Game_later_value_overrides_except_tweak_class()
    {
        var result = ArgumentDeduplicator.DeduplicateGame(
        [
            "--width", "854", "--tweakClass", "a.A", "--demo", "--width", "1280", "--tweakClass", "b.B", "--demo",
        ]);
        result.ShouldBe(["--width", "1280", "--tweakClass", "a.A", "--demo", "--tweakClass", "b.B"]);
    }

    [Fact]
    public void Negative_numbers_are_treated_as_values()
    {
        ArgumentDeduplicator.DeduplicateGame(["--xPos", "23", "--xPos", "-50"]).ShouldBe(["--xPos", "-50"]);
    }
}

public class JavaReleaseFileTests
{
    [Theory]
    [InlineData("1.8.0_392", 8, 0, 392)]
    [InlineData("17.0.10", 17, 0, 10)]
    [InlineData("21", 21, 0, -1)]
    [InlineData("25.0.2+10", 25, 0, 2)]
    [InlineData("21.0.6", 21, 0, 6)]
    public void Parses_java_versions(string text, int major, int minor, int build)
    {
        var version = JavaReleaseFile.ParseJavaVersion(text)!;
        version.Major.ShouldBe(major);
        version.Minor.ShouldBe(minor);
        version.Build.ShouldBe(build);
    }
}
