using FCP.Core.Launch;

namespace FCP.Core.Tests.Launch;

public class MemoryCalculatorTests
{
    [Fact]
    public void Vanilla_Gets2Point5Gb()
    {
        var (mb, reason) = MemoryCalculator.Auto(0, availableMb: 16000, totalMb: 32000);
        mb.ShouldBe(2560);
        reason.ShouldContain("原版");
    }

    [Fact]
    public void Mods_Add16MbEach()
    {
        MemoryCalculator.Auto(316, availableMb: 20000, totalMb: 32000).Megabytes.ShouldBe(2560 + 316 * 16);
    }

    [Fact]
    public void LeavesTwoGigabytesForTheSystem()
    {
        var (mb, reason) = MemoryCalculator.Auto(316, availableMb: 6000, totalMb: 16000);
        mb.ShouldBe(6000 - 2048);
        reason.ShouldContain("可用内存");
    }

    [Fact]
    public void NeverBelowOneGigabyte_NorAbove16()
    {
        MemoryCalculator.Auto(50, availableMb: 1500, totalMb: 4000).Megabytes.ShouldBe(MemoryCalculator.MinimumMb);
        MemoryCalculator.Auto(5000, availableMb: 60000, totalMb: 64000).Megabytes.ShouldBe(MemoryCalculator.AutoMaximumMb);
    }

    [Fact]
    public void CountMods_CountsOnlyEnabledModFiles()
    {
        using var mc = new TempMinecraft();
        var mods = Path.Combine(mc.Root, "mods");
        Directory.CreateDirectory(mods);
        foreach (var name in new[] { "a.jar", "b.JAR", "c.zip", "d.litemod", "e.jar.disabled", "readme.txt" })
            File.WriteAllText(Path.Combine(mods, name), "");
        Directory.CreateDirectory(Path.Combine(mods, "config.jar"));

        MemoryCalculator.CountMods(mc.Root).ShouldBe(4);
        MemoryCalculator.CountMods(Path.Combine(mc.Root, "missing")).ShouldBe(0);
    }
}
