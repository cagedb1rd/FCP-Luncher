namespace FCP.Core.Launch;

/// <summary>
/// 自动内存分配（自己的算法，docs/02 §4 要求不明显低于 PCL）：
/// 原版 2.5 GB 起；有 Mod 时每个 Mod 再加 16 MB；不超过“可用内存 − 2 GB”，也不超过 16 GB；最少 1 GB。
/// </summary>
public static class MemoryCalculator
{
    public const int MinimumMb = 1024;
    public const int AutoMaximumMb = 16 * 1024;

    public static (int Megabytes, string Reason) Auto(int modCount, long availableMb, long totalMb)
    {
        var need = 2560 + modCount * 16;
        var reason = modCount > 0 ? $"{modCount} 个 Mod 建议约 {need / 1024.0:0.#} GB" : $"原版建议约 {need / 1024.0:0.#} GB";
        var limit = (int)Math.Min(AutoMaximumMb, Math.Max(MinimumMb, availableMb - 2048));
        if (need > limit)
        {
            reason += $"，但电脑当前只剩 {availableMb / 1024.0:0.#} GB 可用内存，分配 {limit / 1024.0:0.#} GB";
            need = limit;
        }
        _ = totalMb;
        return (Math.Max(MinimumMb, need), reason);
    }

    /// <summary>统计 mods 文件夹中启用的 Mod 数量（.jar / .zip / .litemod）。</summary>
    public static int CountMods(string gameDirectory)
    {
        var mods = Path.Combine(gameDirectory, "mods");
        if (!Directory.Exists(mods))
            return 0;
        return Directory.EnumerateFiles(mods).Count(f =>
            f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".litemod", StringComparison.OrdinalIgnoreCase));
    }
}
